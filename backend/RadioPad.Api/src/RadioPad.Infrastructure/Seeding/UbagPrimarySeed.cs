using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Ubag;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>
/// Single, idempotent source of truth for the curated UBAG primary providers every
/// RadioPad org gets — <b>UBAG (Gemini Web)</b> (the unattended primary) and
/// <b>UBAG (DeepSeek Web)</b> (the secondary). Both were verified end-to-end against
/// the production gateway (2026-06-22).
///
/// Historically these rows were inlined in <see cref="DevSeed"/>, so only the
/// development "dev" tenant ever received them; production orgs created via
/// <c>bootstrap-org</c> / registration started with zero providers, and the UBAG
/// auto-discovery sweeper only runs for tenants that <i>already</i> have a UBAG row —
/// so those orgs' "AI models" page stayed empty forever. Routing every creation path
/// (and a one-time startup backfill for pre-existing orgs) through this helper closes
/// that gap without the definitions drifting between call sites.
/// </summary>
public static class UbagPrimarySeed
{
    /// <summary>
    /// The curated UBAG primaries for <paramref name="tenantId"/>. Mirrors the values
    /// the dev tenant has carried since the 2026-06-22 integration: Gemini ranks first
    /// (Quality 0.90 / Priority 1) so the Quality-weighted router prefers it unattended;
    /// DeepSeek is an enabled secondary (Quality 0.85 / Priority 2). Both are Sandbox
    /// class — UBAG is non-PHI by policy.
    /// </summary>
    public static IReadOnlyList<ProviderConfig> CuratedPrimaries(Guid tenantId) => new[]
    {
        new ProviderConfig
        {
            TenantId = tenantId,
            Name = "UBAG (Gemini Web)",
            Adapter = UbagProviderAdapter.AdapterId,
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
            Quality = 0.9m,
            Priority = 1,
        },
        new ProviderConfig
        {
            TenantId = tenantId,
            Name = "UBAG (DeepSeek Web)",
            Adapter = UbagProviderAdapter.AdapterId,
            Model = "deepseek_web",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
            Quality = 0.85m,
            Priority = 2,
        },
    };

    /// <summary>
    /// Idempotently ensures the curated UBAG primaries exist for <paramref name="tenantId"/>.
    /// Only the primaries whose <see cref="ProviderConfig.Model"/> is not already present
    /// (matched on <c>adapter=ubag</c> + model, case-insensitive) are inserted, so it never
    /// duplicates a row and never overwrites or re-enables an operator's existing/customised
    /// UBAG provider. Saves its own changes; returns the number of rows inserted.
    /// </summary>
    public static async Task<int> EnsureCuratedPrimariesAsync(
        RadioPadDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existingModels = await db.Providers
            .Where(p => p.TenantId == tenantId && p.Adapter == UbagProviderAdapter.AdapterId)
            .Select(p => p.Model)
            .ToListAsync(ct);
        var have = new HashSet<string>(existingModels, StringComparer.OrdinalIgnoreCase);

        var toAdd = CuratedPrimaries(tenantId)
            .Where(p => !have.Contains(p.Model))
            .ToList();
        if (toAdd.Count == 0) return 0;

        db.Providers.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        return toAdd.Count;
    }
}
