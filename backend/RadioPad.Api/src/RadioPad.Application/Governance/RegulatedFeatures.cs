using System.Text.Json;

namespace RadioPad.Application.Governance;

/// <summary>
/// PRD Phase 3 — regulated, assist-only capabilities that SUGGEST clinical content.
///
/// <para><b>Enabled by default (opt-OUT), per the operator's standing instruction (2026-07-20): the
/// deploying organisation holds the applicable UKCA / MHRA / CE / FDA clearances, so no capability
/// is withheld pending review.</b> An administrator can still switch an individual feature off per
/// tenant; only an explicit <c>false</c> disables one.</para>
///
/// <para>This replaced a fail-CLOSED default that was never actually enforced — the gate had zero
/// production call sites, so every capability ran unconditionally while the admin panel told
/// customers they shipped off pending review. The default changed to match the operator's decision;
/// the gate is now genuinely consulted, so the toggles do something.</para>
///
/// <para>Unchanged by any of this: these capabilities remain SUGGESTION-ONLY and are never
/// auto-applied, and RadioPad still never auto-signs a report (safety boundary #1).</para>
/// </summary>
public enum RegulatedFeature
{
    AutoImpression,
    CriticalFindingFlagging,
    FollowUpStandardisation,
    IntervalChangeTracking,
}

/// <summary>A regulated feature's registry entry + its current on/off state for a tenant.</summary>
public sealed record RegulatedFeatureInfo(
    RegulatedFeature Feature,
    string Key,
    string Title,
    string Description,
    bool Enabled);

/// <summary>
/// The gate for Phase 3 regulated features. Flag state lives in
/// <c>TenantSettings.FeatureFlagsJson</c> under the <see cref="Prefix"/> namespace. Pure +
/// deterministic so it is unit-tested directly.
///
/// <para>The doc comment here previously named <c>Tenant.FeatureFlagsJson</c>, which does not
/// exist — the gate was written against a field on the wrong entity. Nothing caught it because
/// nothing ever called the gate.</para>
/// </summary>
public static class RegulatedFeatures
{
    /// <summary>Flag keys live in <c>Tenant.FeatureFlagsJson</c> under this prefix.</summary>
    public const string Prefix = "regulated.";

    private static readonly IReadOnlyList<(RegulatedFeature Feature, string Key, string Title, string Description)> Catalog =
        new[]
        {
            (RegulatedFeature.AutoImpression, Prefix + "autoImpression", "Automatic impression draft",
                "AI-drafted impression offered as an editable suggestion. The radiologist authors or confirms the final impression; never auto-applied and never signed."),
            (RegulatedFeature.CriticalFindingFlagging, Prefix + "criticalFindingFlagging", "Critical-finding flagging",
                "Flags possible critical findings (e.g. suspected PE, a new mass) for a communicate/acknowledge workflow. Suggestion-only; requires explicit radiologist confirmation."),
            (RegulatedFeature.FollowUpStandardisation, Prefix + "followUpStandardisation", "Follow-up standardisation",
                "Cites structured follow-up frameworks (Fleischner, LI-RADS, TI-RADS, Bosniak) as a non-auto-applied, radiologist-confirmed suggestion."),
            (RegulatedFeature.IntervalChangeTracking, Prefix + "intervalChangeTracking", "Interval-change / RECIST tracking",
                "Computed interval deltas / RECIST-style lesion measurements the radiologist confirms. Assistive only."),
        };

    public static string KeyFor(RegulatedFeature feature) =>
        Catalog.First(c => c.Feature == feature).Key;

    /// <summary>
    /// True unless the tenant has explicitly switched the feature OFF.
    ///
    /// <para>Absent or malformed flags mean ENABLED — the operator holds the applicable clearances
    /// and does not want capabilities withheld. Only a literal <c>false</c> disables one, so a
    /// corrupted flags blob cannot silently remove a capability a radiologist is relying on
    /// mid-session.</para>
    /// </summary>
    public static bool IsEnabled(string? featureFlagsJson, RegulatedFeature feature)
    {
        var flags = Parse(featureFlagsJson);
        return !flags.TryGetValue(KeyFor(feature), out var on) || on;
    }

    /// <summary>The full registry with each feature's current on/off state for a tenant. Must agree
    /// with <see cref="IsEnabled"/> — the admin panel reads this, so a mismatch would show a state
    /// the enforcement path does not honour.</summary>
    public static IReadOnlyList<RegulatedFeatureInfo> Describe(string? featureFlagsJson)
    {
        var flags = Parse(featureFlagsJson);
        return Catalog
            .Select(c => new RegulatedFeatureInfo(
                c.Feature, c.Key, c.Title, c.Description,
                !flags.TryGetValue(c.Key, out var on) || on))
            .ToList();
    }

    private static Dictionary<string, bool> Parse(string? json)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.True: result[prop.Name] = true; break;
                    case JsonValueKind.False: result[prop.Name] = false; break;
                    case JsonValueKind.String when bool.TryParse(prop.Value.GetString(), out var b):
                        result[prop.Name] = b;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed flags JSON → no explicit overrides, so every feature stays ENABLED. A
            // corrupted blob must not silently strip a capability out from under a radiologist
            // mid-session; disabling one is an explicit administrative act.
        }
        return result;
    }
}
