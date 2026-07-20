using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — adapter that shells out to the Google Gemini CLI
/// (<c>gemini</c>). Provider id <c>gemini-cli</c>. The binary defaults to
/// <c>gemini</c>; override with <c>RADIOPAD_GEMINI_BIN</c>. The prompt is
/// piped on stdin; the model id (when present) is forwarded via
/// <c>--model</c>. Compliance class defaults to
/// <see cref="ProviderComplianceClass.PhiApproved"/> per the 2026-07-12
/// operator promotion (see <see cref="DefaultComplianceClass"/>).
/// </summary>
public sealed class GeminiCliProvider : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "gemini-cli";
    public const string BinaryEnvVar = "RADIOPAD_GEMINI_BIN";
    public const string DefaultBinary = "gemini";
    // Operator decision (2026-07-12): the reporting workflow routes de-identified
    // text through this provider, so it is promoted out of the CLI Sandbox default
    // — it must not be blocked by the PHI / compliance gates. Mirrors the UBAG
    // promotion of 2026-06-27 (UbagProviderAdapter.DefaultComplianceClass).
    // Auth is via GEMINI_API_KEY (Google retired oauth-personal); the launcher
    // must allow that env var through to the child process (IProcessLauncher
    // ScrubEnvironment). CodexCliProvider intentionally keeps the Sandbox default.
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.PhiApproved;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<GeminiCliProvider> _log;

    public GeminiCliProvider(IProcessLauncher launcher, ILogger<GeminiCliProvider> log)
    {
        _launcher = launcher;
        _log = log;
    }

    public string Id => AdapterId;

    public Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        var bin = CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
        // --skip-trust so the probe never trips gemini-cli's headless trusted-folder
        // check (exit 55) in the launcher's scrubbed temp cwd. The probe is slow
        // regardless — gemini cold-loads its whole bundle for --version — so it runs
        // under the generous probe timeout (ResolveProbeTimeoutMs), not the 10 s cap.
        return CliProviderRunner.ProbeBinaryAsync(AdapterId, bin, new[] { "--skip-trust", "--version" }, _launcher, cancellationToken);
    }

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        CliProviderRunner.EnforceRequestPolicy(AdapterId, request);
        var p = request.Provider;
        var bin = CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
        CliProviderRunner.EnforceBinaryAllowlist(AdapterId, bin);
        var prompt = CliProviderRunner.Compose(
            CliProviderRunner.Sanitise(AdapterId, request.SystemPrompt),
            CliProviderRunner.Sanitise(AdapterId, request.UserPrompt));

        // --skip-trust: the launcher spawns gemini in a scrubbed environment and a
        // dedicated temp working directory, so gemini-cli's "trusted folder" check
        // has no project config to guard and (in headless mode) would otherwise
        // abort with exit 55 ("not running in a trusted directory"). We also allow
        // GEMINI_CLI_TRUST_WORKSPACE through the env scrub (IProcessLauncher) as a
        // belt-and-suspenders; the flag makes the provider work regardless of env.
        var args = new List<string> { "--skip-trust", "--output-format", "json" };
        if (!string.IsNullOrWhiteSpace(p.Model))
        {
            args.Add("--model");
            args.Add(p.Model);
        }

        var spec = new ProcessLaunchSpec(
            FileName: bin,
            Arguments: args,
            StandardInput: prompt,
            TimeoutMs: CliProviderRunner.ResolveTimeoutMs());

        ProcessLaunchResult result;
        try
        {
            result = await _launcher.RunAsync(spec, cancellationToken);
        }
        catch (Exception ex) when (ex is ProcessLaunchNotFoundException or ProcessLaunchTimeoutException)
        {
            throw CliProviderRunner.ToTransport(AdapterId, ex);
        }

        if (result.ExitCode != 0)
        {
            throw new ProviderTransportException(
                $"{AdapterId}: gemini exited with code {result.ExitCode}.",
                statusCode: result.ExitCode,
                responseBody: Truncate(result.StandardError));
        }

        return new AiResult(
            Text: CliProviderRunner.ExtractTextFromJsonOrRaw(result.StandardOutput),
            Provider: p.Name,
            Model: string.IsNullOrWhiteSpace(p.Model) ? "gemini" : p.Model,
            LatencyMs: (int)result.ElapsedMs,
            InputTokens: 0,
            OutputTokens: 0,
            PromptVersion: request.PromptVersion);
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
