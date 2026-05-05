using RadioPad.Application.Abstractions;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-32 MCP-005 — production default-deny scope policy. The set of
/// dangerous prefixes (<c>shell:</c>, <c>fs:</c>, <c>net:</c>) is fixed at
/// compile time; weakening it requires a code change AND a code review (see
/// <c>docs/04-security/security-architecture.md#mcp-signing</c>).
/// </summary>
public sealed class McpScopePolicy : IMcpScopePolicy
{
    /// <summary>The unforgeable env-var override. Reading it inside
    /// <see cref="Evaluate"/> means tests can flip it via
    /// <c>Environment.SetEnvironmentVariable</c> per-case.</summary>
    public const string AllowDangerousEnvVar = "RADIOPAD_MCP_ALLOW_DANGEROUS";

    private static readonly string[] DangerousPrefixes = { "shell:", "fs:", "net:" };

    public McpScopeDecision Evaluate(string scopeString, bool tenantAllowDangerous)
    {
        var tokens = SplitTokens(scopeString);
        var dangerous = tokens
            .Where(t => DangerousPrefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (dangerous.Length == 0)
            return new McpScopeDecision(true, null, Array.Empty<string>());

        var envOverride = string.Equals(
            Environment.GetEnvironmentVariable(AllowDangerousEnvVar),
            "1",
            StringComparison.Ordinal);

        if (envOverride && tenantAllowDangerous)
            return new McpScopeDecision(true, null, dangerous);

        return new McpScopeDecision(false, "mcp_scope", dangerous);
    }

    private static IReadOnlyList<string> SplitTokens(string scopeString) =>
        string.IsNullOrWhiteSpace(scopeString)
            ? Array.Empty<string>()
            : scopeString
                .Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
}
