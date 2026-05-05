namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-33 MCP-007 — thrown when a plugin fails the trust gate: invalid
/// manifest signature, no trusted publisher key, revoked publisher, or a
/// capability that has not been registered with the
/// <see cref="Abstractions.IMcpCapabilityRegistry"/>. The MCP host audits
/// <c>AuditAction.ProviderBlocked</c> with <c>kind: "plugin_policy"</c>
/// before rethrowing, mirroring the AI-gateway PHI block path.
/// </summary>
public sealed class PluginPolicyException : Exception
{
    public string Reason { get; }
    public string PluginId { get; }

    public PluginPolicyException(string pluginId, string reason, string message)
        : base(message)
    {
        PluginId = pluginId;
        Reason = reason;
    }

    public PluginPolicyException(string pluginId, string reason, string message, Exception inner)
        : base(message, inner)
    {
        PluginId = pluginId;
        Reason = reason;
    }
}
