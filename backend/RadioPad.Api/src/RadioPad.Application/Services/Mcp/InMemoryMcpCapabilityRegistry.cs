using System.Collections.Concurrent;
using RadioPad.Application.Abstractions;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-33 MCP-007 — process-local default-deny implementation of
/// <see cref="IMcpCapabilityRegistry"/>. Capability strings are compared
/// ordinal-case-sensitive (lower-case dotted, e.g. <c>"rulebook.lookup"</c>).
/// </summary>
public sealed class InMemoryMcpCapabilityRegistry : IMcpCapabilityRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _allowed
        = new(StringComparer.Ordinal);

    public void Allow(string pluginId, string capability)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId is required", nameof(pluginId));
        if (string.IsNullOrWhiteSpace(capability))
            throw new ArgumentException("capability is required", nameof(capability));
        var bag = _allowed.GetOrAdd(pluginId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        bag[capability] = 1;
    }

    public void AllowAll(string pluginId, IEnumerable<string> capabilities)
    {
        foreach (var c in capabilities)
        {
            if (!string.IsNullOrWhiteSpace(c))
                Allow(pluginId, c);
        }
    }

    public void Revoke(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        _allowed.TryRemove(pluginId, out _);
    }

    public bool IsAllowed(string pluginId, string capability)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(capability))
            return false;
        return _allowed.TryGetValue(pluginId, out var bag) && bag.ContainsKey(capability);
    }

    public void EnsureAllowed(string pluginId, string capability)
    {
        if (!IsAllowed(pluginId, capability))
        {
            throw new PluginPolicyException(
                pluginId ?? "",
                "capability_not_registered",
                $"Plugin '{pluginId}' is not allowed to invoke capability '{capability}'.");
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot()
    {
        var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var kvp in _allowed)
        {
            dict[kvp.Key] = kvp.Value.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        }
        return dict;
    }
}
