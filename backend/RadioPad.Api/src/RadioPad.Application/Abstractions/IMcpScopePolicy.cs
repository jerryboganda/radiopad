namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-32 MCP-005 — central scope-policy gate. Default-deny for any tool
/// whose scope string contains a <c>shell:</c>, <c>fs:</c>, or <c>net:</c>
/// token unless BOTH the env var <c>RADIOPAD_MCP_ALLOW_DANGEROUS=1</c> is
/// set AND the per-tenant <c>TenantSettings.AllowDangerousMcp</c> flag is
/// true. Returning <see cref="McpScopeDecision.Allowed"/> is the only path
/// that should let an invocation reach <see cref="IMcpSandbox"/>.
/// </summary>
public interface IMcpScopePolicy
{
    McpScopeDecision Evaluate(string scopeString, bool tenantAllowDangerous);
}

/// <param name="Allowed">True iff the scope is permitted for invocation.</param>
/// <param name="Reason">When <c>Allowed=false</c>, a stable kebab-case reason string for audit + 403 body.</param>
/// <param name="DangerousTokens">List of recognised dangerous tokens that triggered a refusal (empty when allowed).</param>
public sealed record McpScopeDecision(bool Allowed, string? Reason, IReadOnlyList<string> DangerousTokens);
