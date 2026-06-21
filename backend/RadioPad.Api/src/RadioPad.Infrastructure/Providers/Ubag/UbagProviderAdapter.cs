using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Ubag;

public sealed class UbagProviderAdapter : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "ubag";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.Sandbox;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    private readonly IUbagClient _client;

    public UbagProviderAdapter(IUbagClient client)
    {
        _client = client;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        EnforceRequestPolicy(request);
        var target = ResolveTarget(request.Provider);
        var prompt = ComposePrompt(request.SystemPrompt, request.UserPrompt);
        var idempotencyKey = $"radiopad-ai-{request.Provider.Id:N}-{Hash(prompt)[..16]}";

        var created = await _client.CreateJobAsync(
            new UbagJobRequest(target, prompt, ClientRequestId: idempotencyKey),
            idempotencyKey,
            cancellationToken);

        var terminal = await WaitForJobAsync(created, cancellationToken);
        if (!string.IsNullOrWhiteSpace(terminal.ManualAction))
            throw new ProviderPolicyException($"{AdapterId}: manual_action_required");
        if (!string.IsNullOrWhiteSpace(terminal.Error) || terminal.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            throw new ProviderTransportException($"{AdapterId}: {terminal.Error ?? terminal.Status}");
        if (string.IsNullOrWhiteSpace(terminal.Output))
            throw new ProviderTransportException($"{AdapterId}: empty_output");

        return new AiResult(
            Text: terminal.Output,
            Provider: request.Provider.Name,
            Model: target,
            LatencyMs: terminal.LatencyMs ?? 0,
            InputTokens: EstimateTokens(prompt),
            OutputTokens: EstimateTokens(terminal.Output),
            PromptVersion: request.PromptVersion);
    }

    public async Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        try
        {
            var health = await _client.GetHealthAsync(cancellationToken);
            if (!health.Ok)
                return new AiProviderHealthResult(false, health.Error ?? health.Status, Runtime: AdapterId);

            var target = ResolveTarget(provider);
            var targets = await _client.ListTargetsAsync(cancellationToken);
            var match = targets.FirstOrDefault(t => string.Equals(t.Id, target, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return new AiProviderHealthResult(false, $"target_not_found:{target}", Runtime: AdapterId);

            var contexts = await _client.ListBrowserContextsAsync(cancellationToken);
            var ctx = contexts.FirstOrDefault(c => string.Equals(c.TargetId, target, StringComparison.OrdinalIgnoreCase));
            if (ctx is null)
                return new AiProviderHealthResult(false, $"context_not_found:{target}", Runtime: target);
            if (ctx.Authenticated)
                return new AiProviderHealthResult(true, null, Note: ctx.LoginState, Runtime: target);
            return new AiProviderHealthResult(false, $"login_required:{target}", Note: ctx.LoginState, Runtime: target);
        }
        catch (Exception ex) when (ex is ProviderTransportException or HttpRequestException or TaskCanceledException)
        {
            return new AiProviderHealthResult(false, ex.Message, Runtime: AdapterId);
        }
    }

    /// <summary>
    /// Returns true when the browser context for <paramref name="targetId"/> is authenticated.
    /// Used by both <see cref="ProbeAsync"/> and the UBAG Hub status endpoint so they agree
    /// on the same readiness rule.
    /// </summary>
    public static bool IsTargetReady(string targetId, IReadOnlyList<UbagBrowserContext> contexts)
    {
        var ctx = contexts.FirstOrDefault(c => string.Equals(c.TargetId, targetId, StringComparison.OrdinalIgnoreCase));
        return ctx?.Authenticated == true;
    }

    internal static void EnforceRequestPolicy(AiCompletionRequest request)
    {
        if (request.ContainsPhi)
            throw new ProviderPolicyException($"{AdapterId}: phi_not_supported");
        if (LooksLikeSecret(request.SystemPrompt) || LooksLikeSecret(request.UserPrompt))
            throw new ProviderPolicyException($"{AdapterId}: secret_not_supported");
        if (string.Equals(request.Provider.Model, "ordered:web-chain", StringComparison.OrdinalIgnoreCase))
            throw new ProviderPolicyException($"{AdapterId}: ordered workflows must use the UBAG Hub, not report drafting.");
    }

    internal static string ResolveTarget(ProviderConfig provider)
    {
        var target = string.IsNullOrWhiteSpace(provider.Model) ? "gemini_web" : provider.Model.Trim();
        var allowed = ResolveAllowedTargets();
        if (!allowed.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new ProviderPolicyException($"{AdapterId}: target_not_allowed:{target}");
        return target;
    }

    public static IReadOnlyList<string> ResolveAllowedTargets()
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_ALLOWED_TARGETS");
        if (string.IsNullOrWhiteSpace(raw)) raw = "chatgpt_web,gemini_web,deepseek_web,mock";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<UbagJob> WaitForJobAsync(UbagJob initial, CancellationToken ct)
    {
        var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_UBAG_TIMEOUT_MS"), out var ms) && ms > 0
            ? ms
            : 120_000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        var current = initial;
        while (!current.Terminal && !string.IsNullOrWhiteSpace(current.Id))
        {
            await Task.Delay(PollDelay, timeout.Token);
            current = await _client.GetJobAsync(current.Id, timeout.Token);
        }
        return current;
    }

    private static string ComposePrompt(string? systemPrompt, string? userPrompt)
    {
        var sys = (systemPrompt ?? "").Trim();
        var usr = (userPrompt ?? "").Trim();
        return sys.Length == 0 ? usr : $"SYSTEM: {sys}\n\nUSER: {usr}";
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

    private static int EstimateTokens(string value)
        => Math.Max(1, (int)Math.Ceiling((value ?? string.Empty).Length / 4.0));

    private static string Hash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value ?? ""))).ToLowerInvariant();
}
