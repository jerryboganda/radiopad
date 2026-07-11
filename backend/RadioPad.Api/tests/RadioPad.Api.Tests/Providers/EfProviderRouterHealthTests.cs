using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Repositories;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// 2026-07-11 UBAG hardening — routing-level circuit breaker. A provider whose
/// trailing window holds only failures is skipped by both the winner pick and
/// the ranked failover chain; any success (or the window sliding past) restores
/// it. "blocked" rows never count as failures.
/// </summary>
public class EfProviderRouterHealthTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        db.Tenants.Add(new Tenant { Id = TenantId, Slug = "router-health", DisplayName = "Router Health" });
        db.SaveChanges();
        return db;
    }

    // RequirePhiApprovedProvider=false so plain Sandbox test providers stay
    // eligible — these tests exercise the health filter, not the PHI gates.
    private static Tenant TestTenant() => new()
    {
        Id = TenantId,
        Slug = "router-health",
        DisplayName = "Router Health",
        RequirePhiApprovedProvider = false,
    };

    private static ProviderConfig Provider(string name, decimal quality = 0.5m) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        Name = name,
        Adapter = "mock",
        Model = name,
        Compliance = ProviderComplianceClass.Sandbox,
        Enabled = true,
        Quality = quality,
    };

    private static AiRequest Request(string provider, string status, TimeSpan age) => new()
    {
        TenantId = TenantId,
        Provider = provider,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow - age,
    };

    [Fact]
    public async Task Provider_with_failure_streak_is_excluded_from_ranked_chain()
    {
        using var db = CreateDb();
        db.Providers.AddRange(Provider("failing", quality: 0.9m), Provider("healthy", quality: 0.1m));
        for (var i = 0; i < EfProviderRouter.FailureStreakThreshold; i++)
            db.AiRequests.Add(Request("failing", "error", TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var router = new EfProviderRouter(db);
        var ranked = await router.SelectRankedAsync(TestTenant(), containsPhi: false, default);

        Assert.DoesNotContain(ranked, p => p.Name == "failing");
        Assert.Contains(ranked, p => p.Name == "healthy");

        var winner = await router.SelectAsync(TestTenant(), containsPhi: false, default);
        Assert.Equal("healthy", winner!.Name);
    }

    [Fact]
    public async Task Recent_success_clears_the_failure_streak()
    {
        using var db = CreateDb();
        db.Providers.Add(Provider("flaky"));
        for (var i = 0; i < EfProviderRouter.FailureStreakThreshold + 2; i++)
            db.AiRequests.Add(Request("flaky", "error", TimeSpan.FromMinutes(2)));
        db.AiRequests.Add(Request("flaky", "ok", TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var ranked = await new EfProviderRouter(db).SelectRankedAsync(TestTenant(), containsPhi: false, default);

        Assert.Contains(ranked, p => p.Name == "flaky");
    }

    [Fact]
    public async Task Failures_outside_the_window_do_not_count()
    {
        using var db = CreateDb();
        db.Providers.Add(Provider("recovered"));
        for (var i = 0; i < 10; i++)
            db.AiRequests.Add(Request("recovered", "error", EfProviderRouter.RecentFailureWindow + TimeSpan.FromMinutes(5)));
        await db.SaveChangesAsync();

        var ranked = await new EfProviderRouter(db).SelectRankedAsync(TestTenant(), containsPhi: false, default);

        Assert.Contains(ranked, p => p.Name == "recovered");
    }

    [Fact]
    public async Task Blocked_rows_are_not_failures()
    {
        using var db = CreateDb();
        db.Providers.Add(Provider("policy-blocked"));
        for (var i = 0; i < 10; i++)
            db.AiRequests.Add(Request("policy-blocked", "blocked", TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var ranked = await new EfProviderRouter(db).SelectRankedAsync(TestTenant(), containsPhi: false, default);

        Assert.Contains(ranked, p => p.Name == "policy-blocked");
    }

    [Fact]
    public async Task Below_threshold_failures_do_not_exclude()
    {
        using var db = CreateDb();
        db.Providers.Add(Provider("wobbly"));
        for (var i = 0; i < EfProviderRouter.FailureStreakThreshold - 1; i++)
            db.AiRequests.Add(Request("wobbly", "error", TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var ranked = await new EfProviderRouter(db).SelectRankedAsync(TestTenant(), containsPhi: false, default);

        Assert.Contains(ranked, p => p.Name == "wobbly");
    }

    [Fact]
    public async Task Ranked_chain_orders_best_first_and_matches_select()
    {
        using var db = CreateDb();
        db.Providers.AddRange(Provider("low", quality: 0.2m), Provider("high", quality: 0.9m));
        await db.SaveChangesAsync();

        var router = new EfProviderRouter(db);
        var ranked = await router.SelectRankedAsync(TestTenant(), containsPhi: false, default);
        var winner = await router.SelectAsync(TestTenant(), containsPhi: false, default);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(winner!.Name, ranked[0].Name);
    }
}
