using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Controllers;
using RadioPad.Api.Tests.Integration;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 AUTH-004 — magic-link <c>request</c> endpoint must enforce the
/// chained per-email (5 / 15 min) and per-IP (20 / 15 min) fixed-window
/// rate limits. A rejected request returns <c>429</c> with a
/// <c>Retry-After</c> header and emits a
/// <see cref="AuditAction.RateLimited"/> audit row.
///
/// We isolate the static limiter state by calling
/// <c>MagicLinkRateLimiter.ResetForTesting()</c> in the test-class
/// constructor.
/// </summary>
public sealed class MagicLinkRateLimitTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public MagicLinkRateLimitTests(RadioPadAppFactory factory)
    {
        _factory = factory;
        MagicLinkRateLimiter.ResetForTesting();
    }

    [Fact]
    public async Task SixthRequest_For_SameEmail_Returns_429_With_RetryAfter()
    {
        using var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);

        var email = "it-radiologist@radiopad.local";
        for (var i = 0; i < 5; i++)
        {
            var ok = await c.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = _factory.SeedTenant.Slug,
                email,
            });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        var rejected = await c.PostAsJsonAsync("/api/auth/magic-link/request", new
        {
            tenant = _factory.SeedTenant.Slug,
            email,
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retry));
        var retrySec = int.Parse(retry.Single());
        Assert.True(retrySec > 0);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"rate-limit\"", body);
        Assert.Contains("Too many magic-link requests.", body);
    }

    [Fact]
    public async Task TwentyFirstRequest_FromSameIp_AcrossEmails_Returns_429()
    {
        // Reset to ensure no leakage from other tests in this run.
        MagicLinkRateLimiter.ResetForTesting();

        using var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);

        for (var i = 0; i < 20; i++)
        {
            var ok = await c.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = _factory.SeedTenant.Slug,
                email = $"flooder-{i}@radiopad.local", // distinct emails so per-email limit never fires.
            });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await c.PostAsJsonAsync("/api/auth/magic-link/request", new
        {
            tenant = _factory.SeedTenant.Slug,
            email = "flooder-21@radiopad.local",
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task RateLimit_Rejection_Writes_RateLimited_Audit_Row()
    {
        MagicLinkRateLimiter.ResetForTesting();

        using var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);

        var email = "audit-target@radiopad.local";
        for (var i = 0; i < 6; i++)
        {
            await c.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = _factory.SeedTenant.Slug,
                email,
            });
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var rows = await db.AuditEvents
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.RateLimited)
            .ToListAsync();
        Assert.NotEmpty(rows);
        var row = rows.Last();
        Assert.Contains("magic-link/request", row.DetailsJson);
        Assert.Contains("\"scope\":\"email\"", row.DetailsJson);
        // The raw email must NOT appear in the audit row — only its hash.
        Assert.DoesNotContain(email, row.DetailsJson);
        Assert.Contains("emailHash", row.DetailsJson);
    }
}
