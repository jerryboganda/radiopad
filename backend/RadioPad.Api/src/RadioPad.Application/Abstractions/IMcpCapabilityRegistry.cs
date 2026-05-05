namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-33 MCP-007 — capability-scoped allow-list. Plugins must declare
/// requested capabilities (e.g. <c>"dicomweb.read"</c>,
/// <c>"report.draft.suggest"</c>, <c>"rulebook.lookup"</c>) in their signed
/// manifest. The MCP host registers the <c>(pluginId, capability)</c>
/// tuples after signature verification; any tool call whose
/// <c>(pluginId, capability)</c> is not registered is rejected. The default
/// state is empty (deny-by-default).
/// </summary>
public interface IMcpCapabilityRegistry
{
    /// <summary>
    /// Register a single <c>(pluginId, capability)</c> tuple. Idempotent.
    /// </summary>
    void Allow(string pluginId, string capability);

    /// <summary>Bulk-register the capabilities a plugin declared in its manifest.</summary>
    void AllowAll(string pluginId, IEnumerable<string> capabilities);

    /// <summary>Drop every capability registered for the plugin (e.g. on revoke / block).</summary>
    void Revoke(string pluginId);

    /// <summary>True iff <paramref name="capability"/> has been registered for <paramref name="pluginId"/>.</summary>
    bool IsAllowed(string pluginId, string capability);

    /// <summary>
    /// Throws <see cref="Services.Mcp.PluginPolicyException"/> when the
    /// tuple is not on the allow-list. Used by the MCP host as a guard
    /// before forwarding a tool call to the sandbox.
    /// </summary>
    void EnsureAllowed(string pluginId, string capability);

    /// <summary>Snapshot of registered capabilities for diagnostics / audit.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot();
}
