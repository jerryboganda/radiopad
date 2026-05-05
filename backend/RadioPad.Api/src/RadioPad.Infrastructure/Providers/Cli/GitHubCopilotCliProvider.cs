using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — adapter that shells out to the GitHub Copilot CLI extension
/// (<c>gh copilot</c>). The provider id is <c>github-copilot-cli</c>; the
/// binary defaults to <c>gh</c> and may be overridden via the
/// <c>RADIOPAD_GH_COPILOT_BIN</c> environment variable. The composed
/// prompt is piped on stdin; arguments are passed via
/// <see cref="ProcessStartInfo.ArgumentList"/> so the prompt itself never
/// reaches a shell.
///
/// <para>Compliance class defaults to <see cref="ProviderComplianceClass.Sandbox"/>;
/// operators must explicitly upgrade the per-provider compliance row to
/// <see cref="ProviderComplianceClass.PhiApproved"/> before PHI may be routed.</para>
/// </summary>
public sealed class GitHubCopilotCliProvider : IAiProviderAdapter
{
    public const string AdapterId = "github-copilot-cli";
    public const string BinaryEnvVar = "RADIOPAD_GH_COPILOT_BIN";
    public const string DefaultBinary = "gh";
    public const ProviderComplianceClass DefaultComplianceClass = CliProviderRunner.DefaultComplianceClass;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<GitHubCopilotCliProvider> _log;

    public GitHubCopilotCliProvider(IProcessLauncher launcher, ILogger<GitHubCopilotCliProvider> log)
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

        var spec = new ProcessLaunchSpec(
            FileName: bin,
            Arguments: new[] { "copilot", "explain", "--shell-out" },
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
                $"{AdapterId}: gh copilot exited with code {result.ExitCode}.",
                statusCode: result.ExitCode,
                responseBody: Truncate(result.StandardError));
        }

        return new AiResult(
            Text: result.StandardOutput.TrimEnd(),
            Provider: p.Name,
            Model: string.IsNullOrWhiteSpace(p.Model) ? "gh-copilot" : p.Model,
            LatencyMs: (int)result.ElapsedMs,
            InputTokens: 0,
            OutputTokens: 0,
            PromptVersion: request.PromptVersion);
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
