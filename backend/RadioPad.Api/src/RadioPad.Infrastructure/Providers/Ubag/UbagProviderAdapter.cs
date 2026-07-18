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
        // Per-invocation nonce: the key's job is to dedupe TRANSPORT retries of
        // this one submission (the resilience pipeline re-sends the same
        // request/headers), NOT to fuse separate user attempts. A prompt-hash
        // key made a user's retry after a timeout replay the already-cancelled
        // gateway job forever (audit finding, 2026-07-11).
        var idempotencyKey = $"radiopad-ai-{request.Provider.Id:N}-{Guid.NewGuid():N}"[..48];

        var created = await _client.CreateJobAsync(
            new UbagJobRequest(target, prompt, ClientRequestId: idempotencyKey),
            idempotencyKey,
            cancellationToken);

        var terminal = await WaitForJobAsync(created, cancellationToken);
        // manual_action (logged-out session / interstitial) is NOT a policy
        // block — it means THIS provider is temporarily unusable. Classify as
        // transport so the auto-routing failover chain tries the next provider
        // (discovery disables the row within one sweep; this covers the gap).
        if (!string.IsNullOrWhiteSpace(terminal.ManualAction))
            throw new ProviderTransportException($"{AdapterId}: manual_action_required:{target}");
        if (!string.IsNullOrWhiteSpace(terminal.Error) || terminal.Failed)
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
            // An ABSENT browser context is NOT a failure — same doctrine as
            // MergeTargetReadiness. The vps-local executor drives live browser
            // profiles without ever registering a /v1/browser/contexts row, and the
            // 2026-05-22 /v1/targets shape carries no readiness field, so "no context"
            // means "no explicit login signal", not "logged out". Reporting
            // context_not_found here made every WORKING primary (gemini_web,
            // deepseek_web, …) show "Unavailable: context_not_found:…" in the picker's
            // connection test even while jobs succeeded (operator report, 2026-07-19).
            // The target is listed and health is OK, so report ready; only an EXPLICIT
            // context row may downgrade that to login_required.
            if (ctx is null)
                return new AiProviderHealthResult(true, null, Note: match.Status, Runtime: target);
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
    /// When no context matches, the target's original <c>Status</c> AND <c>Ready</c> are
    /// preserved — an absent context row is NOT a logged-out signal. The vps-local executor
    /// runs jobs against live browser profiles without ever registering contexts (verified
    /// 2026-07-18: gemini_web/deepseek_web jobs completed while /v1/browser/contexts was
    /// empty), so only an explicit context row may flip readiness here.
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
                Ready = ctx is not null ? ctx.Authenticated : t.Ready,
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

    /// <summary>
    /// The documented default allow-list applied when <c>RADIOPAD_UBAG_ALLOWED_TARGETS</c>
    /// is unset. Default-DENY (audit fix 2026-07-18): unset used to mean "no cap", silently
    /// permitting every target the gateway could drive; production docs always promised
    /// this conservative set, so the code now matches them. Operators widen it explicitly.
    /// </summary>
    private static readonly string[] DefaultAllowedTargets =
    {
        "chatgpt_web", "gemini_web", "deepseek_web", "mock",
    };

    /// <summary>
    /// The operator allow-list from <c>RADIOPAD_UBAG_ALLOWED_TARGETS</c>, falling back to
    /// <see cref="DefaultAllowedTargets"/> when unset/blank. Never "no cap" — a target
    /// outside this list is rejected at request time and never materialised by discovery.
    /// </summary>
    public static IReadOnlyList<string> ResolveAllowedTargetCap()
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_ALLOWED_TARGETS");
        if (string.IsNullOrWhiteSpace(raw)) return DefaultAllowedTargets;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>True when the effective allow-list (env or documented default) lists the target.</summary>
    public static bool IsTargetAllowed(string target)
        => ResolveAllowedTargetCap().Contains(target, StringComparer.OrdinalIgnoreCase);

    private async Task<UbagJob> WaitForJobAsync(UbagJob initial, CancellationToken ct)
    {
        var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_UBAG_TIMEOUT_MS"), out var ms) && ms > 0
            ? ms
            : 120_000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        var current = initial;
        try
        {
            while (!current.Terminal && !string.IsNullOrWhiteSpace(current.Id))
            {
                await Task.Delay(PollDelay, timeout.Token);
                current = await _client.GetJobAsync(current.Id, timeout.Token);
            }
            return current;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Poll budget exhausted (not a caller abort): the browser job is
            // still running on the gateway. Best-effort cancel it so it stops
            // occupying the shared worker, then surface a TRANSPORT failure —
            // that classification is what lets the auto-routing failover chain
            // try the next provider instead of bubbling a bare cancellation.
            await TryCancelJobAsync(current.Id, timeoutMs);
            throw new ProviderTransportException($"{AdapterId}: job_timeout_after_{timeoutMs}ms:{ResolveTargetOrDefault(current)}");
        }
        catch (OperationCanceledException)
        {
            // Caller aborted (request cancelled / shutdown): still release the
            // gateway job, then propagate the cancellation unchanged.
            await TryCancelJobAsync(current.Id, timeoutMs);
            throw;
        }
    }

    private async Task TryCancelJobAsync(string jobId, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return;
        try
        {
            // Fresh short-lived token: both the caller's token and the poll
            // budget are already cancelled at this point.
            using var cancelCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _client.CancelJobAsync(jobId, cancelCts.Token);
        }
        catch (Exception ex) when (ex is ProviderTransportException or HttpRequestException or OperationCanceledException)
        {
            // Best-effort only — the gateway's own job timeout is the backstop.
        }
    }

    private static string ResolveTargetOrDefault(UbagJob job)
        => string.IsNullOrWhiteSpace(job.Target) ? "unknown" : job.Target;

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

}
