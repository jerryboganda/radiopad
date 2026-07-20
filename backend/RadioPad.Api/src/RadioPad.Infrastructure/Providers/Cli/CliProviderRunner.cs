using RadioPad.Application.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Enums;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — shared helpers for CLI-spawning provider adapters
/// (Gemini CLI, Codex CLI). Centralises:
/// <list type="bullet">
///   <item>Default per-process timeout (60s, override via
///   <c>RADIOPAD_CLI_PROVIDER_TIMEOUT_MS</c>).</item>
///   <item>Binary-path allowlist enforcement
///   (<c>RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS</c>, semicolon-separated). Empty
///   allows PATH lookup in development only; production requires an allowlist.</item>
///   <item>Prompt sanitation — refuses NUL / control characters that could
///   break stdin framing.</item>
/// </list>
/// CLI adapters default to <see cref="ProviderComplianceClass.Sandbox"/>
/// because the local binary may forward the prompt to a vendor cloud —
/// except <see cref="GeminiCliProvider"/>, promoted to <c>PhiApproved</c> by
/// operator decision 2026-07-12 (it runs under the operator's own Google
/// OAuth login and the workflow routes de-identified text).
/// </summary>
internal static class CliProviderRunner
{
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.Sandbox;
    public const int DefaultTimeoutMs = 60_000;

    // The gemini CLI is a heavyweight Node bundle: even `gemini --version` cold-loads
    // the whole framework and takes ~15–31 s (measured in prod), so the old 10 s probe
    // cap always false-negatived it to "Unreachable". Give the health probe its own,
    // generous default that covers the slow bootstrap; override via
    // RADIOPAD_CLI_PROBE_TIMEOUT_MS. Generation is unaffected (RADIOPAD_CLI_PROVIDER_TIMEOUT_MS).
    public const int DefaultProbeTimeoutMs = 45_000;

    public static int ResolveTimeoutMs()
    {
        var v = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_TIMEOUT_MS");
        return int.TryParse(v, out var ms) && ms > 0 ? ms : DefaultTimeoutMs;
    }

    public static int ResolveProbeTimeoutMs()
    {
        var v = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROBE_TIMEOUT_MS");
        return int.TryParse(v, out var ms) && ms > 0 ? ms : DefaultProbeTimeoutMs;
    }

    public static string ResolveBinary(string envVar, string defaultName)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(v) ? defaultName : v.Trim();
    }

    /// <summary>
    /// Default-deny in production: <c>RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS</c>
    /// must list the exact binary path. Development may still use PATH lookup
    /// for local smoke tests.
    /// </summary>
    public static void EnforceBinaryAllowlist(string adapterId, string fileName)
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (IsProductionEnvironment())
                throw new ProviderPolicyException($"{adapterId}: cli_binary_allowlist_required");
            return;
        }
        var allowed = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in allowed)
        {
            if (string.Equals(entry, fileName, cmp)) return;
        }
        throw new ProviderPolicyException($"{adapterId}: cli_binary_not_allowed");
    }

    private static bool IsProductionEnvironment() =>
        string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// CLI-level PHI refusal removed (operator decision 2026-07-20) — every
    /// CLI adapter accepts PHI. The secret screen remains: it stops
    /// credentials from leaking to an external CLI, which is not a PHI rule.
    /// </summary>
    public static void EnforceRequestPolicy(string adapterId, AiCompletionRequest request)
    {
        if (LooksLikeSecret(request.SystemPrompt) || LooksLikeSecret(request.UserPrompt))
            throw new ProviderPolicyException($"{adapterId}: secret_not_supported");
    }

    private static bool LooksLikeSecret(string? value)
    {
        var v = value ?? string.Empty;
        return v.Contains("ghp_", StringComparison.Ordinal)
            || v.Contains("github_pat_", StringComparison.Ordinal)
            || v.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)
            || v.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || v.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || v.Contains("-----BEGIN", StringComparison.Ordinal);
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

    public static async Task<AiProviderHealthResult> ProbeBinaryAsync(
        string adapterId,
        string fileName,
        IReadOnlyList<string> arguments,
        IProcessLauncher launcher,
        CancellationToken ct)
    {
        try
        {
            EnforceBinaryAllowlist(adapterId, fileName);
            var result = await launcher.RunAsync(new ProcessLaunchSpec(
                FileName: fileName,
                Arguments: arguments,
                StandardInput: null,
                TimeoutMs: Math.Min(ResolveTimeoutMs(), ResolveProbeTimeoutMs())), ct);

            if (result.ExitCode == 0)
            {
                return new AiProviderHealthResult(
                    Ok: true,
                    Note: "cli binary available",
                    Runtime: fileName);
            }

            var err = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"{adapterId}: version probe exited with code {result.ExitCode}."
                : Truncate(result.StandardError.Trim());
            return new AiProviderHealthResult(false, err, Runtime: fileName);
        }
        catch (ProviderPolicyException ex)
        {
            return new AiProviderHealthResult(false, ex.Message, Runtime: fileName);
        }
        catch (Exception ex) when (ex is ProcessLaunchNotFoundException or ProcessLaunchTimeoutException)
        {
            return new AiProviderHealthResult(false, ToTransport(adapterId, ex).Message, Runtime: fileName);
        }
    }

    public static string ExtractTextFromJsonOrRaw(string stdout)
    {
        var textOut = stdout ?? string.Empty;
        var raw = textOut.Trim();
        if (raw.Length == 0 || raw[0] is not ('{' or '[')) return textOut.TrimEnd();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in new[] { "response", "text", "output", "content" })
                {
                    if (root.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                        return prop.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == System.Text.Json.JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var first = candidates[0];
                    if (first.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.ValueKind == System.Text.Json.JsonValueKind.Array &&
                        parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var text) &&
                        text.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return textOut.TrimEnd();
        }

        return textOut.TrimEnd();
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
