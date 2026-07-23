using RadioPad.Application.Stt;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Abstractions;

/// <summary>Adapter for an AI provider. Each registered provider
/// (Anthropic, Azure OpenAI, Ollama, mock) implements this interface; the
/// AI gateway selects the active adapter at request time using the tenant
/// provider registry and PHI policy.</summary>
public interface IAiProviderAdapter
{
    /// <summary>Adapter id, e.g. "anthropic". Must match <see cref="ProviderConfig.Adapter"/>.</summary>
    string Id { get; }

    Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Optional capability for provider adapters that can perform a safe,
/// prompt-free readiness probe. Implementations must never send clinical
/// text or secret material during a probe.
/// </summary>
public interface IAiProviderHealthProbe
{
    Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken);
}

public sealed record AiProviderHealthResult(
    bool Ok,
    string? Error = null,
    string? Note = null,
    int? Status = null,
    string? Runtime = null);

public sealed record AiCompletionRequest(
    ProviderConfig Provider,
    string SystemPrompt,
    string UserPrompt,
    string PromptVersion,
    bool ContainsPhi)
{
    /// <summary>PRD AI-014 — clinically conservative default sampling temperature.</summary>
    public const double DefaultTemperature = 0.2;

    /// <summary>
    /// PRD AI-014 — clinical-conservatism ceiling. No clinical AI run may exceed
    /// this sampling temperature; <see cref="Temperature"/> is clamped to
    /// <c>[0, MaxTemperature]</c> on assignment so the ceiling cannot be bypassed
    /// by any caller or rulebook override.
    /// </summary>
    public const double MaxTemperature = 0.4;

    private readonly double _temperature = DefaultTemperature;

    /// <summary>
    /// Iter-0b (AI-014) — sampling temperature, auto-clamped to
    /// <c>[0, <see cref="MaxTemperature"/>]</c>. Defaults to
    /// <see cref="DefaultTemperature"/> (0.2) so every existing call site stays
    /// conservative without change.
    /// </summary>
    public double Temperature
    {
        get => _temperature;
        init => _temperature = Math.Clamp(value, 0.0, MaxTemperature);
    }

    /// <summary>
    /// Iter-0b (RB-009 / AI-012) — the rulebook entity id bound to this AI run,
    /// recorded on every AI audit + usage row for compliance provenance. Null
    /// when no rulebook is bound.
    /// </summary>
    public Guid? RulebookId { get; init; }

    /// <summary>Iter-0b (RB-009) — the rulebook semantic version bound to this run (audit provenance).</summary>
    public string? RulebookVersion { get; init; }

    /// <summary>
    /// Iter-0b (AI-015 / RB-005) — optional JSON Schema (as a JSON string) that
    /// the model output must conform to. When set and valid, OpenAI-family
    /// adapters request structured output via <c>response_format</c>. Null = free text.
    /// </summary>
    public string? OutputSchema { get; init; }

    /// <summary>
    /// Phase 0 (brief §5.4) — optional GBNF grammar (llama.cpp format) that constrains the model's
    /// decoding so it is structurally unable to emit malformed output. When set, local llama.cpp
    /// adapters forward it as the <c>grammar</c> field on <c>/completion</c>. Null = unconstrained.
    /// Preferred over <see cref="OutputSchema"/> for small local models (e.g. MedGemma 1.5 4B), where
    /// prose-instructed JSON is fragile; tolerant JSON parsing remains the secondary net.
    /// </summary>
    public string? Grammar { get; init; }

    /// <summary>
    /// Optional llama.cpp <c>repeat_penalty</c> sampling parameter — discourages resampling recently
    /// used tokens. Null = the adapter's/server's own default. A long, exhaustive generation (e.g.
    /// on-device whole-report drafting) can otherwise degenerate into repeating the same line
    /// verbatim until it exhausts its token budget once a small model runs out of genuinely new
    /// content to add; a moderate penalty (empirically ~1.1) fixes this without measurably
    /// distorting content the way a stronger penalty does (verified against MedGemma 1.5 4B: 1.3
    /// caused the model to fabricate a different measurement rather than repeat the dictated one).
    /// Only meaningful to local llama.cpp adapters; other providers ignore it.
    /// </summary>
    public double? RepeatPenalty { get; init; }

    /// <summary>
    /// Optional llama.cpp <c>repeat_last_n</c> — how many recent tokens <see cref="RepeatPenalty"/>
    /// looks back over. Null = the adapter's/server's own default. Only meaningful to local
    /// llama.cpp adapters; other providers ignore it.
    /// </summary>
    public int? RepeatLastN { get; init; }

    /// <summary>
    /// AI-013 — optional token-stream sink. Adapters that support streaming switch to
    /// stream mode ONLY when this is non-null and MUST invoke <see cref="IProgress{T}.Report"/>
    /// synchronously, in arrival order, from their read loop (use
    /// <see cref="SynchronousProgress{T}"/>, never <see cref="System.Progress{T}"/>). Adapters
    /// that ignore it (UBAG, CLI, Bedrock, Vertex) compile and behave byte-identically.
    /// </summary>
    public IProgress<AiStreamChunk>? OnStream { get; init; }

    /// <summary>
    /// AI-013 re-attach seam — invoked once with the provider-side job id the moment it exists
    /// (today: only the UBAG adapter, wired in PR-B4). Must not throw; must be cheap (callers
    /// persist asynchronously). Null on every non-UBAG path.
    /// </summary>
    public Action<string>? OnProviderJobCreated { get; init; }
}

/// <summary>
/// AI-013 — one incremental chunk of streamed model output. <see cref="OutputTokens"/> is the
/// cumulative token (or chunk) count for the CURRENT provider attempt; it RESETS on failover
/// (a retry against the next provider starts counting at ~1), which the registry's
/// non-monotonic-token rule uses to discard the failed attempt's partial text.
/// </summary>
public sealed record AiStreamChunk(string Delta, int OutputTokens);

/// <summary>
/// AI-013 — the optional streaming/re-attach hooks threaded from the job coordinator through
/// <see cref="Services.ReportingService"/> onto the <see cref="AiCompletionRequest"/>. A null
/// value (the default for every non-job caller) leaves the request non-streaming.
/// </summary>
public sealed record AiRunHooks(IProgress<AiStreamChunk>? OnStream, Action<string>? OnProviderJobCreated);

public interface IUbagClient
{
    Task<UbagHealth> GetHealthAsync(CancellationToken ct);
    Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct);
    Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct);
    Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct);
    Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct);

    /// <summary>
    /// Best-effort hard cancel (<c>POST /v1/jobs/{id}/cancel</c>) for a job
    /// RadioPad has given up on (poll timeout / caller abort), so abandoned
    /// browser jobs stop occupying the gateway's worker instead of running to
    /// their own timeout. Callers treat failures as non-fatal.
    /// </summary>
    Task CancelJobAsync(string jobId, CancellationToken ct);
    Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct);
    Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct);
    Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct);

    /// <summary>
    /// Phase B (dictation transcription) — create a <c>medical_transcription</c>
    /// job whose worker scrapes a transcript from an audio artifact uploaded
    /// separately via <see cref="UploadJobArtifactAsync"/>. The job is created
    /// FIRST (so the worker can wait on the named artifact) and the audio is
    /// uploaded SECOND. Mirrors <see cref="CreateJobAsync"/> but carries an
    /// <c>audio_artifact_key</c> input and a <c>wait_for_artifacts</c> option
    /// rather than a free-text prompt only.
    /// </summary>
    Task<UbagJob> CreateTranscriptionJobAsync(UbagTranscriptionRequest request, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Phase B (dictation transcription) — PUT raw audio bytes to
    /// <c>/v1/jobs/{jobId}/artifacts/{key}</c> with the supplied content type
    /// and length. <paramref name="key"/> must be a single path segment
    /// (no '/', '\', '%'); the payload must be &#8804; 32 MiB. Returns the
    /// artifact descriptor parsed from the gateway's 201 response.
    /// </summary>
    Task<UbagArtifact> UploadJobArtifactAsync(string jobId, string key, Stream content, string contentType, long contentLength, string idempotencyKey, CancellationToken ct);
}

public sealed record UbagHealth(bool Ok, string Status, string? Version, string? Error);

public sealed record UbagBrowserSummary(
    int Instances,
    int Contexts,
    int Tabs,
    string? Status,
    string? RawStatus);

/// <summary>
/// A web-AI target the UBAG gateway can drive. <see cref="Ready"/> is tri-state:
/// <c>true</c>/<c>false</c> only when the gateway gave an EXPLICIT login signal
/// (a legacy <c>ready</c>/status field on <c>/v1/targets</c>, or a
/// <c>/v1/browser/contexts</c> row); <c>null</c> means "no signal" — the real
/// 2026-05-22 shape carries no readiness field, and some executor modes (e.g.
/// vps-local) never register browser contexts even while jobs succeed, so the
/// absence of a context row must NOT be read as logged-out.
/// </summary>
public sealed record UbagTarget(
    string Id,
    string Name,
    string Status,
    bool? Ready,
    string? Url);

/// <summary>
/// A browser context returned by <c>GET /v1/browser/contexts</c>.
/// <see cref="Authenticated"/> is true when <see cref="LoginState"/> equals
/// "authenticated" (case-insensitive) — used to determine per-target readiness.
/// </summary>
public sealed record UbagBrowserContext(string TargetId, string LoginState)
{
    public bool Authenticated => string.Equals(LoginState, "authenticated", StringComparison.OrdinalIgnoreCase);
}

public sealed record UbagJobRequest(
    string Target,
    string Prompt,
    string CommandType = "submit",
    string ReturnMode = "final",
    string? ClientRequestId = null);

/// <summary>
/// Phase B (dictation transcription) — request to create a
/// <c>medical_transcription</c> job. The worker waits for the audio artifact
/// named <see cref="AudioArtifactKey"/> (uploaded separately) and scrapes a
/// free-text transcript. <see cref="Prompt"/> steers the transcription model.
/// </summary>
public sealed record UbagTranscriptionRequest(
    string Target,
    string AudioArtifactKey,
    string Prompt,
    string ReturnMode = "final",
    string? ClientRequestId = null);

/// <summary>
/// Phase B (dictation transcription) — descriptor returned by the gateway for
/// an uploaded job artifact (the audio file). <see cref="Checksum"/> is the
/// server-computed content hash; never carries audio bytes.
/// </summary>
public sealed record UbagArtifact(
    string JobId,
    string Key,
    string ContentType,
    long SizeBytes,
    string Checksum);

/// <summary>
/// Phase 1 (local STT) — an on-device, fully-offline speech-to-text engine
/// (e.g. sherpa-onnx Parakeet). When <see cref="Available"/> is
/// true, <see cref="ITranscriptionService"/> routes dictation audio here and the
/// bytes never leave the machine — replacing the cloud (UBAG) path on desktop.
/// Implementations self-disable (<see cref="Available"/> == false) when the
/// engine is not configured or the model is absent, so non-desktop builds fall
/// through to the cloud flow transparently.
/// </summary>
public interface ILocalSttClient
{
    /// <summary>True when the engine is configured, the model is present, and it
    /// can actually run. Checked before every routing decision.</summary>
    bool Available { get; }

    /// <summary>
    /// Transcribe a complete recorded-audio buffer entirely on-device. The
    /// returned <see cref="TranscriptionResult.Provider"/> identifies the local
    /// engine; only the SHA-256 of the text is ever persisted by the caller.
    /// </summary>
    /// <param name="mode">Optional per-request engine mode: <c>"ensemble"</c>,
    /// <c>"single"</c>, or null/<c>"auto"</c> to use the configured default.</param>
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null);
}

/// <summary>
/// Phase B (dictation transcription) — orchestrates the four-call UBAG flow
/// (create <c>medical_transcription</c> job → upload audio artifact → worker
/// scrapes → poll to terminal) for a single report's dictation audio. Routes
/// provider selection through <see cref="IProviderRouter"/> and enforces the
/// de-identified-audio PHI gate before any bytes leave the process. The
/// resulting transcript is free text the editor can adopt; only its SHA-256
/// is ever persisted to the audit log.
/// </summary>
public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        Tenant tenant,
        User user,
        Report report,
        Stream audio,
        string fileName,
        long sizeBytes,
        string contentType,
        CancellationToken ct,
        string? sttMode = null);
}

/// <summary>
/// Phase B (dictation transcription) — result of a single audio-transcription
/// run. <see cref="Text"/> is the free-text transcript; the remaining fields
/// carry provider/model provenance and latency for the editor + audit trail.
/// </summary>
public sealed record TranscriptionResult(
    string Text,
    string Provider,
    string Model,
    long LatencyMs,
    IReadOnlyList<ReconciledSpan>? Spans = null);

public sealed record UbagJob(
    string Id,
    string Target,
    string Status,
    bool Terminal,
    string? Output,
    string? Error,
    string? ManualAction,
    int? LatencyMs,
    string RawJson)
{
    /// <summary>
    /// True for every non-success terminal status — <c>failed</c>,
    /// <c>failed_retryable</c> (the gateway treats it as terminal and nothing
    /// retries it), <c>failed_terminal</c>, <c>dead_letter</c>,
    /// <c>cancelled</c>/<c>canceled</c>, <c>timed_out</c> — so callers fail
    /// over instead of mistaking a dead job for one with merely empty output.
    /// </summary>
    public bool Failed =>
        Status.StartsWith("failed", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("dead_letter", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("timed_out", StringComparison.OrdinalIgnoreCase);
}

public sealed record UbagWorkflowRequest(
    string Name,
    IReadOnlyList<UbagWorkflowStep> Steps,
    string? ClientRequestId = null);

public sealed record UbagWorkflowStep(
    string Id,
    string Target,
    string Prompt);

public sealed record UbagWorkflow(
    string Id,
    string Status,
    string RawJson);

public sealed record UbagWorkflowRun(
    string Id,
    string WorkflowId,
    string Status,
    bool Terminal,
    string? Output,
    string? Error,
    string? ManualAction,
    string RawJson);

public interface IAiGateway
{
    Task<AiResult> RouteAsync(
        Tenant tenant,
        AiCompletionRequest request,
        CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-computes the SHA-256 chain across every <see cref="AuditEvent"/> for a
    /// tenant in CreatedAt order. Returns the first event id whose stored
    /// <c>IntegrityChain</c> diverges from the recomputed value, or <c>null</c>
    /// when the chain is intact. Used by <c>GET /api/audit/verify</c>
    /// (PRD §13.2 audit-completeness, AUTH-006 tamper-evident logging).
    /// </summary>
    Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// NOTIF-001 — the immutable request to produce one in-app notification for a
/// single recipient. <see cref="Title"/> / <see cref="Body"/> are the in-app tier
/// of NOTIF-004 (may carry a modality/body-part-class descriptor, never raw
/// clinical narrative/findings/accession). <see cref="DedupeKey"/>, when set,
/// makes the produce idempotent — a second draft with the same key for the same
/// recipient is dropped.
/// </summary>
public sealed record NotificationDraft(
    Guid TenantId, Guid UserId, NotificationCategory Category, NotificationUrgency Urgency,
    string Title, string Body, string? LinkHref = null,
    string? SourceKind = null, Guid? SourceId = null,
    bool RequiresAck = false, string? DedupeKey = null);

/// <summary>
/// NOTIF-001 — produces in-app notifications. The single implementation
/// (<c>NotificationProducer</c>) is a singleton hosted service that also drains
/// terminal AI-job bus events into AiJob-category notifications. Producing a
/// notification writes the inbox row (own scope, uncancelled write), audits
/// <see cref="AuditAction.NotificationCreated"/> (workflow metadata only — never
/// Title/Body), and publishes it to the SSE bus for live delivery. Muted
/// categories (non-critical only), a per-recipient storm cap, and DedupeKeys can
/// all cause a produce to return <c>null</c>.
/// </summary>
public interface INotificationProducer
{
    /// <summary>Creates one notification. Returns <c>null</c> when the draft was
    /// deduped, muted (non-critical), or coalesced by the storm guard.</summary>
    Task<Notification?> CreateAsync(NotificationDraft draft, CancellationToken ct);

    /// <summary>Fans a notification out to every active user in the tenant whose role
    /// grants <paramref name="permission"/> (resolved via <c>RolePermissionMap</c>),
    /// excluding <paramref name="excludeUserId"/> (typically the acting user). One
    /// <see cref="NotificationDraft"/> is built per recipient by <paramref name="draftFor"/>.</summary>
    Task NotifyPermissionHoldersAsync(Guid tenantId, RbacPermission permission, Guid? excludeUserId,
        Func<Guid, NotificationDraft> draftFor, CancellationToken ct);
}

/// <summary>
/// Result of a tenant audit-chain integrity check.
/// </summary>
/// <param name="EventCount">Total events in scope.</param>
/// <param name="Intact">True when no tampering detected.</param>
/// <param name="FirstBrokenEventId">First event id where the recomputed hash diverged, or null if intact.</param>
/// <param name="LastVerifiedAt">CreatedAt of the last event that was successfully verified.</param>
public record AuditChainVerification(
    int EventCount,
    bool Intact,
    Guid? FirstBrokenEventId,
    DateTimeOffset? LastVerifiedAt);

public interface IRulebookStore
{
    Task<Rulebook?> GetAsync(Guid tenantId, string rulebookId, CancellationToken ct);
    Task<IReadOnlyList<Rulebook>> ListAsync(Guid tenantId, CancellationToken ct);
    Task SaveAsync(Rulebook rulebook, CancellationToken ct);
}

/// <summary>
/// PRD AI-010 — selects the cheapest enabled <see cref="ProviderConfig"/>
/// whose <see cref="ProviderComplianceClass"/> satisfies the tenant + PHI
/// constraints. Returns null when no provider matches.
/// </summary>
public interface IProviderRouter
{
    Task<ProviderConfig?> SelectAsync(
        Tenant tenant,
        bool containsPhi,
        CancellationToken ct);

    /// <summary>
    /// Every eligible provider ordered best-first — the failover chain for
    /// auto-routed AI calls (transport failure on one candidate falls through
    /// to the next; see <see cref="Services.ProviderFailover"/>). The default
    /// implementation preserves single-provider semantics for routers that do
    /// not rank (test fakes), returning the <see cref="SelectAsync"/> winner
    /// as a one-element chain.
    /// </summary>
    async Task<IReadOnlyList<ProviderConfig>> SelectRankedAsync(
        Tenant tenant,
        bool containsPhi,
        CancellationToken ct)
    {
        var winner = await SelectAsync(tenant, containsPhi, ct);
        return winner is null ? Array.Empty<ProviderConfig>() : new[] { winner };
    }
}

/// <summary>
/// Iter-32 AI-010 — explains the routing decision for a hypothetical
/// (modality, phi, tokens) tuple. Returns the selected provider plus a
/// per-candidate score breakdown (cost / quality / latency normalised
/// scores and the composite). Surfaced via
/// <c>GET /api/ai/routing/preview</c> for ItAdmin support tooling.
/// </summary>
public interface IRoutingPreviewService
{
    Task<RoutingPreview> PreviewAsync(
        Tenant tenant,
        bool containsPhi,
        string? modality,
        int? estimatedInputTokens,
        int? estimatedOutputTokens,
        CancellationToken ct);
}

public sealed record RoutingPreview(
    Guid? SelectedProviderId,
    string? SelectedProviderName,
    string? Reason,
    IReadOnlyList<RoutingCandidate> Candidates,
    RoutingWeights Weights);

public sealed record RoutingWeights(double Cost, double Quality, double Latency);

public sealed record RoutingCandidate(
    Guid ProviderId,
    string Name,
    string Adapter,
    string Compliance,
    bool Eligible,
    string? IneligibleReason,
    decimal CostUsdEstimate,
    double CostScore,
    double QualityScore,
    double LatencyScore,
    int? P95LatencyMs24h,
    double CompositeScore);

/// <summary>
/// Per-tenant AI usage ledger. Every <see cref="AiGateway"/> route call writes
/// one <see cref="AiRequest"/> row regardless of outcome (ok / blocked / error)
/// so administrators have an exhaustive trace for AI-012, BILL-002, and the
/// §13.2 audit-completeness KPI.
/// </summary>
public interface IAiUsageStore
{
    Task RecordAsync(AiRequest request, CancellationToken ct);

    Task<UsageSummary> SummariseAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

public sealed record UsageSummary(
    int TotalRequests,
    int OkCount,
    int BlockedCount,
    int ErrorCount,
    long InputTokens,
    long OutputTokens,
    int AvgLatencyMs,
    IReadOnlyList<UsageByProvider> ByProvider,
    decimal CostTotalUsd = 0m);

/// <summary>
/// Iter-34 BILL-005 — per-provider usage rollup priced via the matching
/// <c>ProviderConfig.CostPerInputKToken</c> / <c>CostPerOutputKToken</c>
/// (USD per 1K tokens). When no current <see cref="ProviderConfig"/> matches
/// the historical <see cref="AiRequest.Provider"/> name (e.g. retired
/// provider) the cost columns stay <c>0</c> and <see cref="Unpriced"/> is
/// <c>true</c> so the UI can flag it.
/// </summary>
public sealed record UsageByProvider(
    string Provider,
    string Adapter,
    int Requests,
    long InputTokens,
    long OutputTokens,
    decimal CostInputUsd = 0m,
    decimal CostOutputUsd = 0m,
    decimal CostTotalUsd = 0m,
    bool Unpriced = false);

/// <summary>
/// PRD BILL-001..006 — minimal data-access surface for plan-quota enforcement.
/// Lives in the Application layer (abstraction); the EF implementation lives
/// in <c>RadioPad.Infrastructure</c>. Used by <c>PlanQuotaService</c>.
/// </summary>
public interface IPlanQuotaStore
{
    /// <summary>Successful AI usage totals since a billing-period boundary.</summary>
    Task<PlanQuotaUsage> GetOkAiUsageAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Count successful (Status == "ok") AI calls for a tenant since the given
    /// instant. Used to compare against the plan's monthly AI-call quota.
    /// </summary>
    Task<int> CountOkAiCallsAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Persist a mutated <see cref="TenantSettings"/> row (e.g. flipping
    /// <c>SuspendedAt</c> when the grace period elapses). Implementation is
    /// idempotent — passing a row with no changes is a no-op.
    /// </summary>
    Task SaveSettingsAsync(TenantSettings settings, CancellationToken ct);

    /// <summary>
    /// Load the singleton <see cref="TenantSettings"/> row for the given
    /// tenant, or <c>null</c> when none has been provisioned yet. Used by
    /// the AI gateway and suspension guard to evaluate quotas without
    /// reaching across the layering boundary into the EF context.
    /// </summary>
    Task<TenantSettings?> LoadSettingsAsync(Guid tenantId, CancellationToken ct);
}

public sealed record PlanQuotaUsage(int AiCalls, long InputTokens, long OutputTokens);

/// <summary>
/// Transactional email sender abstraction. Implementations may use HTTPS
/// REST APIs (Resend, SendGrid, Mailgun) or SMTP as a fallback.
/// DigitalOcean blocks SMTP ports so the primary implementation should be
/// an HTTPS-based provider for guaranteed deliverability.
/// </summary>
public interface IEmailSender
{
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainBody = null,
    string? From = null,
    string? ReplyTo = null);
