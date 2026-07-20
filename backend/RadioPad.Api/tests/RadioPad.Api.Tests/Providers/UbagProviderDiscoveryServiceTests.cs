using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Ubag;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// Iter — dynamic UBAG provider auto-discovery. Verifies SyncAsync keeps a tenant's
/// UBAG provider rows in lock-step with the gateway login state: logged-in non-curated
/// targets materialise + enable, logged-out ones disable, and placeholder targets are
/// never auto-managed. Curated primaries are never CREATED here (DevSeed owns them),
/// but since 2026-07-11 their Enabled flag mirrors live login state, and a logout
/// transition audits + alerts the operator (throttled email + Hub banner).
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class UbagProviderDiscoveryServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        // ProviderConfig.TenantId is an enforced FK; seed the tenant first.
        db.Tenants.Add(new Tenant { Id = Tenant, Slug = "disco-test", DisplayName = "Discovery Test" });
        db.SaveChanges();
        return db;
    }

    private static UbagProviderDiscoveryService Service(RadioPadDbContext db, FakeClient client)
        => new(client, db, NullLogger<UbagProviderDiscoveryService>.Instance);

    [Fact]
    public async Task Materialises_authenticated_non_pinned_target()
    {
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("chatgpt_web", "ChatGPT Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("chatgpt_web", "authenticated") });

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        Assert.Equal(1, changed);
        var row = await db.Providers.SingleAsync(p => p.Model == "chatgpt_web");
        Assert.Equal("ubag", row.Adapter);
        Assert.True(row.Enabled);
        Assert.Equal("UBAG (ChatGPT Web)", row.Name);
    }

    [Fact]
    public async Task Skips_target_that_is_not_logged_in()
    {
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("claude_web", "Claude Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("claude_web", "unknown") });

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        Assert.Equal(0, changed);
        Assert.False(await db.Providers.AnyAsync(p => p.Model == "claude_web"));
    }

    [Fact]
    public async Task Pinned_primary_is_disabled_on_logout_and_reenabled_on_login()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG Gemini Web",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        // Logged out -> the curated primary must stop receiving traffic.
        var loggedOut = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "login_required") });
        Assert.Equal(1, await Service(db, loggedOut).SyncAsync(Tenant, default));
        Assert.False((await db.Providers.SingleAsync(p => p.Model == "gemini_web")).Enabled);

        // Logged back in -> restored automatically, no human config.
        var loggedIn = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "authenticated") });
        Assert.Equal(1, await Service(db, loggedIn).SyncAsync(Tenant, default));
        Assert.True((await db.Providers.SingleAsync(p => p.Model == "gemini_web")).Enabled);
    }

    [Fact]
    public async Task Pinned_primary_stays_enabled_when_gateway_gives_no_login_signal()
    {
        // Regression (2026-07-18, prod): the vps-local executor runs jobs against live
        // browser profiles WITHOUT ever registering /v1/browser/contexts rows — the real
        // /v1/targets shape carries no readiness either (Ready=null). The old code read
        // that absence as logged-out and disabled a perfectly working curated primary.
        // No signal must leave the operator's Enabled choice untouched.
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG Gemini Web",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var noSignal = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", null, null) },
            contexts: Array.Empty<UbagBrowserContext>());

        Assert.Equal(0, await Service(db, noSignal).SyncAsync(Tenant, default));
        Assert.True((await db.Providers.SingleAsync(p => p.Model == "gemini_web")).Enabled);
    }

    [Fact]
    public async Task Pinned_primary_stays_disabled_when_gateway_gives_no_login_signal()
    {
        // Symmetric to the enabled case: an operator-disabled row must not be silently
        // re-enabled just because the gateway is silent about login state.
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG DeepSeek Web",
            Adapter = "ubag",
            Model = "deepseek_web",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = false,
        });
        await db.SaveChangesAsync();

        var noSignal = new FakeClient(
            targets: new[] { new UbagTarget("deepseek_web", "DeepSeek Web", "listed", null, null) },
            contexts: Array.Empty<UbagBrowserContext>());

        Assert.Equal(0, await Service(db, noSignal).SyncAsync(Tenant, default));
        Assert.False((await db.Providers.SingleAsync(p => p.Model == "deepseek_web")).Enabled);
    }

    [Fact]
    public async Task No_signal_allowed_target_is_materialised_enabled()
    {
        // Operator report 2026-07-19: chatgpt_web is an ALLOWED catalog target but the
        // vps-local gateway reports no readiness (Ready=null) and no browser context, so
        // it never surfaced in the picker. Auto-sync must materialise every allowed
        // target the gateway lists even with no login signal, defaulting Enabled=ON
        // (the failure-based alert path covers a dead session), so all UBAG web models
        // appear automatically.
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("chatgpt_web", "ChatGPT Web", "listed", null, null) },
            contexts: Array.Empty<UbagBrowserContext>());

        Assert.Equal(1, await Service(db, client).SyncAsync(Tenant, default));
        var row = await db.Providers.SingleAsync(p => p.Model == "chatgpt_web");
        Assert.Equal("UBAG (ChatGPT Web)", row.Name);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task Explicitly_logged_out_allowed_target_is_materialised_disabled()
    {
        // The one case that still starts a freshly discovered row OFF: the gateway gave
        // an EXPLICIT logged-out signal. The model is still visible (so the operator
        // knows it exists) but not selectable until they log in.
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("chatgpt_web", "ChatGPT Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("chatgpt_web", "login_required") });

        Assert.Equal(1, await Service(db, client).SyncAsync(Tenant, default));
        var row = await db.Providers.SingleAsync(p => p.Model == "chatgpt_web");
        Assert.False(row.Enabled);
    }

    [Fact]
    public async Task Zero_row_tenant_is_healed_with_curated_primaries()
    {
        // Heal-on-empty (2026-07-18): a tenant with NO ubag rows (creation-time
        // seeding failed) gets the curated primaries from the sweep/refresh path
        // instead of being invisible to discovery forever. The healed rows come
        // from UbagPrimarySeed (not the reconcile branch), so `changed` stays 0.
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "authenticated") });

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        Assert.Equal(0, changed);
        var rows = await db.Providers.Where(p => p.Adapter == "ubag").OrderBy(p => p.Priority).ToListAsync();
        Assert.Equal(2, rows.Count);
        // Pin the router-preference numbers (audit fix 2026-07-18: previously unpinned):
        // Gemini P1/Q0.90 outranks DeepSeek P2/Q0.85 in the quality-weighted router.
        Assert.Equal("gemini_web", rows[0].Model);
        Assert.Equal(1, rows[0].Priority);
        Assert.Equal(0.9m, rows[0].Quality);
        Assert.Equal("deepseek_web", rows[1].Model);
        Assert.Equal(2, rows[1].Priority);
        Assert.Equal(0.85m, rows[1].Quality);
        Assert.All(rows, r => Assert.True(r.Enabled));
        Assert.All(rows, r => Assert.Equal(ProviderComplianceClass.PhiApproved, r.Compliance));
    }

    [Fact]
    public async Task Logout_transition_audits_and_notifies_operator()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG Gemini Web",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var email = new RecordingEmailSender();
        var alerts = new UbagOperatorAlertService(email, NullLogger<UbagOperatorAlertService>.Instance);
        var audit = new RecordingAudit();
        var client = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "login_required") });
        var service = new UbagProviderDiscoveryService(
            client, db, NullLogger<UbagProviderDiscoveryService>.Instance, alerts, audit);

        Environment.SetEnvironmentVariable(UbagOperatorAlertService.OperatorEmailEnvVar, "ops@example.test");
        try
        {
            await service.SyncAsync(Tenant, default);

            var evt = Assert.Single(audit.Events);
            Assert.Equal(AuditAction.SystemAlert, evt.Action);
            Assert.Contains("ubag_login_lost", evt.DetailsJson);
            Assert.Contains("gemini_web", evt.DetailsJson);

            var sent = Assert.Single(email.Sent);
            Assert.Equal("ops@example.test", sent.To);
            Assert.Contains("gemini_web", sent.Subject);

            Assert.True(alerts.LoggedOutTargets.ContainsKey("gemini_web"));

            // Second sweep while still logged out: banner persists, email throttled.
            await service.SyncAsync(Tenant, default);
            Assert.Single(email.Sent);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UbagOperatorAlertService.OperatorEmailEnvVar, null);
        }
    }

    [Fact]
    public async Task Failure_streak_on_enabled_provider_alerts_operator_and_clears_on_recovery()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG Gemini Web",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        for (var i = 0; i < 3; i++)
            db.AiRequests.Add(new AiRequest
            {
                TenantId = Tenant,
                Provider = "UBAG Gemini Web",
                Status = "error",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
        await db.SaveChangesAsync();

        var email = new RecordingEmailSender();
        var alerts = new UbagOperatorAlertService(email, NullLogger<UbagOperatorAlertService>.Instance);
        // Gateway reports the session authenticated — the point of this signal
        // is that failing REAL traffic alerts even when login-state is fiction.
        var client = new FakeClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "authenticated") });
        var service = new UbagProviderDiscoveryService(
            client, db, NullLogger<UbagProviderDiscoveryService>.Instance, alerts);

        Environment.SetEnvironmentVariable(UbagOperatorAlertService.OperatorEmailEnvVar, "ops@example.test");
        try
        {
            await service.SyncAsync(Tenant, default);

            Assert.True(alerts.FailingTargets.ContainsKey("gemini_web"));
            var sent = Assert.Single(email.Sent);
            Assert.Contains("failing", sent.Subject, StringComparison.OrdinalIgnoreCase);

            // A success within the window clears the streak and the banner.
            db.AiRequests.Add(new AiRequest
            {
                TenantId = Tenant,
                Provider = "UBAG Gemini Web",
                Status = "ok",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await service.SyncAsync(Tenant, default);
            Assert.False(alerts.FailingTargets.ContainsKey("gemini_web"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UbagOperatorAlertService.OperatorEmailEnvVar, null);
        }
    }

    [Fact]
    public async Task Gateway_unreachable_is_tracked_and_cleared()
    {
        using var db = CreateDb();
        var email = new RecordingEmailSender();
        var alerts = new UbagOperatorAlertService(email, NullLogger<UbagOperatorAlertService>.Instance);

        var down = new FakeClient(
            targets: Array.Empty<UbagTarget>(),
            contexts: Array.Empty<UbagBrowserContext>(),
            health: new UbagHealth(false, "down", null, "unreachable"));
        await new UbagProviderDiscoveryService(down, db, NullLogger<UbagProviderDiscoveryService>.Instance, alerts)
            .SyncAsync(Tenant, default);
        Assert.NotNull(alerts.GatewayUnreachableSince);

        var up = new FakeClient(
            targets: Array.Empty<UbagTarget>(),
            contexts: Array.Empty<UbagBrowserContext>());
        await new UbagProviderDiscoveryService(up, db, NullLogger<UbagProviderDiscoveryService>.Instance, alerts)
            .SyncAsync(Tenant, default);
        Assert.Null(alerts.GatewayUnreachableSince);
    }

    [Fact]
    public async Task Never_touches_pinned_primaries_or_placeholder_targets()
    {
        using var db = CreateDb();
        var client = new FakeClient(
            targets: new[]
            {
                new UbagTarget("gemini_web", "Gemini Web", "ready", true, null),
                new UbagTarget("deepseek_web", "DeepSeek Web", "ready", true, null),
                new UbagTarget("mock", "Mock", "ready", true, null),
                new UbagTarget("generic_chat", "Generic Chat", "ready", true, null),
            },
            contexts: new[]
            {
                new UbagBrowserContext("gemini_web", "authenticated"),
                new UbagBrowserContext("deepseek_web", "authenticated"),
                new UbagBrowserContext("mock", "authenticated"),
                new UbagBrowserContext("generic_chat", "authenticated"),
            });

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        // The zero-row tenant is first healed with the two curated primaries
        // (2026-07-18); the reconcile branch itself must still create nothing for
        // pinned targets and never materialise placeholder targets (mock /
        // generic_chat are Excluded) — so exactly the two healed rows exist.
        Assert.Equal(0, changed);
        var models = await db.Providers.Where(p => p.Adapter == "ubag").Select(p => p.Model).ToListAsync();
        Assert.Equal(2, models.Count);
        Assert.Contains("gemini_web", models);
        Assert.Contains("deepseek_web", models);
        Assert.DoesNotContain("mock", models);
        Assert.DoesNotContain("generic_chat", models);
    }

    [Fact]
    public async Task Disables_auto_row_when_provider_logs_out()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG (ChatGPT Web)",
            Adapter = "ubag",
            Model = "chatgpt_web",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var client = new FakeClient(
            targets: new[] { new UbagTarget("chatgpt_web", "ChatGPT Web", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("chatgpt_web", "login_required") });

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        Assert.Equal(1, changed);
        var row = await db.Providers.SingleAsync(p => p.Model == "chatgpt_web");
        Assert.False(row.Enabled);
    }

    [Fact]
    public async Task No_op_when_gateway_unhealthy()
    {
        using var db = CreateDb();
        var client = new FakeClient(
            targets: Array.Empty<UbagTarget>(),
            contexts: Array.Empty<UbagBrowserContext>(),
            health: new UbagHealth(false, "down", null, "unreachable"));

        var changed = await Service(db, client).SyncAsync(Tenant, default);

        Assert.Equal(0, changed);
        Assert.False(await db.Providers.AnyAsync());
    }

    private sealed class FakeClient : IUbagClient
    {
        private readonly UbagHealth _health;
        private readonly IReadOnlyList<UbagTarget> _targets;
        private readonly IReadOnlyList<UbagBrowserContext> _contexts;

        public FakeClient(
            IReadOnlyList<UbagTarget> targets,
            IReadOnlyList<UbagBrowserContext> contexts,
            UbagHealth? health = null)
        {
            _targets = targets;
            _contexts = contexts;
            _health = health ?? new UbagHealth(true, "ok", "2026-05-22", null);
        }

        public Task<UbagHealth> GetHealthAsync(CancellationToken ct) => Task.FromResult(_health);
        public Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct) =>
            Task.FromResult(new UbagBrowserSummary(1, 1, 1, "ready", "{}"));
        public Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct) => Task.FromResult(_targets);
        public Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct) => Task.FromResult(_contexts);
        public Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct) => throw new NotImplementedException();
        public Task CancelJobAsync(string jobId, CancellationToken ct) => Task.CompletedTask;
        public Task<UbagJob> CreateTranscriptionJobAsync(UbagTranscriptionRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagArtifact> UploadJobArtifactAsync(string jobId, string key, Stream content, string contentType, long contentLength, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken) { Events.Add(evt); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditChainVerification(Events.Count, true, null, null));
    }
}
