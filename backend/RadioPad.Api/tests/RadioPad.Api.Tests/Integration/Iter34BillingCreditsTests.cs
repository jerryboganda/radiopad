using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-34 PRD BILL-002 / BILL-007 — covers the new
/// <c>GET /api/billing/credits</c> endpoint that surfaces month-to-date
/// AI credit balance + the trial countdown for the admin UI. Verifies
/// (a) used/limits/remaining are computed by reusing
/// <c>PlanQuotaService</c>, and (b) <c>trialEndsAt</c> is surfaced when
/// the tenant is on the Trial plan.
/// </summary>
public class Iter34BillingCreditsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public Iter34BillingCreditsTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Credits_ReturnsComputedUsageLimitsRemaining_OnTeamPlan()
    {
        // Seed a fresh tenant so we don't perturb the shared seed tenant.
        var tenantId = Guid.NewGuid();
        var slug = $"credits-{tenantId:N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Slug = slug, DisplayName = "Credits" });
            db.Users.Add(new User
            {
                TenantId = tenantId,
                Email = $"user-{tenantId:N}@radiopad.local",
                DisplayName = "U",
                Role = UserRole.Radiologist,
            });
            db.TenantSettings.Add(new TenantSettings { TenantId = tenantId, Plan = TenantPlan.Team });

            // 3 successful AI calls month-to-date with token totals.
            var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 3; i++)
            {
                db.AiRequests.Add(new AiRequest
                {
                    TenantId = tenantId,
                    UserId = Guid.Empty,
                    Provider = "mock",
                    Model = "mock",
                    Mode = "draft",
                    Status = "ok",
                    CreatedAt = monthStart.AddMinutes(i),
                    InputHash = "x",
                    OutputHash = "y",
                    InputTokens = 100,
                    OutputTokens = 50,
                });
            }
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", $"user-{tenantId:N}@radiopad.local");

        var resp = await client.GetAsync("/api/billing/credits");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("Team", root.GetProperty("plan").GetString());
        Assert.True(root.TryGetProperty("periodStart", out _));
        Assert.True(root.TryGetProperty("periodEnd", out _));

        var used = root.GetProperty("used");
        Assert.Equal(3, used.GetProperty("calls").GetInt32());
        Assert.Equal(300, used.GetProperty("inputTokens").GetInt64());
        Assert.Equal(150, used.GetProperty("outputTokens").GetInt64());

        var limits = root.GetProperty("limits");
        // Team plan limits — see PlanLimits.For(TenantPlan.Team).
        Assert.Equal(10_000, limits.GetProperty("calls").GetInt32());
        Assert.Equal(5_000_000, limits.GetProperty("inputTokens").GetInt32());
        Assert.Equal(2_000_000, limits.GetProperty("outputTokens").GetInt32());

        var remaining = root.GetProperty("remaining");
        Assert.Equal(10_000 - 3, remaining.GetProperty("calls").GetInt64());
        Assert.Equal(5_000_000 - 300, remaining.GetProperty("inputTokens").GetInt64());
        Assert.Equal(2_000_000 - 150, remaining.GetProperty("outputTokens").GetInt64());

        // Team-plan tenant has no trial marker.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("trialEndsAt").ValueKind);
    }

    [Fact]
    public async Task Credits_SurfacesTrialEndsAt_OnTrialPlan()
    {
        var tenantId = Guid.NewGuid();
        var slug = $"trial-{tenantId:N}";
        var trialEnd = DateTimeOffset.UtcNow.AddDays(7);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Slug = slug, DisplayName = "Trial" });
            db.Users.Add(new User
            {
                TenantId = tenantId,
                Email = $"user-{tenantId:N}@radiopad.local",
                DisplayName = "U",
                Role = UserRole.Radiologist,
            });
            db.TenantSettings.Add(new TenantSettings
            {
                TenantId = tenantId,
                Plan = TenantPlan.Trial,
                TrialEndsAt = trialEnd,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", $"user-{tenantId:N}@radiopad.local");

        var resp = await client.GetAsync("/api/billing/credits");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("Trial", root.GetProperty("plan").GetString());
        var surfaced = root.GetProperty("trialEndsAt").GetDateTimeOffset();
        // Allow ms drift round-trip via JSON.
        Assert.True(Math.Abs((surfaced - trialEnd).TotalSeconds) < 2);

        // Trial limits — see PlanLimits.For(TenantPlan.Trial).
        Assert.Equal(100, root.GetProperty("limits").GetProperty("calls").GetInt32());
    }
}
