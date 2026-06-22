using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-30 — additional report-lifecycle endpoints: rewrite modes (RPT-007),
/// multi-radiologist sign-off + addendum. Lives alongside
/// <see cref="ReportsController"/> as a partial / second controller class so
/// the original file stays focused on draft + validate + export.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsLifecycleController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IReportRewriteService _rewrite;

    public ReportsLifecycleController(RadioPadDbContext db, IAuditLog audit, IReportRewriteService rewrite)
    {
        _db = db;
        _audit = audit;
        _rewrite = rewrite;
    }

    public record RewriteDto(string Mode, string[]? Sections, Guid? ProviderId);

    /// <summary>RPT-007 — multi-mode rewrite. Returns the rewritten text;
    /// frontend handles accept/reject. PHI policy is enforced inside the
    /// gateway and is never bypassed by mode selection.</summary>
    [HttpPost("{id:guid}/rewrite")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> Rewrite(Guid id, [FromBody] RewriteDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Mode)
            || !TryParseMode(dto.Mode, out var mode))
        {
            return BadRequest(new
            {
                error = $"Unsupported rewrite mode '{dto.Mode}'. Supported: concise, formal, patient_friendly, referring_summary.",
                kind = "validation",
            });
        }

        var providerQuery = _db.Providers.Where(p => p.TenantId == tenant.Id && p.Enabled);
        // When no provider is pinned, default to the highest-Quality enabled provider
        // (Priority as the tie-break) so rewrite agrees with the drafting router's
        // Quality-first intent — e.g. Gemini (Quality 0.9) wins over DeepSeek (0.85) —
        // instead of a raw Priority sort that could diverge if priorities change. PHI
        // compliance is still enforced downstream in the rewrite service, unchanged.
        ProviderConfig? provider = dto.ProviderId is { } pid && pid != Guid.Empty
            ? await providerQuery.FirstOrDefaultAsync(p => p.Id == pid, ct)
            // Order in memory (like EfProviderRouter): SQLite stores decimal as TEXT and
            // cannot ORDER BY it server-side, so Quality must be ranked client-side.
            : (await providerQuery.ToListAsync(ct))
                .OrderByDescending(p => p.Quality).ThenBy(p => p.Priority).FirstOrDefault();
        if (provider is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "No enabled AI provider available for this tenant.", kind = "provider_unavailable" });

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.AiRequest,
            DetailsJson = JsonSerializer.Serialize(new
            {
                kind = "rewrite",
                mode = mode.ToString(),
                sections = dto.Sections,
                providerId = provider.Id,
            }),
        }, ct);

        try
        {
            var result = await _rewrite.RewriteAsync(tenant, report, provider, mode, dto.Sections, ct);
            return Ok(new
            {
                text = result.Text,
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                promptVersion = result.PromptVersion,
                mode = mode.ToString(),
            });
        }
        catch (ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = pex.Message, kind = "provider_policy" });
        }
    }

    public record SignDto(string Role, string? Note);

    /// <summary>Multi-radiologist sign-off. The first signature must be
    /// <c>Primary</c>; <c>CoSigner</c> and <c>Addendum</c> are only
    /// permitted after a Primary signature exists.</summary>
    [HttpPost("{id:guid}/sign")]
    public async Task<IActionResult> Sign(Guid id, [FromBody] SignDto dto, CancellationToken ct)
    {
        // Iter-33 PERF-004 — record sign-endpoint duration for SLO budget.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await SignCoreAsync(id, dto, ct);
        }
        finally
        {
            sw.Stop();
            Services.PerfBudgets.SignDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("role", dto?.Role ?? "(none)"));
        }
    }

    private async Task<IActionResult> SignCoreAsync(Guid id, SignDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsSign);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (!Enum.TryParse<SignatureRole>(dto.Role, ignoreCase: true, out var role))
        {
            return BadRequest(new
            {
                error = $"Unknown signature role '{dto.Role}'. Supported: Primary, CoSigner, Addendum.",
                kind = "validation",
            });
        }

        var existing = await _db.ReportSignatures
            .Where(s => s.TenantId == tenant.Id && s.ReportId == report.Id)
            .ToListAsync(ct);

        if (role == SignatureRole.Primary && existing.Any(s => s.Role == SignatureRole.Primary))
        {
            return Conflict(new
            {
                error = "Report already has a Primary signature.",
                kind = "report_state",
            });
        }
        if (role != SignatureRole.Primary && !existing.Any(s => s.Role == SignatureRole.Primary))
        {
            return Conflict(new
            {
                error = "A Primary signature is required before adding CoSigner or Addendum signatures.",
                kind = "report_state",
            });
        }

        var sig = new ReportSignature
        {
            ReportId = report.Id,
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = role,
            Note = dto.Note,
            SignedAt = DateTimeOffset.UtcNow,
        };
        sig.Hash = ComputeHash(sig);
        _db.ReportSignatures.Add(sig);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.ReportSigned,
            DetailsJson = JsonSerializer.Serialize(new { role = role.ToString(), signatureId = sig.Id }),
        }, ct);
        return Ok(new
        {
            id = sig.Id,
            role = sig.Role.ToString(),
            signedAt = sig.SignedAt,
            hash = sig.Hash,
        });
    }

    public record AddendumDto(string Body);

    /// <summary>Append an addendum body to a previously-signed report. Creates
    /// a new <see cref="ReportVersion"/> with <c>IsAddendum=true</c>; the
    /// report itself is not mutated.</summary>
    [HttpPost("{id:guid}/addendum")]
    public async Task<IActionResult> Addendum(Guid id, [FromBody] AddendumDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsSign);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest(new { error = "body is required.", kind = "validation" });

        var hasPrimary = await _db.ReportSignatures.AnyAsync(
            s => s.TenantId == tenant.Id && s.ReportId == report.Id && s.Role == SignatureRole.Primary, ct);
        if (!hasPrimary)
        {
            return Conflict(new
            {
                error = "Report must be signed (Primary) before appending an addendum.",
                kind = "report_state",
            });
        }

        var nextSeq = await _db.ReportVersions.Where(v => v.ReportId == report.Id).CountAsync(ct);
        var version = new ReportVersion
        {
            ReportId = report.Id,
            Sequence = nextSeq + 1,
            AuthorUserId = user.Id,
            Action = "addendum",
            IsAddendum = true,
            RulebookId = report.RulebookId,
            SnapshotJson = JsonSerializer.Serialize(new { addendum = dto.Body, appendedAt = DateTimeOffset.UtcNow }),
        };
        _db.ReportVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.ReportAddendumAppended,
            DetailsJson = JsonSerializer.Serialize(new { versionId = version.Id, sequence = version.Sequence }),
        }, ct);
        return Ok(new { id = version.Id, sequence = version.Sequence, isAddendum = true });
    }

    /// <summary>List signatures attached to a report.</summary>
    [HttpGet("{id:guid}/signatures")]
    public async Task<IActionResult> Signatures(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var sigs = await _db.ReportSignatures
            .Where(s => s.TenantId == tenant.Id && s.ReportId == id)
            .OrderBy(s => s.SignedAt)
            .Select(s => new
            {
                id = s.Id,
                userId = s.UserId,
                role = s.Role.ToString(),
                signedAt = s.SignedAt,
                note = s.Note,
                hash = s.Hash,
            })
            .ToListAsync(ct);
        return Ok(sigs);
    }

    /// <summary>Iter-30 fix B1/B4 — session-authenticated admin endpoint that
    /// imports a FHIR R4 <c>DiagnosticReport</c> as a Draft for the active
    /// tenant. Differs from <c>POST /api/ingest/fhir/diagnosticreport</c>
    /// (which is a tenant-bearer-secret machine ingress) in that it is gated
    /// by RBAC and uses the radiologist's session token. Reuses the parser
    /// from <see cref="IngestController"/>; returns <c>{ reportId, status, deduplicated }</c>.</summary>
    [HttpPost("import/fhir")]
    [Consumes("application/json", "application/fhir+json")]
    public async Task<IActionResult> ImportFhirDiagnosticReport(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector,
            UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return BadRequest(new { error = "Body is required.", kind = "validation" });
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { return BadRequest(new { error = "Body is not valid JSON.", kind = "validation" }); }

        var dr = IngestController.ResolveDiagnosticReportPublic(doc.RootElement);
        if (dr is null)
            return BadRequest(new { error = "No FHIR DiagnosticReport found in payload.", kind = "validation" });

        var parsed = IngestController.ParseDiagnosticReportPublic(dr.Value);
        if (string.IsNullOrEmpty(parsed.AccessionNumber))
            return BadRequest(new { error = "DiagnosticReport missing accession (identifier.value).", kind = "validation" });

        var existing = await _db.Reports.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id && r.Study.AccessionNumber == parsed.AccessionNumber, ct);
        if (existing is not null)
        {
            return Ok(new
            {
                reportId = existing.Id,
                status = existing.Status.ToString(),
                deduplicated = true,
            });
        }

        var imported = new Report
        {
            TenantId = tenant.Id,
            Status = ReportStatus.Draft,
            Findings = parsed.Findings,
            Impression = parsed.Impression,
            ServiceRequestRef = parsed.BasedOnRef,
            Study = new StudyContext
            {
                AccessionNumber = parsed.AccessionNumber,
                Modality = parsed.Modality,
                BodyPart = parsed.BodyPart,
            },
        };
        _db.Reports.Add(imported);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = imported.Id,
            Action = AuditAction.ReportImported,
            DetailsJson = JsonSerializer.Serialize(new
            {
                accession = parsed.AccessionNumber,
                modality = parsed.Modality,
                source = "admin-fhir-diagnosticreport",
                basedOn = parsed.BasedOnRef,
            }),
        }, ct);
        return Ok(new
        {
            reportId = imported.Id,
            status = imported.Status.ToString(),
            deduplicated = false,
        });
    }

    private static bool TryParseMode(string raw, out ReportRewriteMode mode)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "concise": mode = ReportRewriteMode.Concise; return true;
            case "formal": mode = ReportRewriteMode.Formal; return true;
            case "patient_friendly":
            case "patient-friendly": mode = ReportRewriteMode.PatientFriendly; return true;
            case "referring_summary":
            case "referring-summary": mode = ReportRewriteMode.ReferringSummary; return true;
            default: mode = default; return false;
        }
    }

    private static string ComputeHash(ReportSignature s)
    {
        var raw = $"{s.Id}|{s.ReportId}|{s.UserId}|{(int)s.Role}|{s.SignedAt:o}|{s.Note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
