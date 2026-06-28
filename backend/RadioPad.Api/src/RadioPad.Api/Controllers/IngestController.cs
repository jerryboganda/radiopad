using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD INT-001..004 — inbound order webhook for upstream HIS / RIS / EMR
/// integrations. Accepts FHIR <c>ServiceRequest</c> + <c>Patient</c> bundles
/// or a small RadioPad-native JSON envelope and creates a Draft report so the
/// radiologist sees the case the moment it arrives.
///
/// Authentication is a per-tenant shared bearer secret stored in
/// <see cref="TenantSettings.IngestBearerSecret"/>. The request must carry:
/// <list type="bullet">
///   <item><c>X-RadioPad-Tenant: &lt;slug&gt;</c></item>
///   <item><c>Authorization: Bearer &lt;secret&gt;</c></item>
/// </list>
/// Comparison is constant-time. Successful ingest writes
/// <see cref="AuditAction.OrderIngested"/>; bad/missing tokens write
/// <see cref="AuditAction.PolicyViolation"/> when the tenant exists.
/// </summary>
[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public IngestController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    public record IngestOrderDto(
        string AccessionNumber,
        string Modality,
        string BodyPart,
        string? Indication,
        string? Comparison,
        string? PatientRef,
        string? RulebookId);

    [HttpPost("order")]
    public async Task<IActionResult> Order([FromBody] IngestOrderDto dto, CancellationToken ct)
    {
        var slug = Request.Headers["X-RadioPad-Tenant"].ToString();
        if (string.IsNullOrEmpty(slug))
            return Unauthorized(new { error = "Missing tenant header.", kind = "unauthenticated" });
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
            return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var expected = settings?.IngestBearerSecret ?? "";
        if (string.IsNullOrEmpty(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Ingest is not configured for this tenant.", kind = "provider_unavailable" });

        var auth = Request.Headers["Authorization"].ToString();
        var presented = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : "";
        if (!FixedTimeEquals(expected, presented))
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason = "ingest:bad_bearer" }),
            }, ct);
            return Unauthorized(new { error = "Invalid bearer token.", kind = "unauthenticated" });
        }

        if (string.IsNullOrWhiteSpace(dto.AccessionNumber) || string.IsNullOrWhiteSpace(dto.Modality))
            return BadRequest(new { error = "accessionNumber and modality are required.", kind = "validation" });

        // Idempotency: if an order with this accession already exists for the
        // tenant, return it instead of creating a duplicate. Upstream RIS
        // sometimes resends on retry.
        var existing = await _db.Reports.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id && r.Study.AccessionNumber == dto.AccessionNumber, ct);
        if (existing is not null)
        {
            return Ok(new { id = existing.Id, deduplicated = true });
        }

        var report = new Report
        {
            TenantId = tenant.Id,
            Status = ReportStatus.Draft,
            Indication = dto.Indication ?? "",
            Comparison = dto.Comparison ?? "",
            RulebookId = Guid.TryParse(dto.RulebookId, out var rbId) ? rbId : null,
            Study = new StudyContext
            {
                AccessionNumber = dto.AccessionNumber,
                Modality = dto.Modality,
                BodyPart = dto.BodyPart ?? "",
                // Iter-36 — study-context Indication removed; report-body Indication (set above) is canonical.
                Comparison = dto.Comparison ?? "",
            },
        };
        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            ReportId = report.Id,
            Action = AuditAction.OrderIngested,
            DetailsJson = JsonSerializer.Serialize(new
            {
                accession = dto.AccessionNumber,
                modality = dto.Modality,
                source = "webhook",
            }),
        }, ct);

        return Ok(new { id = report.Id, deduplicated = false });
    }

    /// <summary>
    /// PRD INT-002 — accept a FHIR R4 <c>ServiceRequest</c> resource (or a
    /// <c>Bundle</c> containing one) and translate it into the same Draft
    /// report. Same auth as <c>POST /api/ingest/order</c>. Modality is read
    /// from <c>category</c> (text) or <c>code.coding[0].display</c>; body part
    /// from <c>bodySite[0].text</c> or <c>bodySite[0].coding[0].display</c>;
    /// accession from <c>identifier[0].value</c>.
    /// </summary>
    [HttpPost("fhir/servicerequest")]
    [Consumes("application/json", "application/fhir+json")]
    public async Task<IActionResult> FhirServiceRequest(CancellationToken ct)
    {
        var slug = Request.Headers["X-RadioPad-Tenant"].ToString();
        if (string.IsNullOrEmpty(slug))
            return Unauthorized(new { error = "Missing tenant header.", kind = "unauthenticated" });
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
            return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var expected = settings?.IngestBearerSecret ?? "";
        if (string.IsNullOrEmpty(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Ingest is not configured for this tenant.", kind = "provider_unavailable" });
        var auth = Request.Headers["Authorization"].ToString();
        var presented = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : "";
        if (!FixedTimeEquals(expected, presented))
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason = "ingest:bad_bearer" }),
            }, ct);
            return Unauthorized(new { error = "Invalid bearer token.", kind = "unauthenticated" });
        }

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!await VerifyOptionalSignatureAsync(tenant.Id, settings, raw, ct))
            return Unauthorized(new { error = "Invalid webhook signature.", kind = "unauthenticated" });
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { return BadRequest(new { error = "Body is not valid JSON.", kind = "validation" }); }

        // Accept either a bare ServiceRequest or a Bundle containing one.
        var sr = ResolveServiceRequest(doc.RootElement);
        if (sr is null)
            return BadRequest(new { error = "No FHIR ServiceRequest found in payload.", kind = "validation" });

        var parsed = ParseServiceRequest(sr.Value);
        if (string.IsNullOrEmpty(parsed.AccessionNumber) || string.IsNullOrEmpty(parsed.Modality))
            return BadRequest(new { error = "ServiceRequest missing accession (identifier.value) or modality.", kind = "validation" });

        // Iter-30 — capture the originating ServiceRequest reference so a
        // subsequent FHIR DiagnosticReport import can be correlated back.
        string? srRef = null;
        if (sr.Value.TryGetProperty("id", out var srId) && srId.ValueKind == JsonValueKind.String)
            srRef = $"ServiceRequest/{srId.GetString()}";

        var existing = await _db.Reports.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id && r.Study.AccessionNumber == parsed.AccessionNumber, ct);
        if (existing is not null)
            return Ok(new { id = existing.Id, deduplicated = true });

        var report = new Report
        {
            TenantId = tenant.Id,
            Status = ReportStatus.Draft,
            Indication = parsed.Indication,
            Comparison = "",
            ServiceRequestRef = srRef,
            Study = new StudyContext
            {
                AccessionNumber = parsed.AccessionNumber,
                Modality = parsed.Modality,
                BodyPart = parsed.BodyPart,
                // Iter-36 — study-context Indication removed; report-body Indication (set above) is canonical.
                Comparison = "",
            },
        };
        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            ReportId = report.Id,
            Action = AuditAction.OrderIngested,
            DetailsJson = JsonSerializer.Serialize(new
            {
                accession = parsed.AccessionNumber,
                modality = parsed.Modality,
                source = "fhir",
            }),
        }, ct);
        return Ok(new { id = report.Id, deduplicated = false });
    }

    /// <summary>
    /// Iter-30 — accept a FHIR R4 <c>DiagnosticReport</c> and create a Draft
    /// report keyed by tenant. Optional <c>basedOn[0].reference</c> is stored
    /// as <see cref="Report.ServiceRequestRef"/> so an order placed via
    /// <see cref="FhirServiceRequest"/> can be correlated with its eventual
    /// diagnostic report. Audited as <see cref="AuditAction.ReportImported"/>.
    /// </summary>
    [HttpPost("fhir/diagnosticreport")]
    [Consumes("application/json", "application/fhir+json")]
    public async Task<IActionResult> FhirDiagnosticReport(CancellationToken ct)
    {
        var slug = Request.Headers["X-RadioPad-Tenant"].ToString();
        if (string.IsNullOrEmpty(slug))
            return Unauthorized(new { error = "Missing tenant header.", kind = "unauthenticated" });
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
            return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var expected = settings?.IngestBearerSecret ?? "";
        if (string.IsNullOrEmpty(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Ingest is not configured for this tenant.", kind = "provider_unavailable" });
        var auth = Request.Headers["Authorization"].ToString();
        var presented = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : "";
        if (!FixedTimeEquals(expected, presented))
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason = "ingest:bad_bearer" }),
            }, ct);
            return Unauthorized(new { error = "Invalid bearer token.", kind = "unauthenticated" });
        }

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!await VerifyOptionalSignatureAsync(tenant.Id, settings, raw, ct))
            return Unauthorized(new { error = "Invalid webhook signature.", kind = "unauthenticated" });
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { return BadRequest(new { error = "Body is not valid JSON.", kind = "validation" }); }

        var dr = ResolveDiagnosticReport(doc.RootElement);
        if (dr is null)
            return BadRequest(new { error = "No FHIR DiagnosticReport found in payload.", kind = "validation" });

        var parsedDr = ParseDiagnosticReport(dr.Value);
        if (string.IsNullOrEmpty(parsedDr.AccessionNumber))
            return BadRequest(new { error = "DiagnosticReport missing accession (identifier.value).", kind = "validation" });

        var existingDr = await _db.Reports.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id && r.Study.AccessionNumber == parsedDr.AccessionNumber, ct);
        if (existingDr is not null)
        {
            return Ok(new { id = existingDr.Id, deduplicated = true });
        }

        var importedReport = new Report
        {
            TenantId = tenant.Id,
            Status = ReportStatus.Draft,
            Findings = parsedDr.Findings,
            Impression = parsedDr.Impression,
            ServiceRequestRef = parsedDr.BasedOnRef,
            Study = new StudyContext
            {
                AccessionNumber = parsedDr.AccessionNumber,
                Modality = parsedDr.Modality,
                BodyPart = parsedDr.BodyPart,
            },
        };
        _db.Reports.Add(importedReport);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            ReportId = importedReport.Id,
            Action = AuditAction.ReportImported,
            DetailsJson = JsonSerializer.Serialize(new
            {
                accession = parsedDr.AccessionNumber,
                modality = parsedDr.Modality,
                source = "fhir-diagnosticreport",
                basedOn = parsedDr.BasedOnRef,
            }),
        }, ct);
        return Ok(new { id = importedReport.Id, deduplicated = false });
    }

    private static JsonElement? ResolveDiagnosticReport(JsonElement root)
    {
        if (!root.TryGetProperty("resourceType", out var rt)) return null;
        var type = rt.GetString();
        if (type == "DiagnosticReport") return root;
        if (type == "Bundle" && root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                if (e.TryGetProperty("resource", out var res)
                    && res.TryGetProperty("resourceType", out var t)
                    && t.GetString() == "DiagnosticReport")
                    return res;
            }
        }
        return null;
    }

    public record ParsedDr(
        string AccessionNumber,
        string Modality,
        string BodyPart,
        string Findings,
        string Impression,
        string? BasedOnRef);

    /// <summary>Iter-30 fix B1: exposed so the session-auth admin import
    /// endpoint on <c>ReportsLifecycleController</c> can reuse the same
    /// parser without duplicating logic.</summary>
    public static JsonElement? ResolveDiagnosticReportPublic(JsonElement root) => ResolveDiagnosticReport(root);

    /// <summary>Iter-30 fix B1: see <see cref="ResolveDiagnosticReportPublic"/>.</summary>
    public static ParsedDr ParseDiagnosticReportPublic(JsonElement dr) => ParseDiagnosticReport(dr);

    private static ParsedDr ParseDiagnosticReport(JsonElement dr)
    {
        var accession = "";
        if (dr.TryGetProperty("identifier", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0)
        {
            if (ids[0].TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                accession = v.GetString() ?? "";
        }
        var modality = "";
        if (dr.TryGetProperty("code", out var code))
        {
            if (code.TryGetProperty("coding", out var coding) && coding.ValueKind == JsonValueKind.Array && coding.GetArrayLength() > 0)
            {
                if (coding[0].TryGetProperty("display", out var d)) modality = d.GetString() ?? "";
                if (string.IsNullOrEmpty(modality) && coding[0].TryGetProperty("code", out var c)) modality = c.GetString() ?? "";
            }
            if (string.IsNullOrEmpty(modality) && code.TryGetProperty("text", out var ct2))
                modality = ct2.GetString() ?? "";
        }
        var bodyPart = "";
        if (dr.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.Array && cat.GetArrayLength() > 0)
        {
            if (cat[0].TryGetProperty("text", out var t)) bodyPart = t.GetString() ?? "";
        }
        var findings = "";
        var impression = "";
        if (dr.TryGetProperty("conclusion", out var concl) && concl.ValueKind == JsonValueKind.String)
            impression = concl.GetString() ?? "";
        if (dr.TryGetProperty("presentedForm", out var pfs) && pfs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pf in pfs.EnumerateArray())
            {
                if (pf.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(data.GetString() ?? "");
                        findings = Encoding.UTF8.GetString(bytes);
                        break;
                    }
                    catch (FormatException) { /* not base64 — skip */ }
                }
            }
        }
        string? basedOnRef = null;
        if (dr.TryGetProperty("basedOn", out var basedOn) && basedOn.ValueKind == JsonValueKind.Array && basedOn.GetArrayLength() > 0)
        {
            if (basedOn[0].TryGetProperty("reference", out var refn) && refn.ValueKind == JsonValueKind.String)
                basedOnRef = refn.GetString();
        }
        return new ParsedDr(accession.Trim(), modality.Trim(), bodyPart.Trim(), findings.Trim(), impression.Trim(), basedOnRef);
    }

    private record ParsedSr(string AccessionNumber, string Modality, string BodyPart, string Indication);

    private static JsonElement? ResolveServiceRequest(JsonElement root)
    {
        if (!root.TryGetProperty("resourceType", out var rt)) return null;
        var type = rt.GetString();
        if (type == "ServiceRequest") return root;
        if (type == "Bundle" && root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                if (e.TryGetProperty("resource", out var res)
                    && res.TryGetProperty("resourceType", out var t)
                    && t.GetString() == "ServiceRequest")
                    return res;
            }
        }
        return null;
    }

    private static ParsedSr ParseServiceRequest(JsonElement sr)
    {
        var accession = "";
        if (sr.TryGetProperty("identifier", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0)
        {
            if (ids[0].TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                accession = v.GetString() ?? "";
        }

        var modality = "";
        // Prefer `code.coding[0].display`.
        if (sr.TryGetProperty("code", out var code))
        {
            if (code.TryGetProperty("coding", out var coding) && coding.ValueKind == JsonValueKind.Array && coding.GetArrayLength() > 0)
            {
                if (coding[0].TryGetProperty("display", out var d)) modality = d.GetString() ?? "";
                if (string.IsNullOrEmpty(modality) && coding[0].TryGetProperty("code", out var c)) modality = c.GetString() ?? "";
            }
            if (string.IsNullOrEmpty(modality) && code.TryGetProperty("text", out var ct2))
                modality = ct2.GetString() ?? "";
        }
        if (string.IsNullOrEmpty(modality) && sr.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.Array && cat.GetArrayLength() > 0)
        {
            if (cat[0].TryGetProperty("text", out var t)) modality = t.GetString() ?? "";
        }

        var bodyPart = "";
        if (sr.TryGetProperty("bodySite", out var bs) && bs.ValueKind == JsonValueKind.Array && bs.GetArrayLength() > 0)
        {
            if (bs[0].TryGetProperty("text", out var t)) bodyPart = t.GetString() ?? "";
            if (string.IsNullOrEmpty(bodyPart) && bs[0].TryGetProperty("coding", out var bc) && bc.ValueKind == JsonValueKind.Array && bc.GetArrayLength() > 0)
            {
                if (bc[0].TryGetProperty("display", out var d)) bodyPart = d.GetString() ?? "";
            }
        }

        var indication = "";
        if (sr.TryGetProperty("reasonCode", out var rc) && rc.ValueKind == JsonValueKind.Array && rc.GetArrayLength() > 0)
        {
            if (rc[0].TryGetProperty("text", out var t)) indication = t.GetString() ?? "";
        }
        if (string.IsNullOrEmpty(indication) && sr.TryGetProperty("note", out var notes) && notes.ValueKind == JsonValueKind.Array && notes.GetArrayLength() > 0)
        {
            if (notes[0].TryGetProperty("text", out var t)) indication = t.GetString() ?? "";
        }

        return new ParsedSr(accession.Trim(), modality.Trim(), bodyPart.Trim(), indication.Trim());
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    /// <summary>
    /// Iter-31 INT-005 — verifies the optional <c>X-RadioPad-Signature:
    /// sha256=&lt;hex&gt;</c> header against an HMAC-SHA256 of the raw request
    /// body using <see cref="TenantSettings.FhirWebhookSecret"/>. When the
    /// tenant has not configured a webhook secret, returns true (back-compat
    /// bearer-only flow). When configured, the signature must validate;
    /// failures are audited as <see cref="AuditAction.PolicyViolation"/> with
    /// reason <c>fhir-webhook:bad_signature</c>.
    /// </summary>
    private async Task<bool> VerifyOptionalSignatureAsync(
        Guid tenantId, TenantSettings? settings, string rawBody, CancellationToken ct)
    {
        var secret = settings?.FhirWebhookSecret ?? "";
        if (string.IsNullOrEmpty(secret)) return true;

        var header = Request.Headers["X-RadioPad-Signature"].ToString();
        const string prefix = "sha256=";
        var presentedHex = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : "";

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody ?? ""));
        var expectedHex = Convert.ToHexString(expected).ToLowerInvariant();

        var presentedBytes = Encoding.UTF8.GetBytes(presentedHex.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        var ok = presentedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);

        if (!ok)
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenantId,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason = "fhir-webhook:bad_signature" }),
            }, ct);
        }
        return ok;
    }
}
