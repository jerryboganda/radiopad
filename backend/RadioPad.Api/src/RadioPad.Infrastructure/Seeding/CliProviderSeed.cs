using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Cli;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>
/// Idempotent seed for the <b>Gemini API</b> provider — the Google <c>gemini</c>
/// binary authenticated with an API key (<c>GEMINI_API_KEY</c>) rather than the
/// retired oauth-personal login (Google now rejects it with UNSUPPORTED_CLIENT).
/// Surfacing it as a seeded row means it appears in the report intake's
/// AI-provider dropdown out-of-the-box; its runtime availability (binary present +
/// key valid) is reported by <see cref="GeminiCliProvider.ProbeAsync"/> via the
/// "Test" health probe on the AI-models page. Mirrors <see cref="UbagPrimarySeed"/>:
/// every creation path plus a one-time startup backfill routes through here so
/// every tenant gets exactly one row and an operator who deletes it keeps it deleted.
/// </summary>
public static class CliProviderSeed
{
    /// <summary>The current curated display name.</summary>
    public const string ProviderName = "Gemini API";

    /// <summary>The pre-2026-07-13 name, kept so the startup backfill can rename
    /// existing rows in place (the seed itself is only-when-missing).</summary>
    public const string LegacyProviderName = "Gemini CLI (OAuth)";

    /// <summary>
    /// The curated Gemini API provider for <paramref name="tenantId"/>. Compliance
    /// follows <see cref="GeminiCliProvider.DefaultComplianceClass"/> — PhiApproved
    /// since the 2026-07-12 operator promotion (the workflow routes de-identified
    /// text). Quality/Priority sit just below the UBAG primaries so auto-routing
    /// still prefers UBAG; this row exists mainly for the manual dropdown selection.
    /// </summary>
    public static ProviderConfig CuratedGeminiCli(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = ProviderName,
        Adapter = GeminiCliProvider.AdapterId,
        Model = "",
        Compliance = GeminiCliProvider.DefaultComplianceClass,
        Enabled = true,
        Quality = 0.80m,
        Priority = 5,
    };

    /// <summary>
    /// Idempotently ensures the Gemini CLI provider exists for <paramref name="tenantId"/>.
    /// Matched on <c>adapter=gemini-cli</c> (case-insensitive) so it never duplicates a
    /// row and never overwrites an operator's existing/customised CLI provider. Saves its
    /// own changes; returns 1 when a row was inserted, 0 otherwise.
    /// </summary>
    public static async Task<int> EnsureGeminiCliAsync(
        RadioPadDbContext db, Guid tenantId, CancellationToken ct)
    {
        var exists = await db.Providers.AnyAsync(
            p => p.TenantId == tenantId && p.Adapter == GeminiCliProvider.AdapterId, ct);
        if (exists) return 0;
        db.Providers.Add(CuratedGeminiCli(tenantId));
        await db.SaveChangesAsync(ct);
        return 1;
    }

    /// <summary>
    /// Operator promotion (2026-07-12): Gemini CLI is PhiApproved. Promotes any
    /// rows seeded as Sandbox before this change so existing orgs' AI features
    /// stop being blocked by the PHI gates. Idempotent — only touches rows that
    /// are not already PhiApproved. Mirrors
    /// <see cref="UbagPrimarySeed.EnsureCuratedComplianceAsync"/>.
    /// </summary>
    public static async Task<int> EnsureGeminiCliComplianceAsync(
        RadioPadDbContext db, CancellationToken ct)
    {
        var stale = await db.Providers
            .Where(p => p.Adapter == GeminiCliProvider.AdapterId
                     && p.Compliance != Domain.Enums.ProviderComplianceClass.PhiApproved)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;
        foreach (var p in stale)
            p.Compliance = Domain.Enums.ProviderComplianceClass.PhiApproved;
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    /// <summary>
    /// One-time rename backfill (2026-07-13): the provider was seeded as
    /// "<see cref="LegacyProviderName"/>" back when it authenticated via Google
    /// OAuth. It now uses an API key, so the OAuth name is misleading. Renames only
    /// rows that still carry the exact legacy default — an operator who renamed the
    /// row keeps their name. Idempotent; the only-when-missing seed never re-creates
    /// the old name, so this converges to zero. Mirrors
    /// <see cref="EnsureGeminiCliComplianceAsync"/>.
    /// </summary>
    public static async Task<int> EnsureGeminiCliNameAsync(
        RadioPadDbContext db, CancellationToken ct)
    {
        var stale = await db.Providers
            .Where(p => p.Adapter == GeminiCliProvider.AdapterId && p.Name == LegacyProviderName)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;
        foreach (var p in stale)
            p.Name = ProviderName;
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
