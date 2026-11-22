using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-35 — manages versioned <see cref="ValidationPack"/> rows. Wraps the
/// existing on-disk fixture format under <c>rulebooks/_tests/&lt;rulebook_id&gt;/</c>
/// so packs can be imported / exported / executed against the rulebook
/// loader and <see cref="ReportValidator"/>.
/// </summary>
public class ValidationPackService
{
    private readonly RadioPadDbContext _db;
    private readonly IRulebookStore _rulebooks;
    private readonly ReportValidator _validator;
    private readonly IAuditLog _audit;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public ValidationPackService(
        RadioPadDbContext db,
        IRulebookStore rulebooks,
        ReportValidator validator,
        IAuditLog audit)
    {
        _db = db;
        _rulebooks = rulebooks;
        _validator = validator;
        _audit = audit;
    }

    /// <summary>Strongly-typed payload for a single golden case.</summary>
    public record GoldenCaseDto(string Name, Report Report, string[] ExpectFlagged);

    public record RunFailureDto(string CaseName, string[] Missing, string[] Unexpected);

    public record RunSummaryDto(int Passed, int Failed, int TotalCases, IReadOnlyList<RunFailureDto> Failures);

    /// <summary>
    /// Imports a directory of <c>*.json</c> golden case fixtures (matching the
    /// existing <c>rulebooks/_tests/&lt;rulebook_id&gt;/</c> on-disk format)
    /// into a new <see cref="ValidationPack"/> row in the supplied tenant.
    /// </summary>
    public async Task<ValidationPack> ImportFromDirectoryAsync(
        Tenant tenant,
        User user,
        string rulebookId,
        string version,
        string name,
        DirectoryInfo dir,
        CancellationToken ct)
    {
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Validation pack directory '{dir.FullName}' not found.");

        var cases = new List<JsonElement>();
        foreach (var file in dir.EnumerateFiles("*.json").OrderBy(f => f.Name))
        {
            await using var s = file.OpenRead();
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            cases.Add(doc.RootElement.Clone());
        }
        var json = JsonSerializer.Serialize(cases, JsonOpts);
        return await CreateAsync(tenant, user, rulebookId, version, name, json, ct);
    }

    /// <summary>
    /// Inserts a new pack row from an in-memory payload (used by the API +
    /// CLI). Throws <see cref="InvalidOperationException"/> when a row with
    /// the same (RulebookId, Version) already exists for the tenant — the
    /// controller surfaces this as a 409 with <c>kind: "validation_packs"</c>.
    /// </summary>
    public async Task<ValidationPack> CreateAsync(
        Tenant tenant,
        User user,
        string rulebookId,
        string version,
        string name,
        string goldenCasesJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rulebookId))
            throw new ArgumentException("rulebookId is required.", nameof(rulebookId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("version is required.", nameof(version));

        var existing = await _db.ValidationPacks.FirstOrDefaultAsync(
            p => p.TenantId == tenant.Id && p.RulebookId == rulebookId && p.Version == version, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Validation pack for {rulebookId} v{version} already exists.");

        var pack = new ValidationPack
        {
            TenantId = tenant.Id,
            RulebookId = rulebookId.Trim(),
            Version = version.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? $"{rulebookId} v{version}" : name.Trim(),
            CreatedBy = user.Id,
            GoldenCasesJson = string.IsNullOrWhiteSpace(goldenCasesJson) ? "[]" : goldenCasesJson,
            Status = ValidationPackStatus.Draft,
        };
        _db.ValidationPacks.Add(pack);
        await _db.SaveChangesAsync(ct);
        return pack;
    }

    /// <summary>Returns the canonical export payload for a pack.</summary>
    public async Task<object> ExportAsync(Tenant tenant, Guid packId, CancellationToken ct)
    {
        var pack = await Require(tenant, packId, ct);
        using var doc = JsonDocument.Parse(pack.GoldenCasesJson);
        return new
        {
            id = pack.Id,
            rulebookId = pack.RulebookId,
            version = pack.Version,
            name = pack.Name,
            status = pack.Status.ToString(),
            createdAt = pack.CreatedAt,
            approvedAt = pack.ApprovedAt,
            cases = doc.RootElement.Clone(),
        };
    }

    public async Task<ValidationPack> ApproveAsync(Tenant tenant, User user, Guid packId, CancellationToken ct)
    {
        var pack = await Require(tenant, packId, ct);
        if (pack.Status == ValidationPackStatus.Deprecated)
            throw new InvalidOperationException("Cannot approve a deprecated validation pack.");
        pack.Status = ValidationPackStatus.Approved;
        pack.ApprovedAt = DateTimeOffset.UtcNow;
        pack.ApprovedBy = user.Id;
        pack.UpdatedAt = pack.ApprovedAt.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.ValidationPackApproved,
            DetailsJson = JsonSerializer.Serialize(new
            {
                packId = pack.Id,
                rulebookId = pack.RulebookId,
                version = pack.Version,
            }),
        }, ct);
        return pack;
    }

    public async Task<ValidationPack> DeprecateAsync(Tenant tenant, User user, Guid packId, CancellationToken ct)
    {
        var pack = await Require(tenant, packId, ct);
        pack.Status = ValidationPackStatus.Deprecated;
        pack.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.ValidationPackDeprecated,
            DetailsJson = JsonSerializer.Serialize(new
            {
                packId = pack.Id,
                rulebookId = pack.RulebookId,
                version = pack.Version,
            }),
        }, ct);
        return pack;
    }

    /// <summary>
    /// Executes the pack's golden cases against the latest rulebook with the
    /// matching <c>RulebookId</c> in this tenant, returning a pass/fail
    /// summary. Audits <see cref="AuditAction.ValidationPackRun"/>.
    /// </summary>
    public async Task<RunSummaryDto> RunAsync(Tenant tenant, User user, Guid packId, CancellationToken ct)
    {
        var pack = await Require(tenant, packId, ct);
        var rulebookEntity = await _rulebooks.GetAsync(tenant.Id, pack.RulebookId, ct)
            ?? throw new InvalidOperationException($"Rulebook '{pack.RulebookId}' not found in this tenant.");
        var spec = RulebookSpec.FromYaml(rulebookEntity.SourceYaml);

        var failures = new List<RunFailureDto>();
        int passed = 0;
        int total = 0;

        foreach (var (name, expected, actual, _) in EvaluateCases(pack, spec))
        {
            total++;
            var missing = expected.Except(actual).ToArray();
            var unexpected = actual.Except(expected).ToArray();

            if (missing.Length == 0 && unexpected.Length == 0)
            {
                passed++;
            }
            else
            {
                failures.Add(new RunFailureDto(name, missing, unexpected));
            }
        }

        var summary = new RunSummaryDto(passed, total - passed, total, failures);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.ValidationPackRun,
            DetailsJson = JsonSerializer.Serialize(new
            {
                packId = pack.Id,
                rulebookId = pack.RulebookId,
                version = pack.Version,
                passed,
                failed = total - passed,
                total,
            }),
        }, ct);
        return summary;
    }

    /// <summary>One golden case run through the real validator (Prompt Studio's per-case rows).</summary>
    public record GoldenCaseResultDto(string CaseName, bool Passed, string[] ExpectedRules, string[] ActualRules, int QualityScore);

    /// <summary>
    /// Runs every golden case of <paramref name="pack"/> through <see cref="ReportValidator"/> against the
    /// tenant's latest rulebook with the pack's <c>RulebookId</c>. Empty when that rulebook does not exist.
    /// </summary>
    public async Task<IReadOnlyList<GoldenCaseResultDto>> EvaluateAsync(Tenant tenant, ValidationPack pack, CancellationToken ct)
    {
        var rulebookEntity = await _rulebooks.GetAsync(tenant.Id, pack.RulebookId, ct);
        if (rulebookEntity is null) return Array.Empty<GoldenCaseResultDto>();
        var spec = RulebookSpec.FromYaml(rulebookEntity.SourceYaml);
        return EvaluateCases(pack, spec)
            .Select(c => new GoldenCaseResultDto(
                c.Name,
                !c.Expected.Except(c.Actual).Any() && !c.Actual.Except(c.Expected).Any(),
                c.Expected,
                c.Actual,
                c.QualityScore))
            .ToList();
    }

    // The single place a golden case is executed: shared by RunAsync (pass/fail summary) and EvaluateAsync.
    private IEnumerable<(string Name, string[] Expected, string[] Actual, int QualityScore)> EvaluateCases(
        ValidationPack pack, RulebookSpec spec)
    {
        using var doc = JsonDocument.Parse(pack.GoldenCasesJson);
        var index = 0;
        foreach (var caseEl in doc.RootElement.EnumerateArray())
        {
            index++;
            var name = caseEl.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : $"case-{index}";
            var report = ParseReport(caseEl.GetProperty("report"));
            var expected = caseEl.TryGetProperty("expectFlagged", out var eEl)
                ? eEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : Array.Empty<string>();

            ValidationResult v = _validator.Validate(report, spec);
            yield return (name, expected, v.Findings.Select(f => f.RuleId).Distinct().ToArray(), v.QualityScore);
        }
    }

    private async Task<ValidationPack> Require(Tenant tenant, Guid packId, CancellationToken ct)
    {
        var pack = await _db.ValidationPacks.FirstOrDefaultAsync(
            p => p.Id == packId && p.TenantId == tenant.Id, ct)
            ?? throw new KeyNotFoundException($"Validation pack '{packId}' not found.");
        return pack;
    }

    private static Report ParseReport(JsonElement el)
    {
        var r = new Report();
        if (el.TryGetProperty("study", out var st))
        {
            r.Study.Modality = st.TryGetProperty("modality", out var m) ? (m.GetString() ?? "") : "";
            r.Study.BodyPart = st.TryGetProperty("bodyPart", out var b) ? (b.GetString() ?? "") : "";
            // Iter-36 — study-context Indication removed; map the pack's indication onto the report-body section.
            r.Indication = st.TryGetProperty("indication", out var i) ? (i.GetString() ?? "") : "";
            r.Study.AccessionNumber = st.TryGetProperty("accessionNumber", out var a) ? (a.GetString() ?? "") : "";
        }
        r.Indication = el.TryGetProperty("indication", out var ind) ? (ind.GetString() ?? "") : "";
        r.Technique = el.TryGetProperty("technique", out var tq) ? (tq.GetString() ?? "") : "";
        r.Comparison = el.TryGetProperty("comparison", out var cm) ? (cm.GetString() ?? "") : "";
        r.Findings = el.TryGetProperty("findings", out var fn) ? (fn.GetString() ?? "") : "";
        r.Impression = el.TryGetProperty("impression", out var ip) ? (ip.GetString() ?? "") : "";
        r.Recommendations = el.TryGetProperty("recommendations", out var rc) ? (rc.GetString() ?? "") : "";
        return r;
    }
}
