using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — adapter that shells out to GitHub Copilot CLI. The provider id is
/// <c>github-copilot-cli</c>; the binary defaults to <c>copilot</c> and may be
/// overridden via <c>RADIOPAD_COPILOT_BIN</c>.
/// The composed prompt is supplied through Copilot CLI's stdin option stream;
/// arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/> so the
/// prompt itself never reaches a shell.
///
/// <para>Compliance class defaults to <see cref="ProviderComplianceClass.Sandbox"/>;
/// operators must explicitly upgrade the per-provider compliance row to
/// <see cref="ProviderComplianceClass.PhiApproved"/> before PHI may be routed.</para>
/// </summary>
public sealed class GitHubCopilotCliProvider : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "github-copilot-cli";
    public const string BinaryEnvVar = "RADIOPAD_COPILOT_BIN";
    public const string DefaultBinary = "copilot";
    public const ProviderComplianceClass DefaultComplianceClass = CliProviderRunner.DefaultComplianceClass;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<GitHubCopilotCliProvider> _log;

    public GitHubCopilotCliProvider(IProcessLauncher launcher, ILogger<GitHubCopilotCliProvider> log)
    {
        _launcher = launcher;
        _log = log;
    }

    public string Id => AdapterId;

    public Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        var bin = ResolveBinary();
        return CliProviderRunner.ProbeBinaryAsync(AdapterId, bin, new[] { "--help" }, _launcher, cancellationToken);
    }

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        CliProviderRunner.EnforceRequestPolicy(AdapterId, request);
        var p = request.Provider;
        var bin = ResolveBinary();
        CliProviderRunner.EnforceBinaryAllowlist(AdapterId, bin);
        var prompt = CliProviderRunner.Compose(
            CliProviderRunner.Sanitise(AdapterId, request.SystemPrompt),
            CliProviderRunner.Sanitise(AdapterId, request.UserPrompt));

        var spec = new ProcessLaunchSpec(
            FileName: bin,
            Arguments: Array.Empty<string>(),
            StandardInput: BuildOptionStream(prompt, p.Model),
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
                $"{AdapterId}: copilot exited with code {result.ExitCode}.",
                statusCode: result.ExitCode,
                responseBody: Truncate(result.StandardError));
        }

        return new AiResult(
            Text: result.StandardOutput.TrimEnd(),
            Provider: p.Name,
            Model: string.IsNullOrWhiteSpace(p.Model) ? "copilot" : p.Model,
            LatencyMs: (int)result.ElapsedMs,
            InputTokens: 0,
            OutputTokens: 0,
            PromptVersion: request.PromptVersion);
    }

    private static string ResolveBinary()
    {
        return CliProviderRunner.ResolveBinary(BinaryEnvVar, DefaultBinary);
    }

    private static string BuildOptionStream(string prompt, string? model)
    {
        var parts = new List<string>
        {
            "--prompt",
            QuoteOptionValue(prompt),
            "--deny-tool=shell",
            "--deny-tool=write",
        };
        if (!string.IsNullOrWhiteSpace(model)
            && !string.Equals(model, "copilot", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(model, "gh-copilot", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("--model");
            parts.Add(QuoteOptionValue(model.Trim()));
        }

        return string.Join(' ', parts);
    }

    private static string QuoteOptionValue(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
