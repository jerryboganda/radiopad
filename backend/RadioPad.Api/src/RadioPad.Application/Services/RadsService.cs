using System.IO;
using YamlDotNet.RepresentationModel;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-30 (STD-002) — ACR Reporting and Data Systems (RADS) adapter. Loads
/// the curated <c>rulebooks/_terminology/rads.yaml</c> subset on first use.
/// Only public category codes + short labels are stored; ACR's proprietary
/// descriptive prose is intentionally NOT reproduced.
/// </summary>
public interface IRadsService
{
    /// <summary>Look up a single category by system + code (case-insensitive).</summary>
    RadsCategory? Lookup(string system, string code);

    /// <summary>List every system id known to the adapter.</summary>
    IReadOnlyList<string> ListSystems();

    /// <summary>Resolve all categories for a system, or null if the system is unknown.</summary>
    RadsSystem? GetSystem(string system);
}

public sealed record RadsCategory(string Code, string ShortLabel, string? PublicGuidanceUrl = null);

public sealed record RadsSystem(
    string System,
    string Description,
    string PublicGuidanceUrl,
    IReadOnlyList<RadsCategory> Categories);

public sealed class RadsService : IRadsService
{
    private readonly Dictionary<string, RadsSystem> _systems;

    public RadsService(string yamlPath)
    {
        _systems = LoadFromYaml(yamlPath);
    }

    public RadsCategory? Lookup(string system, string code)
    {
        if (!_systems.TryGetValue(system ?? "", out var sys)) return null;
        return sys.Categories.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ListSystems() => _systems.Keys.OrderBy(k => k).ToList();

    public RadsSystem? GetSystem(string system) =>
        _systems.TryGetValue(system ?? "", out var sys) ? sys : null;

    private static Dictionary<string, RadsSystem> LoadFromYaml(string path)
    {
        var result = new Dictionary<string, RadsSystem>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        var yaml = new YamlStream();
        using var reader = new StreamReader(path);
        yaml.Load(reader);
        if (yaml.Documents.Count == 0) return result;
        if (yaml.Documents[0].RootNode is not YamlMappingNode root) return result;
        if (!root.Children.TryGetValue(new YamlScalarNode("systems"), out var systemsNode)) return result;
        if (systemsNode is not YamlMappingNode systems) return result;

        foreach (var entry in systems.Children)
        {
            if (entry.Key is not YamlScalarNode keyNode || string.IsNullOrEmpty(keyNode.Value)) continue;
            if (entry.Value is not YamlMappingNode body) continue;

            var description = ScalarOr(body, "description", "");
            var url = ScalarOr(body, "publicGuidanceUrl", "");
            var categories = new List<RadsCategory>();
            if (body.Children.TryGetValue(new YamlScalarNode("categories"), out var catsNode)
                && catsNode is YamlSequenceNode catsSeq)
            {
                foreach (var node in catsSeq)
                {
                    if (node is not YamlMappingNode m) continue;
                    var code = ScalarOr(m, "code", "");
                    var label = ScalarOr(m, "shortLabel", "");
                    var catUrl = ScalarOr(m, "publicGuidanceUrl", "");
                    if (!string.IsNullOrEmpty(code))
                    {
                        categories.Add(new RadsCategory(
                            code, label, string.IsNullOrEmpty(catUrl) ? null : catUrl));
                    }
                }
            }

            result[keyNode.Value] = new RadsSystem(keyNode.Value, description, url, categories);
        }
        return result;
    }

    private static string ScalarOr(YamlMappingNode m, string key, string fallback)
    {
        return m.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value ?? fallback
            : fallback;
    }
}
