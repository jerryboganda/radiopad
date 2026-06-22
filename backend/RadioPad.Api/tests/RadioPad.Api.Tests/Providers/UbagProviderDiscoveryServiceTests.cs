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
/// targets materialise + enable, logged-out ones disable, and the curated primaries /
/// placeholder targets are never auto-managed.
/// </summary>
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

        Assert.Equal(0, changed);
        Assert.False(await db.Providers.AnyAsync());
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
        public Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct) => throw new NotImplementedException();
    }
}
