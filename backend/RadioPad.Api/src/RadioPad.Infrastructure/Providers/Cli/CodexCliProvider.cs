using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — adapter that shells out to the OpenAI Codex CLI
/// (<c>codex</c>). Provider id <c>codex-cli</c>. Binary defaults to
/// <c>codex</c>; override with <c>RADIOPAD_CODEX_BIN</c>. The prompt is
/// piped on stdin through <c>codex exec -</c>. Compliance class defaults to
/// <see cref="ProviderComplianceClass.Sandbox"/> because the CLI may call a
/// vendor cloud.
/// </summary>
public sealed class CodexCliProvider : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "codex-cli";
    public const string BinaryEnvVar = "RADIOPAD_CODEX_BIN";
    public const string DefaultBinary = "codex";
    public const ProviderComplianceClass DefaultComplianceClass = CliProviderRunner.DefaultComplianceClass;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<CodexCliProvider> _log;

    public CodexCliProvider(IProcessLauncher launcher, ILogger<CodexCliProvider> log)
    {
        _launcher = launcher;
        _log = log;
    }

    public string Id => AdapterId;

    public Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        var bin = CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
        return CliProviderRunner.ProbeBinaryAsync(AdapterId, bin, new[] { "--version" }, _launcher, cancellationToken);
    }

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        CliProviderRunner.EnforceRequestPolicy(AdapterId, request);
        if (Environment.GetEnvironmentVariable("RADIOPAD_CODEX_CLI_ENABLED") != "1")
            throw new ProviderPolicyException($"{AdapterId}: runtime_not_enabled");

        var p = request.Provider;
        var bin = CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
        CliProviderRunner.EnforceBinaryAllowlist(AdapterId, bin);
        var prompt = CliProviderRunner.Compose(
            CliProviderRunner.Sanitise(AdapterId, request.SystemPrompt),
            CliProviderRunner.Sanitise(AdapterId, request.UserPrompt));

        // Keep Codex read-only by default; edit-capable modes require a separate reviewed flow.
        var args = new List<string> { "exec", "--sandbox", "read-only" };
        if (!string.IsNullOrWhiteSpace(p.Model))
        {
            args.Add("--model");
            args.Add(p.Model);
        }
        args.Add("-");

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
                $"{AdapterId}: codex exited with code {result.ExitCode}.",
                statusCode: result.ExitCode,
                responseBody: Truncate(result.StandardError));
        }

        return new AiResult(
            Text: result.StandardOutput.TrimEnd(),
            Provider: p.Name,
            Model: string.IsNullOrWhiteSpace(p.Model) ? "codex" : p.Model,
            LatencyMs: (int)result.ElapsedMs,
            InputTokens: 0,
            OutputTokens: 0,
            PromptVersion: request.PromptVersion);
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
