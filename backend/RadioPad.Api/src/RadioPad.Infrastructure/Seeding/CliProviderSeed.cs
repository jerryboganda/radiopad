using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Cli;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>
/// Idempotent seed for the <b>Gemini CLI (OAuth)</b> provider — the Google
/// <c>gemini</c> binary authenticated through the operator's Google / Gemini Code
/// Assist (Pro subscription) OAuth login rather than an API key. Surfacing it as a
/// seeded row means it appears in the report intake's AI-provider dropdown
/// out-of-the-box; its runtime availability (binary present + logged in) is
/// reported by <see cref="GeminiCliProvider.ProbeAsync"/> via the "Test" health
/// probe on the AI-models page. Mirrors <see cref="UbagPrimarySeed"/>: every
/// creation path plus a one-time startup backfill routes through here so every
/// tenant gets exactly one row and an operator who deletes it keeps it deleted.
/// </summary>
public static class CliProviderSeed
{
    /// <summary>
    /// The curated Gemini CLI provider for <paramref name="tenantId"/>. Compliance
    /// defaults to <see cref="GeminiCliProvider.DefaultComplianceClass"/> (Sandbox)
    /// because the CLI may forward the prompt to a vendor cloud — the PHI gate keeps
    /// patient-identifying text off it unless an operator explicitly promotes it.
    /// Quality/Priority sit just below the UBAG primaries so auto-routing still
    /// prefers UBAG; this row exists mainly for the manual dropdown selection.
    /// </summary>
    public static ProviderConfig CuratedGeminiCli(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = "Gemini CLI (OAuth)",
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
}
