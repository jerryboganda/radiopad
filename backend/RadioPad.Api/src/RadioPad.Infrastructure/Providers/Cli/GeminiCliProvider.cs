using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — adapter that shells out to the Google Gemini CLI
/// (<c>gemini</c>). Provider id <c>gemini-cli</c>. The binary defaults to
/// <c>gemini</c>; override with <c>RADIOPAD_GEMINI_BIN</c>. The prompt is
/// piped on stdin; the model id (when present) is forwarded via
/// <c>--model</c>. Compliance class defaults to
/// <see cref="ProviderComplianceClass.Sandbox"/> because the CLI may call a
/// vendor cloud.
/// </summary>
public sealed class GeminiCliProvider : IAiProviderAdapter
{
    public const string AdapterId = "gemini-cli";
    public const string BinaryEnvVar = "RADIOPAD_GEMINI_BIN";
    public const string DefaultBinary = "gemini";
    public const ProviderComplianceClass DefaultComplianceClass = CliProviderRunner.DefaultComplianceClass;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<GeminiCliProvider> _log;

    public GeminiCliProvider(IProcessLauncher launcher, ILogger<GeminiCliProvider> log)
    {
        _launcher = launcher;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var bin = CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
        CliProviderRunner.EnforceBinaryAllowlist(AdapterId, bin);
        var prompt = CliProviderRunner.Compose(
            CliProviderRunner.Sanitise(AdapterId, request.SystemPrompt),
            CliProviderRunner.Sanitise(AdapterId, request.UserPrompt));

        // D1 — Updated 2025-06: gemini CLI uses `prompt` sub-command with
        // `--stdin` to read from stdin in non-interactive mode, and `--json`
        // for machine-readable output.
        // TODO: Update when vendor publishes stable non-interactive flags
        var args = new List<string> { "prompt", "--stdin", "--json" };
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
            Text: result.StandardOutput.TrimEnd(),
            Provider: p.Name,
            Model: string.IsNullOrWhiteSpace(p.Model) ? "gemini" : p.Model,
            LatencyMs: (int)result.ElapsedMs,
            InputTokens: 0,
            OutputTokens: 0,
            PromptVersion: request.PromptVersion);
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
