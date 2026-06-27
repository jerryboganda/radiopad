using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Ubag;

public sealed class UbagProviderAdapter : IAiProviderAdapter, IAiProviderHealthProbe
{
    public const string AdapterId = "ubag";
    // Operator decision (2026-06-27): RadioPad routes only de-identified report
    // text to UBAG and the workflow guarantees no PHI reaches it, so UBAG is
    // treated as a PHI-approved provider — it must never be blocked by the PHI /
    // compliance gates (router eligibility, AiGateway policy, and the
    // request-level guard below). See UbagPrimarySeed for the seeded value and
    // the discovery hosted service for the one-time backfill of existing rows.
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.PhiApproved;
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
    /// Merges browser-context readiness into a target list in a single pass (no double-scan).
    /// For each target, finds the matching context by id (case-insensitive) once, then sets
    /// <c>Ready</c> from <c>ctx.Authenticated</c> and <c>Status</c> from <c>ctx.LoginState</c>.
    /// When no context matches the target's original <c>Status</c> is preserved and
    /// <c>Ready</c> is <c>false</c>.
    /// Pure function — safe to unit-test without HTTP.
    /// </summary>
    public static IReadOnlyList<UbagTarget> MergeTargetReadiness(
        IReadOnlyList<UbagTarget> targets,
        IReadOnlyList<UbagBrowserContext> contexts)
    {
        var result = new List<UbagTarget>(targets.Count);
        foreach (var t in targets)
        {
            var ctx = contexts.FirstOrDefault(c => string.Equals(c.TargetId, t.Id, StringComparison.OrdinalIgnoreCase));
            result.Add(t with
            {
                Ready = ctx?.Authenticated == true,
                Status = ctx is not null ? ctx.LoginState : t.Status,
            });
        }
        return result;
    }

    internal static void EnforceRequestPolicy(AiCompletionRequest request)
    {
        // PHI gate intentionally removed (2026-06-27): UBAG is operator-approved
        // for PHI (see DefaultComplianceClass). The remaining guards are not PHI
        // restrictions — they stop credentials/secrets from leaking to a web AI
        // and stop ordered Hub workflows from being mis-routed through report
        // drafting. They never trigger for normal radiology dictation.
        if (LooksLikeSecret(request.SystemPrompt) || LooksLikeSecret(request.UserPrompt))
            throw new ProviderPolicyException($"{AdapterId}: secret_not_supported");
        if (string.Equals(request.Provider.Model, "ordered:web-chain", StringComparison.OrdinalIgnoreCase))
            throw new ProviderPolicyException($"{AdapterId}: ordered workflows must use the UBAG Hub, not report drafting.");
    }

    internal static string ResolveTarget(ProviderConfig provider)
    {
        var target = string.IsNullOrWhiteSpace(provider.Model) ? "gemini_web" : provider.Model.Trim();
        if (!IsTargetAllowed(target))
            throw new ProviderPolicyException($"{AdapterId}: target_not_allowed:{target}");
        return target;
    }

    // Full set of UBAG web targets the gateway can drive today. Used only as the
    // display fallback when no explicit operator cap is configured; the live source of
    // truth for what is *selectable* is the per-tenant ProviderConfig rows that
    // UbagProviderDiscoveryService keeps in sync with the gateway login state.
    private static readonly string[] KnownCatalog =
    {
        "gemini_web", "deepseek_web", "chatgpt_web", "claude_web",
        "mistral_lechat", "perplexity_web", "mock",
    };

    /// <summary>
    /// The explicit operator allow-list from <c>RADIOPAD_UBAG_ALLOWED_TARGETS</c>, or
    /// <c>null</c> when unset/blank — meaning "no cap": any live UBAG catalog target is
    /// permitted, so a provider the operator logs into works with zero configuration.
    /// </summary>
    public static IReadOnlyList<string>? ResolveAllowedTargetCap()
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_ALLOWED_TARGETS");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>True when no cap is configured, or the cap explicitly lists the target.</summary>
    public static bool IsTargetAllowed(string target)
    {
        var cap = ResolveAllowedTargetCap();
        return cap is null || cap.Contains(target, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Back-compat concrete list for callers that need one. When no operator cap is
    /// configured this returns the full known catalog rather than a hardcoded subset,
    /// so newly logged-in providers are never gated out.
    /// </summary>
    public static IReadOnlyList<string> ResolveAllowedTargets()
        => ResolveAllowedTargetCap() ?? KnownCatalog;

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
