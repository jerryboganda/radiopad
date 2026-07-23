using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using RadioPad.Api.Auth;
using RadioPad.Api.Jobs;
using RadioPad.Api.Middleware;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Providers;
using RadioPad.Application.Services;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Repositories;
using RadioPad.Infrastructure.Seeding;
using RadioPad.Validation.Engine;

var builder = WebApplication.CreateBuilder(args);

// Iter-32 AUTH-001 — apply named OIDC presets (keycloak / auth0 / okta) to
// the environment before any middleware reads them. Operator-supplied env
// vars always win.
RadioPad.Api.Auth.OidcProfiles.ApplyToEnvironment(
    Environment.GetEnvironmentVariable("RADIOPAD_OIDC_PRESET"));
RadioPad.Api.Auth.RadioPadBearerTokens.ValidateStartupSecret(builder.Environment);

if (!builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing")
    && RadioPadBearerToken.UsesDefaultSecret)
{
    throw new InvalidOperationException(
        "RADIOPAD_AUTH_SECRET must be set outside Development/Testing so RadioPad bearer tokens are not signed with the default secret.");
}

if (!builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing")
    && (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_REF"))
        || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_WRAPPED"))))
{
    throw new InvalidOperationException(
        "RADIOPAD_COLUMN_KEY_REF and RADIOPAD_COLUMN_KEY_WRAPPED must be set outside Development/Testing so encrypted columns do not use the dev fallback key.");
}

// Bind to localhost by default (safety boundary §local-trust).
var bindUrl = Environment.GetEnvironmentVariable("RADIOPAD_BIND") ?? "http://127.0.0.1:7457";
if (!bindUrl.Contains("://", StringComparison.Ordinal)) bindUrl = $"http://{bindUrl}";
builder.WebHost.UseUrls(bindUrl);

// Connection string is resolved once at top level so both EF Core and Hangfire
// share the exact same sniff (Host=/Server= => Postgres, else SQLite / Hangfire
// InMemory). The AddDbContext lambda below closes over this outer `conn`.
var conn = builder.Configuration.GetConnectionString("RadioPad")
    ?? Environment.GetEnvironmentVariable("RADIOPAD_DB")
    ?? "Data Source=radiopad.dev.db";
builder.Services.AddDbContext<RadioPadDbContext>(opt =>
{
    // Connection-string sniff: "Host=..." or "Server=...;Port=..." => Postgres,
    // anything else falls back to SQLite for the dev workstation case.
    if (conn.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
        conn.Contains("Server=", StringComparison.OrdinalIgnoreCase))
    {
        opt.UseNpgsql(conn);
    }
    else
    {
        opt.UseSqlite(conn);
    }
});

// PR-N1 — Hangfire cron platform bootstrap (storage + processing server + global
// retry policy). Storage mirrors the EF sniff above (Postgres => PostgreSql in its
// own `hangfire` schema; SQLite/desktop => InMemory). Skipped under the Testing
// environment — like DevSeed and the UBAG discovery sweeper — so integration tests
// never spin up a processing server or contend on a per-fixture database; tests
// drive the job classes' sweep/scan methods directly. The job CLASSES are
// registered unconditionally further below so they resolve even under Testing.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.AddRadioPadHangfire(conn);

builder.Services.AddHttpClient();
// In-memory store for short-lived, single-use WebAuthn challenges (AUTH-001).
// Challenges expire in ~2 minutes; a single backend instance (desktop sidecar
// and the single-VPS prod deployment) makes an in-process cache sufficient.
// Move to a distributed cache if the API is ever scaled horizontally.
builder.Services.AddMemoryCache();
// Transactional email via HTTPS REST API (Resend/SendGrid/Mailgun).
// Bypasses DigitalOcean SMTP port block for guaranteed deliverability.
builder.Services.AddHttpClient<RadioPad.Infrastructure.Email.HttpEmailSender>();
builder.Services.AddSingleton<IEmailSender, RadioPad.Infrastructure.Email.HttpEmailSender>();
// Iter-31 — shared HttpClient pool for production AI provider adapters
// (Azure OpenAI, AWS Bedrock, Vertex AI, OpenAI direct, OpenAI-compatible).
// Per-adapter timeout is 60 s; overrideable via RADIOPAD_AI_HTTP_TIMEOUT_SEC.
// 2026-07-11 UBAG hardening — resilience pipeline on both AI HttpClients:
// transient retry, circuit breaker, and per-attempt timeout. The pipeline
// owns the timing budget, so HttpClient.Timeout is raised to sit ABOVE the
// pipeline's TotalRequestTimeout (it is only the last-resort backstop).
// The circuit breaker is tuned for this low-traffic deployment: it opens
// only when nearly every call in the sampling window fails, and recovers in
// seconds — it stops a dead gateway from being hammered without ever
// tripping on a routine bad request.
var aiTimeoutSec = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_AI_HTTP_TIMEOUT_SEC"), out var aiSec) && aiSec > 0 ? aiSec : 60;
builder.Services.AddHttpClient("ai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(aiTimeoutSec + 40);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
.AddStandardResilienceHandler(o =>
{
    // A full-length LLM completion must never be cut short by the attempt
    // timeout — it equals the operator-configured per-call budget; the retry
    // only helps calls that fail FAST (connect refused, 5xx). Clamped so the
    // options stay valid for any configured value (see the ubag pipeline).
    var attemptSec = Math.Max(1, aiTimeoutSec);
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptSec);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(attemptSec + 30);
    o.Retry.MaxRetryAttempts = 1;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(attemptSec * 2, 30));
    o.CircuitBreaker.MinimumThroughput = 8;
    o.CircuitBreaker.FailureRatio = 0.9;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
});
// On-device llama.cpp completions run on THIS workstation's CPU, not a cloud API — a cold
// model load plus token generation legitimately takes minutes, not the ~60 s a cloud call
// budgets for. Sharing the "ai" client above would (a) cut a still-loading/still-generating
// local completion off mid-flight and report it as a failure even though it was working, and
// (b) let a slow local call trip the SAME circuit breaker that gates unrelated cloud providers.
// A dedicated client with a long timeout and NO retry (retrying a slow-but-working local
// server just doubles the CPU load it's already straining under) avoids both. Overrideable via
// RADIOPAD_LOCAL_AI_HTTP_TIMEOUT_SEC for a workstation that needs even longer.
var localAiTimeoutSec = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_LOCAL_AI_HTTP_TIMEOUT_SEC"), out var localAiSec) && localAiSec > 0 ? localAiSec : 300;
builder.Services.AddHttpClient("ai-local", c =>
{
    c.Timeout = TimeSpan.FromSeconds(localAiTimeoutSec);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var ubagTimeoutMs = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_UBAG_TIMEOUT_MS"), out var ubagMs) && ubagMs > 0 ? ubagMs : 120_000;
builder.Services.AddHttpClient(RadioPad.Infrastructure.Providers.Ubag.UbagClient.HttpClientName, c =>
{
    var baseUrl = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_BASE_URL") ?? "https://ubag.polytronx.com";
    c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromMilliseconds(ubagTimeoutMs + 15_000);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
.AddStandardResilienceHandler(o =>
{
    // Individual gateway calls (job create, 2 s polls, artifact upload) are
    // short; 45 s covers the worst artifact upload while leaving room for a
    // retry inside the job-poll budget. Retried POSTs are safe — every
    // mutating call carries an Idempotency-Key. The clamps keep the options
    // VALID for any operator-supplied timeout (total must exceed attempt,
    // sampling must be at least double the attempt) — options validation
    // failing at startup would take the whole API down.
    var totalMs = Math.Max(ubagTimeoutMs, 2_000);
    var attemptMs = Math.Min(45_000, totalMs / 2);
    o.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(attemptMs);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(totalMs);
    o.Retry.MaxRetryAttempts = 2;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(Math.Max(attemptMs * 2, 30_000));
    o.CircuitBreaker.MinimumThroughput = 8;
    o.CircuitBreaker.FailureRatio = 0.9;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
});

// AI provider adapters
builder.Services.AddSingleton<IAiProviderAdapter, MockAiAdapter>();
builder.Services.AddSingleton<IAiProviderAdapter, AnthropicAiAdapter>();
builder.Services.AddSingleton<IAiProviderAdapter, OllamaAiAdapter>();
// Iter-31 production adapters.
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.AzureOpenAiProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.AwsBedrockProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.GoogleVertexAiProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.OpenAiDirectProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider>();
// Iter-32 AI-011 — local-only adapters. Default to 127.0.0.1; remote URLs
// require explicit operator configuration. Compliance class defaults to
// LocalOnly so PHI may be routed when the operator opts in.
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Local.OllamaProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Local.VLlmProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Local.LlamaCppProvider>();
// Iter-36 AI-012 — CLI-spawning adapters (default Sandbox because the
// local binary may call vendor cloud APIs). The
// per-process launcher is a singleton so test code can substitute it.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Cli.IProcessLauncher,
    RadioPad.Infrastructure.Providers.Cli.DefaultProcessLauncher>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Cli.GeminiCliProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Cli.CodexCliProvider>();
builder.Services.AddSingleton<IUbagClient, RadioPad.Infrastructure.Providers.Ubag.UbagClient>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Ubag.UbagProviderAdapter>();
// 2026-07-11 UBAG hardening — operator alerting (Hub banner + throttled email)
// for logged-out browser sessions and an unreachable gateway. Singleton: banner
// and throttle state must survive across the scoped discovery sweeps.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Ubag.UbagOperatorAlertService>();
// Async report-AI jobs (submit + poll): generation must survive proxy timeouts
// and client disconnects, so it runs detached from the HTTP request. Singleton:
// poll requests arrive on different scopes than the submit.
builder.Services.AddSingleton<RadioPad.Api.Services.AiJobRegistry>();
// In-process fan-out bus for the SSE stream (PR-B1): terminal job transitions,
// streamed progress/partials, and notifications. Singleton — SSE connections
// subscribe on their own request scopes, publishers push from the runner's scopes.
builder.Services.AddSingleton<RadioPad.Api.Services.IAiJobEventBus, RadioPad.Api.Services.AiJobEventBus>();
// NOTIF-001 — the notification producer is one object registered three ways: the
// INotificationProducer that workflow call sites invoke, and the IHostedService whose
// firehose bus subscription drains terminal AI-job events into AiJob notifications.
builder.Services.AddSingleton<RadioPad.Api.Services.NotificationProducer>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.INotificationProducer>(
    sp => sp.GetRequiredService<RadioPad.Api.Services.NotificationProducer>());
// The firehose bus subscription is skipped under Testing (like the Ubag discovery + seed
// services) so it never holds an open subscription that per-fixture SSE tests assert away,
// nor produces AiJob notifications that would perturb unrelated integration fixtures. The
// INotificationProducer itself stays registered so controllers/wiring resolve it, and the
// producer is exercised directly in NotificationProducerTests.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(
        sp => sp.GetRequiredService<RadioPad.Api.Services.NotificationProducer>());
// Companion relay (desktop↔phone). The registry holds live relay sockets in
// memory (process-local, single instance); the session service manages the
// durable pairing record.
builder.Services.AddSingleton<RadioPad.Api.Services.CompanionRelayRegistry>();
builder.Services.AddScoped<RadioPad.Api.Services.CompanionSessionService>();
// Dynamic UBAG provider auto-discovery: keeps each tenant's UBAG provider rows in sync
// with the gateway's live target catalog + login state, so any web AI the operator logs
// into via the UBAG Chromium session appears in the picker automatically (no dev needed).
builder.Services.AddScoped<RadioPad.Infrastructure.Providers.Ubag.UbagProviderDiscoveryService>();
// The background sweeper is skipped under the Testing environment (like DevSeed) so it
// never contends on a per-fixture SQLite database during integration tests; the on-demand
// POST /api/ubag/refresh-targets path still uses the scoped service above.
// Kept as a BackgroundService (NOT migrated to Hangfire): its first pass ~8s after boot
// is a startup-seed-critical discovery that must run before the first AI call.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<RadioPad.Infrastructure.Providers.Ubag.UbagProviderDiscoveryHostedService>();

// PR-N2 — the audit log is decorated so an append fans out to any active, audit-subscribed
// outbound webhook endpoint (WebhookDispatchJob). The decorator forwards to the real
// EfAuditLog first, then enqueues via Hangfire's IBackgroundJobClient. IBackgroundJobClient is
// resolved with GetService so the decorator is a no-op enqueue under Testing (no Hangfire).
builder.Services.AddScoped<EfAuditLog>();
builder.Services.AddScoped<IAuditLog>(sp => new RadioPad.Api.Services.WebhookEnqueueingAuditLog(
    sp.GetRequiredService<EfAuditLog>(),
    sp.GetRequiredService<RadioPadDbContext>(),
    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
    sp.GetService<Hangfire.IBackgroundJobClient>(),
    sp.GetRequiredService<ILogger<RadioPad.Api.Services.WebhookEnqueueingAuditLog>>()));
builder.Services.AddSingleton<RadioPad.Application.Security.IPermissionService, RadioPad.Application.Security.RolePermissionService>();
builder.Services.AddScoped<RadioPad.Api.Auth.LockoutPolicy>();
builder.Services.AddScoped<IRulebookStore, EfRulebookStore>();
builder.Services.AddScoped<IAiUsageStore, EfAiUsageStore>();
builder.Services.AddScoped<IProviderRouter, EfProviderRouter>();
// Iter-32 AI-010 — routing-preview surface (ItAdmin only).
builder.Services.AddScoped<IRoutingPreviewService, EfRoutingPreviewService>();
builder.Services.AddScoped<AiGateway>();
builder.Services.AddScoped<IAiGateway>(sp =>
    new RadioPad.Api.Services.PerfInstrumentedAiGateway(sp.GetRequiredService<AiGateway>()));
builder.Services.AddSingleton<ReportValidator>();
builder.Services.AddScoped<ReportingService>();
// Iter-35 — versioned clinical validation packs.
builder.Services.AddScoped<RadioPad.Api.Services.ValidationPackService>();
// PRD §18 — advanced analytics dashboard service.
builder.Services.AddScoped<RadioPad.Application.Services.AnalyticsService>();
builder.Services.AddSingleton<RadioPad.Application.Services.HallucinationDetector>();
builder.Services.AddSingleton<RadioPad.Application.Services.MeasurementExtractionService>();
builder.Services.AddScoped<RadioPad.Application.Services.ITerminologyAdapter, RadioPad.Application.Services.NoOpTerminologyAdapter>();
// Iter-31 AI-009 / AI-001 — per-tenant prompt overrides + dictation cleanup.
builder.Services.AddScoped<IPromptOverrideStore, EfPromptOverrideStore>();
builder.Services.AddScoped<RadioPad.Application.Abstractions.IDictationCleanupService,
    RadioPad.Application.Services.DictationCleanupService>();
// Dictation-engine brief §4.2/§5 — deterministic on-device safety pipeline. Stateless singletons
// that wrap whichever formatter runs (cloud default / local MedGemma optional).
builder.Services.AddSingleton<RadioPad.Application.Dictation.DeterministicPassThrough>();
builder.Services.AddSingleton<RadioPad.Application.Dictation.DictationValidationService>();
builder.Services.AddSingleton<RadioPad.Application.Dictation.LateralityNegationSentinel>();
builder.Services.AddSingleton<RadioPad.Application.Dictation.DictationEngineService>();
// §4.4 — model load/unload memory manager (≤5 GB combined resident, CPU-only).
builder.Services.AddSingleton<RadioPad.Application.Runtime.ModelMemoryManager>();
// §5.7 — local dictation audit store. In-memory default (DI-safe on web/server); the desktop wires
// the encrypted on-disk FileDictationAuditStore (see IMPLEMENTATION_NOTES.md — key management).
builder.Services.AddSingleton<RadioPad.Application.Dictation.IDictationAuditStore,
    RadioPad.Application.Dictation.InMemoryDictationAuditStore>();
// §4.2 — safety-wrapped dictation draft pipeline (pass-through → formatter → validation → sentinel
// → audit). Scoped: wraps the scoped IDictationCleanupService.
builder.Services.AddScoped<RadioPad.Application.Dictation.IDictationDraftService,
    RadioPad.Application.Dictation.DictationDraftService>();
// §2.2 — optional on-device MedGemma report formatter (LocalOnly, loopback-enforced). Inert unless
// RADIOPAD_LOCAL_FORMATTER_ENABLED is set; the cloud formatter stays the default everywhere else.
// The llama-server runtime is provisioned ON DEMAND (not bundled in the installer) and started
// lazily by LlamaServerProcess — singleton so one process is shared and disposed with the host,
// never leaving an orphan holding gigabytes of model.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.LlamaServerProcess>();
// Desktop sidecar's async on-device generation runner (LocalGenerationController /jobs endpoints).
// Singleton so its single-slot semaphore serialises every local job behind the single-request
// llama-server, and its in-memory stage map is shared across polls. Inert on web/server (the
// controller gates every endpoint on RADIOPAD_LOCAL_STT_ENABLED), so registering it always is safe.
builder.Services.AddSingleton<RadioPad.Api.Services.LocalGenerationJobRunner>();
builder.Services.AddSingleton<RadioPad.Application.Dictation.ILocalReportFormatter,
    RadioPad.Infrastructure.Providers.Local.LocalMedGemmaFormatter>();
// Cross-check LLM medical-accuracy review (hosted-side; routes via IAiGateway so
// PHI policy + audit apply). Opt-in UBAG is honored by a forced provider.
builder.Services.AddScoped<RadioPad.Application.Abstractions.ICrossCheckReviewService,
    RadioPad.Application.Services.CrossCheckReviewService>();
// Phase B (dictation transcription) — audio → transcript via the UBAG
// medical_transcription flow. Uses the existing "ubag" named HttpClient
// (IUbagClient) — no new client/config needed.
builder.Services.AddScoped<RadioPad.Application.Abstractions.ITranscriptionService,
    RadioPad.Application.Services.TranscriptionService>();
// Phase 1 (local STT) — fully on-device, offline transcription, decoded
// in-process (no ffmpeg). Registered always; ILocalSttClient.Available stays
// false UNLESS RADIOPAD_LOCAL_STT_ENABLED is set and the model is present (the
// desktop STT sidecar). Two consumers, by design:
//   • Desktop → the anonymous, loopback-only POST /api/stt/transcribe
//     (SttController) which requires the on-device engine and has NO cloud
//     fallback (PHI audio must never leave the machine; 503 until the model
//     provisions on first run).
//   • Web / mobile → the report-scoped POST /api/reports/{id}/dictation/transcribe
//     where TranscriptionService keeps the UBAG cloud path (Available == false).
// The recognizer loads the native model once (singleton); the WAV decoder is stateless.
builder.Services.AddSingleton<RadioPad.Infrastructure.Audio.IAudioDecoder,
    RadioPad.Infrastructure.Audio.WavAudioDecoder>();
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.SherpaParakeetSttClient>();
// The ensemble orchestrator is the ILocalSttClient: it reconciles the available
// engines when RADIOPAD_STT_ENSEMBLE is on (≥2 available), else transcribes
// single-engine.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ILocalSttClient,
    RadioPad.Infrastructure.Providers.Local.LocalSttEnsemble>();
// Engines register as ILocalSttEngine for the orchestrator to consume.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ILocalSttEngine>(
    sp => sp.GetRequiredService<RadioPad.Infrastructure.Providers.Local.SherpaParakeetSttClient>());
// MedASR (Google Conformer-CTC, radiology-tuned) — the DEFAULT primary on-device engine (D2), via
// the public/ungated sherpa-onnx CTC bundle. Same sherpa-onnx CPU runtime as Parakeet; Available is
// false until the bundle is provisioned, so it degrades gracefully to Parakeet / the cloud path.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.SherpaMedAsrSttClient>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ILocalSttEngine>(
    sp => sp.GetRequiredService<RadioPad.Infrastructure.Providers.Local.SherpaMedAsrSttClient>());
// Windows on-device speech (System.Speech / SAPI) — the classic Windows Speech
// Recognition engine, fully on-device (PHI-safe) and built into Windows. Registered
// always; Available is false off Windows / when no recognizer is installed (the
// engine guards every call with OperatingSystem.IsWindows()), so it is inert on the
// Linux server build. This is the preferred default dictation engine on the desktop.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.WindowsSapiSttClient>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ILocalSttEngine>(
    sp => sp.GetRequiredService<RadioPad.Infrastructure.Providers.Local.WindowsSapiSttClient>());
// Manual "Cross Check" pass: re-runs retained audio through all available engines,
// N-way ROVER reconcile vs the live draft, then (later phases) an LLM medical pass.
// Singletons (engines are singletons); jobs are tracked in-memory, non-durable.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ICrossCheckJobStore,
    RadioPad.Infrastructure.Providers.Local.InMemoryCrossCheckJobStore>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.ICrossCheckService,
    RadioPad.Infrastructure.Providers.Local.CrossCheckService>();
// First-run download of the on-device STT model. The hosted service is a no-op
// unless RADIOPAD_LOCAL_STT_ENABLED is set (desktop), so web/server are unaffected.
// Kept as a BackgroundService (NOT migrated to Hangfire): boot-once provisioning.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.SttModelProvisioner>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<RadioPad.Infrastructure.Providers.Local.SttModelProvisionHostedService>();
// On-device model manager (LocalModelsController) — download-progress tracker +
// generalized catalog (STT now; TTS + orchestrator are roadmap placeholders).
// Singletons; the controller gates every action on RADIOPAD_LOCAL_STT_ENABLED so
// these are inert on web/server.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.IModelProvisioningStatus,
    RadioPad.Infrastructure.Providers.Local.ModelProvisioningStatus>();
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.ILocalModelCatalog,
    RadioPad.Infrastructure.Providers.Local.LocalModelCatalog>();
// Persisted per-workstation "primary STT model" selection, honored by the ensemble
// and set via POST /api/local-models/{id}/primary.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Local.ILocalSttSettings,
    RadioPad.Infrastructure.Providers.Local.LocalSttSettings>();
// PRD BILL-001..006 — billing helpers (audit + plan quota + subscription lifecycle).
builder.Services.AddScoped<IPlanQuotaStore, EfPlanQuotaStore>();
builder.Services.AddScoped<RadioPad.Application.Services.IBillingAudit, RadioPad.Application.Services.BillingAudit>();
builder.Services.AddScoped<RadioPad.Application.Services.PlanQuotaService>();
builder.Services.AddScoped<RadioPad.Application.Services.SubscriptionLifecycleService>();
// Iter-30 RPT-007 — multi-mode rewrite service.
builder.Services.AddScoped<RadioPad.Application.Services.IReportRewriteService, RadioPad.Application.Services.ReportRewriteService>();
// Iter-30 STD-001/STD-002 — terminology adapters loaded once on startup.
builder.Services.AddSingleton<RadioPad.Application.Services.IRadLexService>(sp =>
    new RadioPad.Application.Services.RadLexService(ResolveTerminologyPath("radlex_subset.yaml")));
builder.Services.AddSingleton<RadioPad.Application.Services.IRadsService>(sp =>
    new RadioPad.Application.Services.RadsService(ResolveTerminologyPath("rads.yaml")));

static string ResolveTerminologyPath(string file)
{
    // Repo layout: <root>/rulebooks/_terminology/<file>. The API binary lives
    // far below the repo root in dev (bin/Debug/net8.0); production deploys
    // ship the YAML alongside the binary. Probe both.
    var probe = Path.Combine(AppContext.BaseDirectory, "rulebooks", "_terminology", file);
    if (File.Exists(probe)) return probe;
    var devProbe = Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "..", "..", "rulebooks", "_terminology", file);
    return Path.GetFullPath(devProbe);
}

builder.Services.AddSingleton<RadioPad.Api.Services.IDicomWebClient, RadioPad.Api.Services.DicomWebClient>();

// Iter-33 INT-007 — vendor PACS adapters (Sectra IDS7, Visage 7, Carestream
// Vue). Keyed by vendor slug so the per-tenant router can pick one based on
// `TenantSettings.PacsVendor`. Each adapter uses a named HttpClient so the
// HTTP factory can apply per-vendor timeouts and handlers without colliding
// with the generic DICOMweb client.
builder.Services.AddHttpClient(RadioPad.Infrastructure.Pacs.SectraIds7Adapter.ClientName,
    c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(RadioPad.Infrastructure.Pacs.Visage7Adapter.ClientName,
    c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(RadioPad.Infrastructure.Pacs.CarestreamVueAdapter.ClientName,
    c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddKeyedSingleton<RadioPad.Application.Services.Pacs.IPacsVendorAdapter,
    RadioPad.Infrastructure.Pacs.SectraIds7Adapter>("sectra");
builder.Services.AddKeyedSingleton<RadioPad.Application.Services.Pacs.IPacsVendorAdapter,
    RadioPad.Infrastructure.Pacs.Visage7Adapter>("visage");
builder.Services.AddKeyedSingleton<RadioPad.Application.Services.Pacs.IPacsVendorAdapter,
    RadioPad.Infrastructure.Pacs.CarestreamVueAdapter>("carestream");
builder.Services.AddSingleton<RadioPad.Application.Services.Pacs.IPacsVendorRouter,
    RadioPad.Infrastructure.Pacs.PacsVendorRouter>();

// PR-N1 — cron-shaped background work now runs on Hangfire (see AddRadioPadHangfire
// / UseRadioPadRecurringJobs). The former BackgroundServices — RetentionWorker,
// CriticalResultEscalationService, AnomalyDetector, OAuthRefreshRotationService,
// ModelDriftDetectionService — are replaced by recurring jobs in RadioPad.Api.Jobs.
// The job classes are registered UNCONDITIONALLY (even under Testing, where no
// Hangfire server runs) so tests can resolve and invoke their sweep/scan methods
// directly. They hold only IServiceScopeFactory (+ logger / IHttpClientFactory)
// and open their own scope per pass, so a singleton lifetime is safe.
builder.Services.AddSingleton<RadioPad.Api.Jobs.RetentionSweepJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.CriticalResultEscalationJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.AnomalyScanJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.OAuthRefreshRotationJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.ModelDriftDetectionJob>();

// PR-N2 — the four planned cron-platform jobs. Registered unconditionally (even under
// Testing, where no Hangfire server runs) so tests resolve and invoke their unit methods
// directly. Each holds only IServiceScopeFactory (+ logger) and opens its own scope per pass,
// so a singleton lifetime is safe. The three recurring ones are scheduled in
// UseRadioPadRecurringJobs; WebhookDispatchJob is enqueue-only (default queue).
builder.Services.AddSingleton<RadioPad.Api.Jobs.AuditExportRollupJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.WebhookDispatchJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.AiCostRollupJob>();
builder.Services.AddSingleton<RadioPad.Api.Jobs.OrphanedDraftCleanupJob>();
// PR-N4 — notification channel dispatch (push + email). Enqueue-only on the critical
// queue by NotificationProducer for Critical-urgency notifications; a singleton for the
// same reason as the others (holds only IServiceScopeFactory + logger, own scope per run).
builder.Services.AddSingleton<RadioPad.Api.Jobs.NotificationChannelDispatchJob>();

// Async AI generation jobs (durable job platform). The unbounded channel is the
// hand-off between the request-scoped coordinator (writer) and the hosted runner
// (reader); the coordinator write-throughs every state transition to the AiJobs
// table so a restart/reload no longer forgets a running job.
// AiJobRecoveryHostedService is registered BEFORE the runner so its boot sweep
// marks orphaned queued/running rows server_restart before any new work is consumed.
builder.Services.AddSingleton(System.Threading.Channels.Channel.CreateUnbounded<RadioPad.Api.Services.AiJobWork>());
builder.Services.AddScoped<RadioPad.Api.Services.AiJobCoordinator>();
// Kept as BackgroundServices (NOT migrated to Hangfire): AiJobRecovery is boot-once
// (runs before the runner consumes), and AiJobRunner is a channel consumer, not a
// cron — neither is a scheduled sweep.
builder.Services.AddHostedService<RadioPad.Api.Services.AiJobRecoveryHostedService>();
builder.Services.AddHostedService<RadioPad.Api.Services.AiJobRunner>();

// PRD §18.2 model drift detection now runs as the recurring Hangfire job
// RadioPad.Api.Jobs.ModelDriftDetectionJob (registered above; cron derived from
// ResolveInterval() in UseRadioPadRecurringJobs). DriftController resolves the same
// job class for the manual /api/admin/drift run + status endpoints.

// Iter-35 PERF-004 — synthetic availability monitor. Runs an in-process
// HTTP probe loop against a configurable list of relative paths (default
// `/api/health/ready`) and maintains a 5-minute rolling failure window.
// Burn-rate breaches are appended to the audit log when
// `RADIOPAD_AVAILABILITY_AUDIT_TENANT` is set. The named HttpClient is
// pre-configured with the local bind URL so probes never leave the host.
{
    var bind = Environment.GetEnvironmentVariable("RADIOPAD_BIND") ?? "http://127.0.0.1:7457";
    if (!bind.Contains("://", StringComparison.Ordinal)) bind = "http://" + bind;
    builder.Services.AddHttpClient(RadioPad.Api.Services.AvailabilityMonitorService.HttpClientName, c =>
    {
        c.BaseAddress = new Uri(bind);
        c.Timeout = TimeSpan.FromSeconds(5);
    });
}
builder.Services.AddSingleton<RadioPad.Api.Services.IAvailabilitySnapshotProvider,
    RadioPad.Api.Services.AvailabilitySnapshotProvider>();
builder.Services.AddSingleton<RadioPad.Api.Services.AvailabilityMonitorService>();
// Kept as a BackgroundService (NOT migrated to Hangfire): a seconds-level probe
// interval plus in-memory 5-minute sliding-window state — Hangfire's 1-minute
// granularity + stateless job instances would change its semantics.
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RadioPad.Api.Services.AvailabilityMonitorService>());
// Iter-31 INT-006 — HL7 v2 MLLP listener. No-op when RADIOPAD_HL7_MLLP_PORT
// is not set; binds 127.0.0.1 by default (override with RADIOPAD_HL7_MLLP_BIND).
// Kept as a BackgroundService (NOT migrated to Hangfire): a socket listener, not a cron.
builder.Services.AddSingleton<RadioPad.Infrastructure.Integration.Hl7MessageHandler>();
builder.Services.AddHostedService<RadioPad.Infrastructure.Integration.Hl7MllpListener>();
// Iter-33 INT-008 — Orthanc bridge HL7 outbox (in-process; future iter swaps
// in a database-backed implementation behind IHl7Outbox).
builder.Services.AddSingleton<RadioPad.Application.Services.Hl7Bridge.IHl7Outbox,
    RadioPad.Application.Services.Hl7Bridge.InMemoryHl7Outbox>();
// PRD SEC-003 — pluggable KMS providers for envelope encryption of tenant
// data keys. `env:` and `local:` are bundled real implementations; `aws:`,
// `azkv:`, `gcp:` are the iter-32 cloud adapters under
// RadioPad.Infrastructure.Kms (require AWSSDK.KeyManagementService /
// Azure.Security.KeyVault.Keys / Google.Cloud.Kms.V1 + IAM permissions).
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsProvider, RadioPad.Application.Services.Kms.EnvKmsProvider>();
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsProvider, RadioPad.Application.Services.Kms.LocalKmsProvider>();
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsProvider, RadioPad.Infrastructure.Kms.AwsKmsProvider>();
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsProvider, RadioPad.Infrastructure.Kms.AzureKeyVaultKmsProvider>();
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsProvider, RadioPad.Infrastructure.Kms.GcpKmsProvider>();
builder.Services.AddSingleton<RadioPad.Application.Services.Kms.IKmsResolver, RadioPad.Application.Services.Kms.DefaultKmsResolver>();
// Iter-32 SEC-003 — 5-minute in-memory cache of unwrapped tenant DEKs.
builder.Services.AddSingleton<RadioPad.Infrastructure.Kms.ITenantDekCache, RadioPad.Infrastructure.Kms.TenantDekCache>();

// Iter-35 PROV-007 — OAuth refresh-token vault + scheduled rotation worker.
// The default token issuer is the no-op stub; real adapters land alongside
// a live IdP integration in a later iteration. The hosted service detects
// `CanRefresh = false` and exits its scan early, so the worker is safe to
// always register.
builder.Services.AddScoped<RadioPad.Application.Services.OAuthRefreshVault>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IOAuthTokenIssuer,
    RadioPad.Application.Abstractions.NoopOAuthTokenIssuer>();
// PROV-007 rotation now runs as the recurring Hangfire job
// RadioPad.Api.Jobs.OAuthRefreshRotationJob (registered above; cron */15 * * * *).
// It still exits early when the issuer reports CanRefresh = false.

// Iter-31 SEC-002 — at-rest column-level encryption. The data key is
// resolved once at startup from RADIOPAD_COLUMN_KEY_REF (KMS reference) +
// RADIOPAD_COLUMN_KEY_WRAPPED (b64 wrapped DEK). When unset (dev/test) the
// encryptor falls back to a deterministic key so EnsureCreated suites work
// without operator setup. Production fails closed if either value is missing.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IColumnEncryptor>(sp =>
{
    var resolver = sp.GetService<RadioPad.Application.Services.Kms.IKmsResolver>();
    var keyRef = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_REF");
    var wrapped = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_WRAPPED");
    if (builder.Environment.IsProduction() &&
        (string.IsNullOrWhiteSpace(keyRef) || string.IsNullOrWhiteSpace(wrapped)))
    {
        throw new InvalidOperationException("Production requires RADIOPAD_COLUMN_KEY_REF and RADIOPAD_COLUMN_KEY_WRAPPED for column encryption.");
    }

    var enc = RadioPad.Infrastructure.Security.AesGcmColumnEncryptor
        .CreateAsync(resolver, keyRef, wrapped, default)
        .GetAwaiter().GetResult();
    RadioPad.Infrastructure.Security.ColumnEncryptorAccessor.Current = enc;
    return enc;
});

// Iter-31 MCP-001..007 — Model Context Protocol sandbox + registry. The
// in-process sandbox is the v0.1 implementation (a future iter swaps in a
// WASM sandbox via Wasmtime without changing the controller surface).
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IMcpSandbox,
    RadioPad.Application.Services.Mcp.InProcessMcpSandbox>();
// Iter-32 MCP-005 — default-deny scope policy + invocation service.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IMcpScopePolicy,
    RadioPad.Application.Services.Mcp.McpScopePolicy>();
builder.Services.AddSingleton<RadioPad.Application.Services.Mcp.McpManifestVerifier>();
builder.Services.AddScoped<RadioPad.Api.Services.McpInvocationService>();

// Iter-33 MCP-007 — plugin trust + capability gate + per-OS sandbox.
builder.Services.AddScoped<RadioPad.Application.Services.Mcp.PluginManifestSignatureVerifier>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IMcpCapabilityRegistry,
    RadioPad.Application.Services.Mcp.InMemoryMcpCapabilityRegistry>();
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IPluginSandbox>(
    _ => RadioPad.Application.Services.Mcp.PluginSandboxFactory.CreateForCurrentOs());

// Iter-35 AUTH-001 — FIDO MDS3 trusted-root pinning for packed-attestation
// x5c chains. The embedded source ships with the build; the HTTP source is
// disabled by default (gated on RADIOPAD_FIDO_MDS3_URL + JWT verification
// placeholder). Tests override this registration via WebApplicationFactory.
builder.Services.AddSingleton<RadioPad.Application.Services.WebAuthn.IFidoMdsMetadataSource>(
    _ => new RadioPad.Application.Services.WebAuthn.EmbeddedFidoMdsMetadataSource());

// Iter-31 SEC-011 — anomaly detection now runs as the recurring Hangfire job
// RadioPad.Api.Jobs.AnomalyScanJob (registered above; cron * * * * *), scanning the
// audit log for burst patterns and writing AnomalyDetected/SecurityAlert rows.

// PRD §14.15 CR-007 — critical-result escalation now runs as the recurring Hangfire
// job RadioPad.Api.Jobs.CriticalResultEscalationJob (registered above; cron * * * * *).
// Flags for a human; never closes the loop itself.

// PRD MOB-007 — mobile push senders (APNs / FCM). Both adapters are registered
// even when env vars are missing; the controller surfaces a 503 with
// `kind=push_not_configured` for runtime mis-configuration so we never crash
// at startup.
builder.Services.AddSingleton<RadioPad.Application.Services.Push.IPushSender, RadioPad.Application.Services.Push.ApnsSender>();
builder.Services.AddSingleton<RadioPad.Application.Services.Push.IPushSender, RadioPad.Application.Services.Push.FcmSender>();
builder.Services.AddSingleton<RadioPad.Application.Services.Push.PushSenderRegistry>();

// Iter-32 INT-010 — SIEM pushers (Splunk HEC / Sentinel Log Analytics /
// Elastic _bulk / Syslog UDP). Each sink is opt-in via env vars; the
// background worker starts whether or not a sink is configured (no-op when
// none are). Failures retry 3× with backoff and never block /api/* paths.
builder.Services.AddHttpClient("siem", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.IUdpSender,
    RadioPad.Application.Services.Siem.DefaultUdpSender>();
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.SiemStatusRegistry>();
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.ISiemSink>(sp =>
    new RadioPad.Application.Services.Siem.SplunkHecSink(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("siem")));
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.ISiemSink>(sp =>
    new RadioPad.Application.Services.Siem.SentinelLogAnalyticsSink(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("siem")));
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.ISiemSink>(sp =>
    new RadioPad.Application.Services.Siem.ElasticBulkSink(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("siem")));
builder.Services.AddSingleton<RadioPad.Application.Services.Siem.ISiemSink>(sp =>
    new RadioPad.Application.Services.Siem.SyslogUdpSink(
        sp.GetRequiredService<RadioPad.Application.Services.Siem.IUdpSender>()));
// Kept as a BackgroundService (NOT migrated to Hangfire): a 5s near-real-time flush
// loop with a stateful cursor + in-loop backoff — sub-minute and not cron-shaped.
builder.Services.AddHostedService<RadioPad.Api.Services.SiemPushService>();

// Iter-33 PERF-004 — OpenTelemetry metrics for the continuous P95 SLO
// budgets. The PerfBudgets meter is always registered so tests using a
// MeterListener can observe values; the OTLP exporter is wired only when
// the operator has set RADIOPAD_OTEL_OTLP_ENDPOINT (default empty → no
// network calls; metrics live only in-process).
{
    var otlpEndpoint = Environment.GetEnvironmentVariable("RADIOPAD_OTEL_OTLP_ENDPOINT");
    var otelBuilder = builder.Services.AddOpenTelemetry()
        .WithMetrics(m =>
        {
            m.AddMeter(RadioPad.Api.Services.PerfBudgets.MeterName);
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                m.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpEndpoint);
                    o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });
            }
        });
}

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    // Tauri serves the desktop webview from tauri://localhost on macOS/Linux but
    // from http(s)://tauri.localhost on Windows — the latter must be allow-listed
    // or the bundled desktop shell's fetches are blocked by CORS ("Failed to fetch").
    // Capacitor serves the mobile companion webview from capacitor://localhost on
    // iOS and https://localhost on Android (capacitor.config androidScheme:'https')
    // — BOTH must be listed or the phone's cross-origin companion calls (pair +
    // the cloud relay handshake) are blocked before the bearer is even checked.
    .WithOrigins(
        "http://localhost:3000", "http://127.0.0.1:3000",
        "tauri://localhost", "capacitor://localhost",
        "https://localhost", "http://localhost",
        "http://tauri.localhost", "https://tauri.localhost")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Rate limiting — protect AI endpoints from runaway loops while keeping local
// dev productive. Per-tenant fixed window: 60 AI calls/minute.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.AddPolicy("ai", context =>
    {
        var tenant = RadioPadRequestIdentity.TenantSlugOrDevHeader(context);
        if (string.IsNullOrEmpty(tenant)) tenant = "__no_tenant";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(tenant,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});

builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// Iter-31 SEC-010 — wrap every registered ILoggerProvider with the PHI
// redactor so PHI-shaped substrings are scrubbed before they reach the
// console / file / SIEM sink. We intercept at the DI layer (after all
// `builder.Logging.Add*` calls have registered their providers) by
// replacing each ILoggerProvider descriptor with a redacting decorator.
{
    var providerDescriptors = builder.Services
        .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Logging.ILoggerProvider))
        .ToList();
    foreach (var d in providerDescriptors) builder.Services.Remove(d);
    foreach (var d in providerDescriptors)
    {
        if (d.ImplementationFactory is { } factory)
        {
            builder.Services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(
                typeof(Microsoft.Extensions.Logging.ILoggerProvider),
                sp => new RadioPad.Api.Logging.PhiRedactingLoggerProvider((Microsoft.Extensions.Logging.ILoggerProvider)factory(sp)),
                d.Lifetime));
        }
        else if (d.ImplementationInstance is Microsoft.Extensions.Logging.ILoggerProvider inst)
        {
            builder.Services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(
                typeof(Microsoft.Extensions.Logging.ILoggerProvider),
                new RadioPad.Api.Logging.PhiRedactingLoggerProvider(inst)));
        }
        else if (d.ImplementationType is { } implType)
        {
            builder.Services.Add(new Microsoft.Extensions.DependencyInjection.ServiceDescriptor(
                typeof(Microsoft.Extensions.Logging.ILoggerProvider),
                sp => new RadioPad.Api.Logging.PhiRedactingLoggerProvider(
                    (Microsoft.Extensions.Logging.ILoggerProvider)Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, implType)),
                d.Lifetime));
        }
    }
}

var app = builder.Build();

if (app.Environment.IsProduction() &&
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUTHORITY")) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE")))
{
    throw new InvalidOperationException("Production OIDC requires RADIOPAD_OIDC_AUDIENCE when RADIOPAD_OIDC_AUTHORITY is configured.");
}

// UBAG misconfiguration surfaced loudly (audit fix 2026-07-18): with the base URL
// unset the client silently targets the hardcoded public host, and with no auth
// secret it silently sends UNAUTHENTICATED requests — both previously discoverable
// only as opaque provider failures. Log ERROR (not throw): UBAG is one provider
// among several and must not take the whole API down.
if (app.Environment.IsProduction())
{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RadioPad.Startup");
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_UBAG_BASE_URL")))
        startupLog.LogError(
            "RADIOPAD_UBAG_BASE_URL is not set — the UBAG client will target the public default host. " +
            "Production must point at the internal gateway (see deploy-guide 'UBAG production wiring').");
    var ubagSecretRef = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SECRET_REF");
    var ubagSecretResolved =
        (ubagSecretRef?.StartsWith("env:", StringComparison.OrdinalIgnoreCase) == true
            ? Environment.GetEnvironmentVariable(ubagSecretRef[4..])
            : ubagSecretRef)
        ?? Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SECRET");
    if (string.IsNullOrWhiteSpace(ubagSecretResolved))
        startupLog.LogError(
            "No UBAG auth secret resolves (RADIOPAD_UBAG_AUTH_SECRET_REF / RADIOPAD_UBAG_AUTH_SECRET) — " +
            "gateway calls will be sent UNAUTHENTICATED and rejected. Set the secret ref to the gateway app secret.");
}

// Resolve the encryptor before EF builds its model for migrations/seeding.
// This makes the production KMS contract fail closed during startup rather
// than silently letting EF converters initialize the non-production fallback.
_ = app.Services.GetRequiredService<RadioPad.Application.Abstractions.IColumnEncryptor>();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
    // PRD §13.1 \u2014 apply pending migrations before serving traffic. The
    // initial migration (InitialCreate) materialises every entity in the model
    // including auth flows (MagicLinkToken, DeviceAuthRequest), retention,
    // CMK and SCIM additions.
    await db.Database.MigrateAsync();
    // PR-N1 — (re)register the recurring Hangfire crons now that storage exists.
    // This whole block is already gated on !IsEnvironment("Testing"), which is the
    // same gate as AddRadioPadHangfire, so Hangfire's JobStorage is guaranteed
    // registered here. AddOrUpdate is idempotent (upsert by id), and every job body
    // is safe to run twice — so this is how InMemory storage recovers its schedules
    // after a restart.
    app.UseRadioPadRecurringJobs();
    // The enterprise-identity tables (GlobalUsers / ExternalIdentities /
    // TenantMemberships / AuthSessions) are not materialised by an EF migration;
    // EnsureSchemaAsync creates them on SQLite (no-op on Postgres). This must run
    // before seeding or serving traffic, since both DevSeed and the runtime
    // sign-in path (RecordAuthSessionAsync) query these tables.
    await EnterpriseIdentityBridge.EnsureSchemaAsync(db, default);
    if (app.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("RADIOPAD_DEV_SEED") == "1")
    {
        // Prefer the app-relative bundle layout (the Tauri desktop ships rulebooks/
        // alongside radiopad-api.exe under C:\Program Files\RadioPad\); fall back to
        // the repo-relative path used by `dotnet run`. The old 7-levels-up-only path
        // never resolved on the desktop, so the bundled starter rulebooks were never
        // seeded (Rulebooks/Prompt-studio showed empty).
        var rulebooksDir = Path.Combine(AppContext.BaseDirectory, "rulebooks");
        if (!Directory.Exists(rulebooksDir))
        {
            rulebooksDir = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "rulebooks"));
        }
        // Same resolution as rulebooks: prefer the app-relative bundle layout the
        // Tauri desktop ships (templates/ next to radiopad-api.exe), fall back to
        // the repo-relative path used by `dotnet run`. Without this the bundled
        // report templates were never seeded (Templates page empty; the editor's
        // "apply scaffolding" dropdown offered only "— none —").
        var templatesDir = Path.Combine(AppContext.BaseDirectory, "templates");
        if (!Directory.Exists(templatesDir))
        {
            templatesDir = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "templates"));
        }
        await DevSeed.EnsureSeededAsync(db, rulebooksDir, templatesDir, default);
    }

    // Production-safe library backfill: seed the curated rulebook + template library
    // and the admin Modality/BodyPart catalogs into EVERY existing tenant,
    // idempotently. The dev block above only seeds the dev tenant (and only when the
    // dev-seed gate is on); this covers REAL prod tenants, so the shipped clinical
    // content (bundled into the Docker image alongside the binary) actually reaches
    // them. Dirs resolved the same way — app-relative bundle first, repo-relative
    // fallback.
    {
        var rbDir = Path.Combine(AppContext.BaseDirectory, "rulebooks");
        if (!Directory.Exists(rbDir))
            rbDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "rulebooks"));
        var tplDir = Path.Combine(AppContext.BaseDirectory, "templates");
        if (!Directory.Exists(tplDir))
            tplDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "templates"));
        await DevSeed.EnsureBundledContentForAllTenantsAsync(db, rbDir, tplDir, default);
    }
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 2,
    };
    forwardedHeaders.KnownNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);
}

app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
// Iter-33 PERF-004 — record per-route request duration. Sits after
// correlation/exception so failed requests still produce a histogram sample.
app.UseMiddleware<PerfBudgetMiddleware>();
// Iter-32 SEC-008 — global per-IP + per-tenant fixed-window rate limiter.
// Sits before auth so brute-force attempts are rate-limited too.
app.UseMiddleware<RateLimitMiddleware>();
// CORS must run BEFORE the bearer/OIDC auth middlewares: a cross-origin
// preflight (OPTIONS) to a protected endpoint carries no credentials, so if auth
// runs first it rejects the preflight with 401 and the browser/webview blocks the
// real request. The desktop webview (tauri.localhost) is a cross-origin client,
// so without this every authenticated GET after login fails its preflight and the
// app bounces back to the login screen. The web app is same-origin (no preflight)
// and was unaffected. Default policy allow-lists the tauri/capacitor origins.
app.UseCors();
// Desktop↔phone companion relay (raw WebSocket, PRD companion subsystem).
// UseWebSockets must precede the endpoint that accepts the upgrade. The relay
// authenticates the RadioPad bearer from the `access_token` query param itself
// (browsers/webviews cannot set WS headers), so it is mapped here as an isolated
// terminal branch AHEAD of the /api bearer middlewares — which only guard /api
// paths — rather than relying on them.
app.UseWebSockets();
app.Map("/ws/companion", static branch =>
    branch.Run(RadioPad.Api.Services.CompanionRelayEndpoint.HandleAsync));
app.UseMiddleware<OidcBearerMiddleware>();
app.UseMiddleware<RadioPadBearerMiddleware>();
// Runs after identity projection so per-tenant allowlists are evaluated
// against the validated bearer/cookie/OIDC tenant, while public magic-link
// requests still resolve the tenant from the request body.
app.UseMiddleware<IpAllowlistMiddleware>();
app.UseMiddleware<SuspensionGuardMiddleware>();
app.UseRateLimiter();
if (!app.Environment.IsProduction() || Environment.GetEnvironmentVariable("RADIOPAD_ENABLE_SWAGGER") == "1")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "radiopad-api", time = DateTimeOffset.UtcNow }));
app.MapGet("/api/health/ready", async (RadioPadDbContext db, CancellationToken ct) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        return canConnect
            ? Results.Ok(new { status = "ready", db = true, time = DateTimeOffset.UtcNow })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "not_ready", db = false, error = ex.GetType().Name },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program { }
