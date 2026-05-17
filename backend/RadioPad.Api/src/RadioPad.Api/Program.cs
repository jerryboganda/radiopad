using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using RadioPad.Api.Auth;
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
builder.WebHost.UseUrls(bindUrl);

builder.Services.AddDbContext<RadioPadDbContext>(opt =>
{
    var conn = builder.Configuration.GetConnectionString("RadioPad")
        ?? "Data Source=radiopad.dev.db";
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

builder.Services.AddHttpClient();
// Iter-31 — shared HttpClient pool for production AI provider adapters
// (Azure OpenAI, AWS Bedrock, Vertex AI, OpenAI direct, OpenAI-compatible).
// Per-adapter timeout is 60 s; overrideable via RADIOPAD_AI_HTTP_TIMEOUT_SEC.
builder.Services.AddHttpClient("ai", c =>
{
    var sec = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_AI_HTTP_TIMEOUT_SEC"), out var t) && t > 0 ? t : 60;
    c.Timeout = TimeSpan.FromSeconds(sec);
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
// Iter-36 AI-012 — CLI-spawning adapters (default LocalOnly). The
// per-process launcher is a singleton so test code can substitute it.
builder.Services.AddSingleton<RadioPad.Infrastructure.Providers.Cli.IProcessLauncher,
    RadioPad.Infrastructure.Providers.Cli.DefaultProcessLauncher>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Cli.GitHubCopilotCliProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Cli.GeminiCliProvider>();
builder.Services.AddSingleton<IAiProviderAdapter, RadioPad.Infrastructure.Providers.Cli.CodexCliProvider>();

builder.Services.AddScoped<IAuditLog, EfAuditLog>();
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
// Enterprise Copilot broker foundation. Defaults fail closed; no SDK/CLI
// runtime is invoked until an official backend-safe transport is enabled.
builder.Services.AddScoped<RadioPad.Api.Services.CopilotService>();
// PRD §18 — advanced analytics dashboard service.
builder.Services.AddScoped<RadioPad.Application.Services.AnalyticsService>();
builder.Services.AddSingleton<RadioPad.Application.Services.HallucinationDetector>();
builder.Services.AddSingleton<RadioPad.Application.Services.MeasurementExtractionService>();
builder.Services.AddScoped<RadioPad.Application.Services.ITerminologyAdapter, RadioPad.Application.Services.NoOpTerminologyAdapter>();
// Iter-31 AI-009 / AI-001 — per-tenant prompt overrides + dictation cleanup.
builder.Services.AddScoped<IPromptOverrideStore, EfPromptOverrideStore>();
builder.Services.AddScoped<RadioPad.Application.Abstractions.IDictationCleanupService,
    RadioPad.Application.Services.DictationCleanupService>();
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

builder.Services.AddHostedService<RadioPad.Api.Services.RetentionWorker>();

// PRD §18.2 — model drift detection background service. Periodically runs
// golden-case regression against sandbox AI providers and raises SystemAlert
// audit events when quality degrades beyond the configured threshold.
builder.Services.AddSingleton<RadioPad.Api.Services.ModelDriftDetectionService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RadioPad.Api.Services.ModelDriftDetectionService>());

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
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RadioPad.Api.Services.AvailabilityMonitorService>());
// Iter-31 INT-006 — HL7 v2 MLLP listener. No-op when RADIOPAD_HL7_MLLP_PORT
// is not set; binds 127.0.0.1 by default (override with RADIOPAD_HL7_MLLP_BIND).
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
builder.Services.AddSingleton<RadioPad.Api.Services.OAuthRefreshRotationService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<RadioPad.Api.Services.OAuthRefreshRotationService>());

// Iter-31 SEC-002 — at-rest column-level encryption. The data key is
// resolved once at startup from RADIOPAD_COLUMN_KEY_REF (KMS reference) +
// RADIOPAD_COLUMN_KEY_WRAPPED (b64 wrapped DEK). When unset (dev/test) the
// encryptor falls back to a deterministic key so EnsureCreated suites work
// without operator setup.
builder.Services.AddSingleton<RadioPad.Application.Abstractions.IColumnEncryptor>(sp =>
{
    var resolver = sp.GetService<RadioPad.Application.Services.Kms.IKmsResolver>();
    var keyRef = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_REF");
    var wrapped = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_WRAPPED");
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

// Iter-31 SEC-011 — anomaly detector background service. Scans the audit
// log every 5 minutes for known burst patterns and writes
// AnomalyDetected rows + structured-log warnings.
builder.Services.AddHostedService<RadioPad.Api.Services.AnomalyDetector>();

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
    .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000", "tauri://localhost", "capacitor://localhost")
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

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
    // PRD §13.1 \u2014 apply pending migrations before serving traffic. The
    // initial migration (InitialCreate) materialises every entity in the model
    // including auth flows (MagicLinkToken, DeviceAuthRequest), retention,
    // CMK and SCIM additions.
    await db.Database.MigrateAsync();
    await EnterpriseIdentityBridge.EnsureSchemaAsync(db, default);
    var rulebooksDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "rulebooks");
    rulebooksDir = Path.GetFullPath(rulebooksDir);
    await DevSeed.EnsureSeededAsync(db, rulebooksDir, default);
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
// Enforce the operator-wide allowlist before auth middleware does JWT crypto
// or tenant/user database lookups. Tenant-specific allowlists run after auth.
app.UseMiddleware<GlobalIpAllowlistMiddleware>();
app.UseMiddleware<OidcBearerMiddleware>();
app.UseMiddleware<RadioPadBearerIdentityMiddleware>();
app.UseMiddleware<IpAllowlistMiddleware>();
// Iter-32 SEC-008 — global per-IP + per-tenant fixed-window rate limiter.
// Sits after identity projection so tenant partitions use verified context
// rather than trusting raw client-supplied tenant headers.
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<SuspensionGuardMiddleware>();
app.UseCors();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()
    || app.Environment.IsEnvironment("Testing")
    || Environment.GetEnvironmentVariable("RADIOPAD_SWAGGER_ENABLED") == "1")
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
