using RadioPad.Application.Services;
using RadioPad.Domain.Enums;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — shared helpers for CLI-spawning provider adapters
/// (GitHub Copilot CLI, Gemini CLI, Codex CLI). Centralises:
/// <list type="bullet">
///   <item>Default per-process timeout (60s, override via
///   <c>RADIOPAD_CLI_PROVIDER_TIMEOUT_MS</c>).</item>
///   <item>Binary-path allowlist enforcement
///   (<c>RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS</c>, semicolon-separated; empty
///   = allow PATH lookup).</item>
///   <item>Prompt sanitation — refuses NUL / control characters that could
///   break stdin framing.</item>
/// </list>
/// All three CLI adapters default to <see cref="ProviderComplianceClass.Sandbox"/>
/// because the local binary may forward the prompt to a vendor cloud. Operators
/// must explicitly mark a configured provider <c>PhiApproved</c> before PHI may
/// flow through it.
/// </summary>
internal static class CliProviderRunner
{
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.Sandbox;
    public const int DefaultTimeoutMs = 60_000;

    public static int ResolveTimeoutMs()
    {
        var v = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_TIMEOUT_MS");
        return int.TryParse(v, out var ms) && ms > 0 ? ms : DefaultTimeoutMs;
    }

    public static string ResolveBinary(string envVar, string defaultName)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(v) ? defaultName : v.Trim();
    }

    /// <summary>
    /// Default-deny: when <c>RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS</c> is set
    /// and non-empty, the resolved file name MUST be one of the entries
    /// (case-insensitive on Windows, ordinal elsewhere). When the env var
    /// is unset or empty, the bare command name is allowed and resolves
    /// via PATH.
    /// </summary>
    public static void EnforceBinaryAllowlist(string adapterId, string fileName)
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS");
        if (string.IsNullOrWhiteSpace(raw)) return;
        var allowed = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in allowed)
        {
            if (string.Equals(entry, fileName, cmp)) return;
        }
        throw new ProviderPolicyException($"{adapterId}: cli_binary_not_allowed");
    }

    /// <summary>
    /// Refuses prompts containing control characters that would break stdin
    /// framing or could let a vendor CLI mis-parse the input. The check is
    /// conservative: tabs / newlines / carriage returns are allowed; NUL
    /// and other C0 controls are rejected.
    /// </summary>
    public static string Sanitise(string adapterId, string? prompt)
    {
        var s = prompt ?? "";
        foreach (var ch in s)
        {
            if (ch == '\t' || ch == '\n' || ch == '\r') continue;
            if (ch < 0x20 || ch == 0x7F)
                throw new ProviderPolicyException($"{adapterId}: prompt contains disallowed control character");
        }
        return s;
    }

    public static string Compose(string? systemPrompt, string? userPrompt)
    {
        var sys = (systemPrompt ?? "").Trim();
        var usr = (userPrompt ?? "").Trim();
        if (sys.Length == 0) return usr;
        return $"SYSTEM: {sys}\n\nUSER: {usr}";
    }

    public static ProviderTransportException ToTransport(string adapterId, Exception inner)
        => inner switch
        {
            ProcessLaunchNotFoundException nf => new ProviderTransportException($"{adapterId}: {nf.Message}", inner: nf),
            ProcessLaunchTimeoutException to => new ProviderTransportException($"{adapterId}: {to.Message}", inner: to),
            _ => new ProviderTransportException($"{adapterId}: {inner.Message}", inner: inner),
        };
}
