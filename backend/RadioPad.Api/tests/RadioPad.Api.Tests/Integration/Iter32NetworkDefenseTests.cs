using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Middleware;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 SEC-008 / SEC-011 — network defenses.
/// Covers CIDR matching (IPv4 + IPv6) in <see cref="IpAllowlistMiddleware"/>,
/// the <see cref="RateLimitMiddleware"/> 429 path, and the
/// <see cref="AnomalyDetector"/>'s new <see cref="AuditAction.SecurityAlert"/>
/// emitters.
/// </summary>
public class Iter32NetworkDefenseTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32NetworkDefenseTests(RadioPadAppFactory f) => _factory = f;

    // ---------- IpAllowlistMiddleware ----------

    [Fact]
    public async Task IpAllowlist_AllowsIpv4InsideJsonCidr()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var s = await EnsureSettings(db);
        s.IpAllowlistJson = "[\"10.0.0.0/8\"]";
        s.IpAllowlistCidr = "";
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpAllowlistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.42.0.7");
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;

        await middleware.InvokeAsync(ctx, db, audit);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task IpAllowlist_BlocksIpv6OutsideJsonCidr()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var s = await EnsureSettings(db);
        s.IpAllowlistJson = "[\"2001:db8::/32\"]";
        s.IpAllowlistCidr = "";
        await db.SaveChangesAsync();

        var middleware = new IpAllowlistMiddleware(_ => Task.CompletedTask, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("2400:cb00::1");
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx, db, audit);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task IpAllowlist_AllowsIpv6InsideJsonCidr()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var s = await EnsureSettings(db);
        s.IpAllowlistJson = "[\"2001:db8::/32\"]";
        s.IpAllowlistCidr = "";
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpAllowlistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:0:1::42");
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;

        await middleware.InvokeAsync(ctx, db, audit);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task IpAllowlist_LoopbackAlwaysAllowed_EvenWhenAllowlistMismatches()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var s = await EnsureSettings(db);
        s.IpAllowlistJson = "[\"10.0.0.0/8\"]";
        s.IpAllowlistCidr = "";
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpAllowlistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;
        await middleware.InvokeAsync(ctx, db, audit);
        Assert.True(nextCalled);

        nextCalled = false;
        ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;
        await middleware.InvokeAsync(ctx, db, audit);
        Assert.True(nextCalled);
    }

    [Fact]
    public void IpAllowlist_XForwardedFor_IgnoredByDefault()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR", null);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        var resolved = IpAllowlistMiddleware.ResolveRemoteIp(ctx);
        Assert.Equal(IPAddress.Parse("10.0.0.5"), resolved);
    }

    [Fact]
    public void IpAllowlist_XForwardedFor_HonouredWhenTrusted()
    {
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR", "1");
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.5";

            var resolved = IpAllowlistMiddleware.ResolveRemoteIp(ctx);
            Assert.Equal(IPAddress.Parse("203.0.113.7"), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR", null);
        }
    }

    // ---------- RateLimitMiddleware ----------

    [Fact]
    public async Task RateLimit_HealthBypassed()
    {
        var middleware = new RateLimitMiddleware(_ => Task.CompletedTask, NullLogger<RateLimitMiddleware>.Instance);
        try
        {
            for (int i = 0; i < 200; i++)
            {
                var ctx = new DefaultHttpContext();
                ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                ctx.Request.Path = "/api/health";
                await middleware.InvokeAsync(ctx);
                Assert.NotEqual(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
            }
        }
        finally { middleware.Dispose(); }
    }

    [Fact]
    public async Task RateLimit_ReturnsRfc7807_429AfterLimit()
    {
        // Override per-IP to a very small ceiling for the test.
        Environment.SetEnvironmentVariable("RADIOPAD_RATE_LIMIT_IP_PER_MIN", "3");
        Environment.SetEnvironmentVariable("RADIOPAD_RATE_LIMIT_TENANT_PER_MIN", "1000");
        try
        {
            var middleware = new RateLimitMiddleware(_ => Task.CompletedTask, NullLogger<RateLimitMiddleware>.Instance);
            try
            {
                int? lastStatus = null;
                string? lastBody = null;
                for (int i = 0; i < 6; i++)
                {
                    var ctx = new DefaultHttpContext();
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.55");
                    ctx.Request.Path = "/api/reports";
                    ctx.Response.Body = new MemoryStream();
                    await middleware.InvokeAsync(ctx);
                    lastStatus = ctx.Response.StatusCode;
                    ctx.Response.Body.Position = 0;
                    lastBody = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
                }
                Assert.Equal(StatusCodes.Status429TooManyRequests, lastStatus);
                Assert.Contains("rate_limited", lastBody);
                Assert.Contains("retryAfterSeconds", lastBody);
            }
            finally { middleware.Dispose(); }
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_RATE_LIMIT_IP_PER_MIN", null);
            Environment.SetEnvironmentVariable("RADIOPAD_RATE_LIMIT_TENANT_PER_MIN", null);
        }
    }

    // ---------- AnomalyDetector — new SecurityAlert patterns ----------

    [Fact]
    public async Task AnomalyDetector_RaisesSecurityAlert_OnProviderBlockedPerUserBurst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        for (var i = 0; i < 55; i++)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = _factory.SeedUser.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = "{\"provider\":\"mock\"}",
            }, default);
        }

        var detector = ActivatorUtilities.CreateInstance<AnomalyDetector>(scope.ServiceProvider, new NullLogger<AnomalyDetector>());
        await detector.ScanOnceAsync(default);

        var alerts = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.SecurityAlert)
            .ToListAsync();
        Assert.Contains(alerts, a => a.DetailsJson.Contains("provider_blocked_burst_by_user"));
    }

    [Fact]
    public async Task AnomalyDetector_NoFalsePositive_OnSmallProviderBlockedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        // Use a dedicated user so noise from sibling tests (which may have
        // pushed >50 ProviderBlocked rows under SeedUser) doesn't pollute
        // this assertion. The fixture is shared across the test class.
        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"low-volume-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Low Volume",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = user.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = "{\"provider\":\"mock\"}",
            }, default);
        }

        var detector = ActivatorUtilities.CreateInstance<AnomalyDetector>(scope.ServiceProvider, new NullLogger<AnomalyDetector>());
        await detector.ScanOnceAsync(default);

        var alerts = await db.AuditEvents.AsNoTracking()
            .Where(e => e.Action == AuditAction.SecurityAlert
                     && e.DetailsJson.Contains("provider_blocked_burst_by_user")
                     && e.DetailsJson.Contains(user.Id.ToString()))
            .CountAsync();
        Assert.Equal(0, alerts);
    }

    private async Task<TenantSettings> EnsureSettings(RadioPadDbContext db)
    {
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null)
        {
            s = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(s);
            await db.SaveChangesAsync();
        }
        return s;
    }
}
