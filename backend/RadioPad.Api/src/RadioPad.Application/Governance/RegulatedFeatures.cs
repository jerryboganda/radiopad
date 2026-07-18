using System.Text.Json;

namespace RadioPad.Application.Governance;

/// <summary>
/// PRD Phase 3 — regulated, assist-only capabilities that SUGGEST clinical content. The underlying
/// models (MedASR / MedGemma) and the follow-up frameworks are developer models "requiring
/// validation," not cleared medical devices, so every one of these ships OFF by default and needs
/// an explicit, audited tenant opt-in plus a UKCA/MHRA/CE/FDA regulatory review before clinical use.
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
/// The gate for Phase 3 regulated features. Flag state lives in <c>Tenant.FeatureFlagsJson</c> under
/// the <see cref="Prefix"/> namespace; an absent, false, or unparseable flag means OFF (fail safe —
/// regulatory review required). Pure + deterministic so it is unit-tested directly.
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

    /// <summary>True ONLY when the tenant has explicitly opted the feature in. Absent / false /
    /// malformed flags → OFF (fail safe).</summary>
    public static bool IsEnabled(string? featureFlagsJson, RegulatedFeature feature)
    {
        var flags = Parse(featureFlagsJson);
        return flags.TryGetValue(KeyFor(feature), out var on) && on;
    }

    /// <summary>The full registry with each feature's current on/off state for a tenant. Every entry
    /// requires regulatory review before clinical use.</summary>
    public static IReadOnlyList<RegulatedFeatureInfo> Describe(string? featureFlagsJson)
    {
        var flags = Parse(featureFlagsJson);
        return Catalog
            .Select(c => new RegulatedFeatureInfo(
                c.Feature, c.Key, c.Title, c.Description,
                flags.TryGetValue(c.Key, out var on) && on))
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
            // Malformed flags JSON → treat every regulated feature as OFF (fail safe).
        }
        return result;
    }
}
