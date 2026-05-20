using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// GitHub Copilot SDK provider placeholder. GitHub Copilot SDK support is
/// represented as a first-class provider id so tenants can model policy, but
/// it fails closed until RadioPad is wired to an official backend-safe Copilot
/// SDK transport. It never accepts PHI, even if a row is misclassified.
/// </summary>
public sealed class GitHubCopilotSdkProvider : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "github-copilot-sdk";
    public const string EnabledEnvVar = "RADIOPAD_GITHUB_COPILOT_SDK_ENABLED";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.Sandbox;

    private readonly ILogger<GitHubCopilotSdkProvider> _log;

    public GitHubCopilotSdkProvider(ILogger<GitHubCopilotSdkProvider> log)
    {
        _log = log;
    }

    public string Id => AdapterId;

    public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        if (request.ContainsPhi)
        {
            throw new ProviderPolicyException(
                $"{AdapterId}: phi_not_supported. GitHub Copilot SDK routing is disabled for PHI workflows.");
        }

        if (!SdkEnabled())
        {
            throw new ProviderPolicyException(
                $"{AdapterId}: runtime_not_configured. No official backend-safe GitHub Copilot SDK transport is enabled.");
        }

        _log.LogWarning("GitHub Copilot SDK provider was enabled but no reviewed SDK transport is installed.");
        throw new ProviderPolicyException(
            $"{AdapterId}: runtime_not_available. A reviewed official SDK transport must be installed before use.");
    }

    public Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        if (!SdkEnabled())
        {
            return Task.FromResult(new AiProviderHealthResult(
                Ok: false,
                Error: "runtime_not_configured",
                Note: "GitHub Copilot SDK provider is policy-mode only until an official backend-safe SDK transport is enabled.",
                Runtime: AdapterId));
        }

        return Task.FromResult(new AiProviderHealthResult(
            Ok: false,
            Error: "runtime_not_available",
            Note: "GitHub Copilot SDK env flag is set, but no reviewed SDK transport is installed.",
            Runtime: AdapterId));
    }

    private static bool SdkEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(EnabledEnvVar);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }
}