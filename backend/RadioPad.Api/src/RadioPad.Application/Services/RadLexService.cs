using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-30 (STD-001) — RadLex® terminology adapter. Loads a curated subset
/// from <c>rulebooks/_terminology/radlex_subset.yaml</c> on first use and
/// answers <see cref="Lookup"/> / <see cref="Search"/> queries in-memory.
///
/// RadLex® is a registered trademark of RSNA. The subset used by RadioPad is
/// reproduced under RSNA's public license terms; it is NOT the full RadLex
/// ontology. Callers needing complete coverage should integrate with the
/// official RSNA download.
/// </summary>
public interface IRadLexService
{
    /// <summary>Find a concept by RID or by exact (case-insensitive) match
    /// against <c>preferredLabel</c> or any synonym. Returns null when no
    /// match is found.</summary>
    RadLexConcept? Lookup(string term);

    /// <summary>Prefix search across <c>preferredLabel</c> + <c>synonyms</c>.
    /// Results are deduplicated by RID and ordered by preferred-label length
    /// (shortest first) for deterministic ranking.</summary>
    IReadOnlyList<RadLexConcept> Search(string prefix, int take = 20);

    /// <summary>Total concept count (used by the FHIR CodeSystem stub).</summary>
    int Count { get; }

    /// <summary>Enumerate every concept (for the FHIR CodeSystem dump).</summary>
    IReadOnlyList<RadLexConcept> All { get; }
}

/// <summary>RadLex concept row. Mirrors the YAML schema 1:1.</summary>
public sealed record RadLexConcept(
    string Rid,
    string PreferredLabel,
    IReadOnlyList<string> Synonyms,
    string Category);

public sealed class RadLexService : IRadLexService
{
    private readonly Dictionary<string, RadLexConcept> _byRid;
    private readonly Dictionary<string, RadLexConcept> _byLabel;
    private readonly List<RadLexConcept> _all;

    public RadLexService(string yamlPath)
    {
        var concepts = LoadFromYaml(yamlPath);
        _all = concepts;
        _byRid = concepts.ToDictionary(c => c.Rid, StringComparer.OrdinalIgnoreCase);
        _byLabel = new(StringComparer.OrdinalIgnoreCase);
        foreach (var c in concepts)
        {
            _byLabel[c.PreferredLabel] = c;
            foreach (var s in c.Synonyms) _byLabel[s] = c;
        }
    }

    public int Count => _all.Count;

    public IReadOnlyList<RadLexConcept> All => _all;

    public RadLexConcept? Lookup(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        var key = term.Trim();
        if (_byRid.TryGetValue(key, out var byRid)) return byRid;
        if (_byLabel.TryGetValue(key, out var byLabel)) return byLabel;
        return null;
    }

    public IReadOnlyList<RadLexConcept> Search(string prefix, int take = 20)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Array.Empty<RadLexConcept>();
        var p = prefix.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<RadLexConcept>();
        foreach (var c in _all)
        {
            if (seen.Contains(c.Rid)) continue;
            if (c.Rid.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                || c.PreferredLabel.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                || c.Synonyms.Any(s => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                seen.Add(c.Rid);
                hits.Add(c);
            }
        }
        return hits
            .OrderBy(c => c.PreferredLabel.Length)
            .ThenBy(c => c.PreferredLabel, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(take, 1, 200))
            .ToList();
    }

    private static List<RadLexConcept> LoadFromYaml(string path)
    {
        var concepts = new List<RadLexConcept>();
        if (!File.Exists(path)) return concepts;
        var yaml = new YamlStream();
        using var reader = new StreamReader(path);
        yaml.Load(reader);
        if (yaml.Documents.Count == 0) return concepts;
        if (yaml.Documents[0].RootNode is not YamlMappingNode root) return concepts;
        if (!root.Children.TryGetValue(new YamlScalarNode("concepts"), out var conceptsNode)) return concepts;
        if (conceptsNode is not YamlSequenceNode seq) return concepts;
        foreach (var node in seq)
        {
            if (node is not YamlMappingNode m) continue;
            var rid = ScalarOr(m, "rid", "");
            var label = ScalarOr(m, "preferredLabel", "");
            var category = ScalarOr(m, "category", "");
            var synonyms = new List<string>();
            if (m.Children.TryGetValue(new YamlScalarNode("synonyms"), out var synsNode)
                && synsNode is YamlSequenceNode syns)
            {
                foreach (var s in syns)
                {
                    if (s is YamlScalarNode sc && !string.IsNullOrEmpty(sc.Value)) synonyms.Add(sc.Value);
                }
            }
            if (!string.IsNullOrEmpty(rid) && !string.IsNullOrEmpty(label))
            {
                concepts.Add(new RadLexConcept(rid, label, synonyms, category));
            }
        }
        return concepts;
    }

    private static string ScalarOr(YamlMappingNode m, string key, string fallback)
    {
        return m.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? fallback
            : fallback;
    }
}
