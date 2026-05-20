using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Auth;
using RadioPad.Api.Middleware;
using RadioPad.Api.Services;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-31 SEC-008/010/011 — security hardening tests.
/// Covers PHI log redaction, anomaly detector burst alerting, and the
/// per-tenant IP allowlist middleware blocking + audit row.
/// </summary>
[Collection(EnvironmentVariableCollection.Name)]
public class Iter31SecurityTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter31SecurityTests(RadioPadAppFactory f) => _factory = f;

    // ----- SEC-010: PHI redactor regex cases -----

    [Fact]
    public void PhiRedactor_ScrubsMrnLikeDigits()
    {
        var s = PhiRedactor.Redact("Order MRN 12345678 received from EMR");
        Assert.DoesNotContain("12345678", s);
        Assert.Contains("<redacted:phi>", s);
    }

    [Fact]
    public void PhiRedactor_ScrubsSlashDob()
    {
        var s = PhiRedactor.Redact("DOB 03/14/1969 normal study");
        Assert.DoesNotContain("03/14/1969", s);
        Assert.Contains("<redacted:phi>", s);
    }

    [Fact]
    public void PhiRedactor_ScrubsIsoDate()
    {
        var s = PhiRedactor.Redact("encounter on 1969-03-14 was uneventful");
        Assert.DoesNotContain("1969-03-14", s);
    }

    [Fact]
    public void PhiRedactor_ScrubsSsnAndPatientName()
    {
        var s = PhiRedactor.Redact("Patient: Jane Doe SSN 123-45-6789");
        Assert.DoesNotContain("Jane Doe", s);
        Assert.DoesNotContain("123-45-6789", s);
    }

    [Fact]
    public void PhiRedactor_LeavesNonPhiAlone()
    {
        var s = PhiRedactor.Redact("ai_request blocked by policy: provider=anthropic");
        Assert.Equal("ai_request blocked by policy: provider=anthropic", s);
    }

    // ----- SEC-011: Anomaly detector synthetic burst -----

    [Fact]
    public async Task AnomalyDetector_RaisesAuditOnProviderBlockedBurst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        // Inject 105 ProviderBlocked rows for this tenant in the last 5 min.
        for (var i = 0; i < 105; i++)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = _factory.SeedTenant.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = "{\"provider\":\"mock\"}",
            }, default);
        }

        var detector = ActivatorUtilities.CreateInstance<AnomalyDetector>(
            scope.ServiceProvider,
            new NullLogger<AnomalyDetector>());
        await detector.ScanOnceAsync(default);

        var alerts = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.AnomalyDetected)
            .ToListAsync();
        Assert.Contains(alerts, a => a.DetailsJson.Contains("provider_blocked_burst"));
    }

    // ----- SEC-008: per-tenant IP allowlist blocks + audits -----

    [Fact]
    public async Task IpAllowlistMiddleware_BlocksWhenPerTenantCidrDisallows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var settings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _factory.SeedTenant.Id);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(settings);
        }
        settings.IpAllowlistCidr = "10.0.0.0/8";
        settings.IpAllowlistJson = "";
        await db.SaveChangesAsync();

        var middleware = new IpAllowlistMiddleware(_ => Task.CompletedTask, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx, db, audit);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);

        var blocked = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.PolicyViolation && e.DetailsJson.Contains("ip_not_allowed"))
            .ToListAsync();
        Assert.NotEmpty(blocked);
    }

    [Fact]
    public async Task IpAllowlistMiddleware_AllowsWhenPerTenantCidrMatches()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var settings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _factory.SeedTenant.Id);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(settings);
        }
        settings.IpAllowlistCidr = "10.0.0.0/8";
        settings.IpAllowlistJson = "";
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpAllowlistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<IpAllowlistMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;

        await middleware.InvokeAsync(ctx, db, audit);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-cidr")]
    [InlineData("203.0.113.7/not-a-prefix")]
    [InlineData("203.0.113.7/")]
    [InlineData("203.0.113.7/32/junk")]
    public async Task IpAllowlistMiddleware_FailsClosed_WhenGlobalAllowlistInvalid(string allowlist)
    {
        var previous = Environment.GetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST", allowlist);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var middleware = new IpAllowlistMiddleware(_ => Task.CompletedTask, NullLogger<IpAllowlistMiddleware>.Instance);

            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            ctx.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx, db, audit);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST", previous);
        }
    }

    [Theory]
    [InlineData("[\"not-a-cidr\"]")]
    [InlineData("[\"203.0.113.7/not-a-prefix\"]")]
    [InlineData("[\"203.0.113.7/\"]")]
    [InlineData("[\"203.0.113.7/32/junk\"]")]
    public async Task IpAllowlistMiddleware_FailsClosed_WhenTenantAllowlistInvalid(string allowlistJson)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var settings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _factory.SeedTenant.Id);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(settings);
        }
        var previousJson = settings.IpAllowlistJson;
        var previousCidr = settings.IpAllowlistCidr;
        try
        {
            settings.IpAllowlistJson = allowlistJson;
            settings.IpAllowlistCidr = "";
            await db.SaveChangesAsync();

            var middleware = new IpAllowlistMiddleware(_ => Task.CompletedTask, NullLogger<IpAllowlistMiddleware>.Instance);
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            ctx.Request.Headers["X-RadioPad-Tenant"] = _factory.SeedTenant.Slug;
            ctx.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx, db, audit);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        }
        finally
        {
            settings.IpAllowlistJson = previousJson;
            settings.IpAllowlistCidr = previousCidr;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task IpAllowlistPipeline_UsesAuthenticatedCookieTenant_WhenTenantHeaderMissing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        var settings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _factory.SeedTenant.Id);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(settings);
        }
        var previousJson = settings.IpAllowlistJson;
        var previousCidr = settings.IpAllowlistCidr;
        try
        {
            settings.IpAllowlistCidr = "10.0.0.0/8";
            settings.IpAllowlistJson = "";
            await db.SaveChangesAsync();

            var allowlist = new IpAllowlistMiddleware(_ => Task.CompletedTask, NullLogger<IpAllowlistMiddleware>.Instance);
            var bearer = new RadioPadBearerMiddleware(ctx => allowlist.InvokeAsync(ctx, db, audit), env);
            var token = RadioPadBearerTokens.Mint(
                _factory.SeedTenant.Slug,
                _factory.SeedUser.Email,
                _factory.SeedUser.SessionEpoch,
                env,
                DateTimeOffset.UtcNow.AddMinutes(5));

            var ctx = new DefaultHttpContext();
            ctx.Request.Path = "/api/reports";
            ctx.Request.Headers.Cookie = $"{RadioPadSessionCookies.CookieName}={token}";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            ctx.Response.Body = new MemoryStream();

            await bearer.InvokeAsync(ctx, db);

            Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
            Assert.Equal(_factory.SeedTenant.Slug, ctx.Request.Headers["X-RadioPad-Tenant"].ToString());
        }
        finally
        {
            settings.IpAllowlistJson = previousJson;
            settings.IpAllowlistCidr = previousCidr;
            await db.SaveChangesAsync();
        }
    }

    // ----- SEC-002: column encryptor round-trip -----

    [Fact]
    public void ColumnEncryptor_RoundTripsString()
    {
        var enc = _factory.Services.GetRequiredService<IColumnEncryptor>();
        var ct = enc.EncryptString("totp-secret-XYZ");
        Assert.StartsWith("enc:v1:", ct);
        Assert.Equal("totp-secret-XYZ", enc.DecryptString(ct));
        Assert.Equal("", enc.EncryptString(""));
        Assert.Equal("legacy-plain", enc.DecryptString("legacy-plain"));
    }
}
