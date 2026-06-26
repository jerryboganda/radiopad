using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Auth;
using RadioPad.Api.Controllers;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Api.Middleware;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public sealed class RadioPadBearerMiddlewareTests
{
    [Theory]
    [InlineData("/api/auth/signin")]
    // AUTH-003: the authenticated self-service change stays behind the gate; the
    // pre-session password sign-in route is matched exactly and is public.
    [InlineData("/api/auth/password/change")]
    [InlineData("/api/auth/webauthn/register-options")]
    [InlineData("/api/auth/webauthn/register")]
    [InlineData("/api/auth/webauthn/credentials")]
    [InlineData("/api/auth/device/approve")]
    [InlineData("/api/auth/device/deny")]
    public async Task Production_Rejects_Protected_Auth_Routes_Without_Validated_Identity(string path)
    {
        using var db = CreateDb();
        var invoked = false;
        var middleware = new RadioPadBearerMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, new TestHostEnvironment("Production"));

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        await middleware.InvokeAsync(ctx, db);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/health/ready")]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/magic-link/request")]
    [InlineData("/api/auth/magic-link/consume")]
    [InlineData("/api/auth/device/authorize")]
    [InlineData("/api/auth/device/token")]
    [InlineData("/api/billing/webhook")]
    // AUTH-003 password + mandatory-TOTP entrance runs before a session exists.
    // These pass the middleware but are still gated inside their controllers
    // (password verify, signed mfa-setup ticket / verified identity, TOTP code,
    // or the RADIOPAD_BOOTSTRAP_SECRET header).
    [InlineData("/api/auth/password")]
    [InlineData("/api/auth/password/reset-with-totp")]
    [InlineData("/api/auth/mfa/login")]
    [InlineData("/api/auth/mfa/enroll")]
    [InlineData("/api/auth/mfa/verify")]
    [InlineData("/api/admin/bootstrap-org")]
    public async Task Production_Allows_Login_Bootstrap_And_Webhook_Routes(string path)
    {
        using var db = CreateDb();
        var invoked = false;
        var middleware = new RadioPadBearerMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, new TestHostEnvironment("Production"));

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        await middleware.InvokeAsync(ctx, db);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Bearer_Rejects_Expired_Token()
    {
        using var secret = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", "test-secret-with-at-least-thirty-two-chars");
        using var db = CreateDb();
        var tenant = new Tenant { Slug = "it", DisplayName = "Integration" };
        var user = new User { TenantId = tenant.Id, Email = "it-radiologist@radiopad.local", IsActive = true };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var invoked = false;
        var env = new TestHostEnvironment("Production");
        var middleware = new RadioPadBearerMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, env);
        var token = RadioPadBearerTokens.Mint(
            tenant.Slug,
            user.Email,
            user.SessionEpoch,
            env,
            DateTimeOffset.UtcNow.AddHours(-2),
            TimeSpan.FromMinutes(5));

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/reports";
        ctx.Request.Headers.Authorization = "Bearer " + token;
        ctx.Request.Headers["X-RadioPad-Tenant"] = tenant.Slug;
        ctx.Request.Headers["X-RadioPad-User"] = user.Email;

        await middleware.InvokeAsync(ctx, db);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CookieBearer_Allows_Production_Browser_Session_Without_Dev_Headers()
    {
        using var secret = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", "test-secret-with-at-least-thirty-two-chars");
        using var db = CreateDb();
        var tenant = new Tenant { Slug = "it", DisplayName = "Integration" };
        var user = new User { TenantId = tenant.Id, Email = "it-radiologist@radiopad.local", IsActive = true };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var invoked = false;
        var env = new TestHostEnvironment("Production");
        var middleware = new RadioPadBearerMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, env);
        var token = RadioPadBearerTokens.Mint(tenant.Slug, user.Email, user.SessionEpoch, env);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/reports";
        ctx.Request.Headers.Cookie = $"{RadioPadSessionCookies.CookieName}={token}";

        await middleware.InvokeAsync(ctx, db);

        Assert.True(invoked);
        Assert.Equal(tenant.Slug, ctx.Request.Headers["X-RadioPad-Tenant"].ToString());
        Assert.Equal(user.Email, ctx.Request.Headers["X-RadioPad-User"].ToString());
    }

    [Fact]
    public async Task MagicLink_Request_InProductionWithoutSmtp_DoesNotExpose_DevLink()
    {
        MagicLinkRateLimiter.ResetForTesting();
        using var smtp = EnvVarScope.Set("RADIOPAD_SMTP_HOST", null);
        using var publicUrl = EnvVarScope.Set("RADIOPAD_PUBLIC_WEB_URL", "https://radiopad.example.com");
        using var db = CreateDb();
        var tenant = new Tenant { Slug = "it", DisplayName = "Integration" };
        var user = new User
        {
            TenantId = tenant.Id,
            Email = "it-radiologist@radiopad.local",
            DisplayName = "IT Radiologist",
            IsActive = true,
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new MagicLinkController(
            db,
            new NoopAuditLog(),
            NullLogger<MagicLinkController>.Instance,
            new TestHostEnvironment("Production"),
            new NoopEmailSender())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.RequestMagicLink(new MagicLinkController.RequestDto(
            tenant.Slug,
            user.Email,
            "https://attacker.example/login"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        Assert.DoesNotContain("devLink", body);
        Assert.DoesNotContain("ml_", body);
        Assert.DoesNotContain("attacker.example", body);
    }

    [Fact]
    public void Production_Startup_Rejects_Missing_Auth_Secret()
    {
        using var secret = EnvVarScope.Set("RADIOPAD_AUTH_SECRET", null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RadioPadBearerTokens.ValidateStartupSecret(new TestHostEnvironment("Production")));

        Assert.Contains("RADIOPAD_AUTH_SECRET", ex.Message);
    }

    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "RadioPad.Api.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoopAuditLog : IAuditLog
    {
        public Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(
            Guid tenantId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int take = 200,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());

        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditChainVerification(0, true, null, null));
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct) => Task.FromResult(true);
    }
}