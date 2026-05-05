using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-35 PERF-004 — drives <see cref="AvailabilityMonitorService"/> end
/// to end, verifies the rolling-window snapshot, and confirms that a
/// burn-rate breach writes exactly one append-only
/// <see cref="AuditAction.SystemAlert"/> row with
/// <c>kind = "availability_burn_rate"</c>. Also exercises the
/// admin-only HTTP surface (RBAC: ItAdmin allowed, Radiologist denied).
/// </summary>
public class Iter35AvailabilityMonitorTests
{
    private const string Target = "/api/health/ready";

    private sealed class StubAvailabilityHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Throw { get; set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (Throw) throw new HttpRequestException("synthetic-failure");
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }

    private sealed class StubFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"radiopad-it35-{Guid.NewGuid():N}.db");
        public Tenant SeedTenant { get; private set; } = null!;
        public User SeedRadiologist { get; private set; } = null!;
        public User SeedAdmin { get; private set; } = null!;
        public StubAvailabilityHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:RadioPad", $"Data Source={DbPath}");
            builder.UseSetting("RADIOPAD_AVAILABILITY_AUDIT_TENANT", "it");
            builder.UseSetting("RADIOPAD_AVAILABILITY_BURN_RATE_THRESHOLD", "0.05");
            builder.UseSetting("RADIOPAD_AVAILABILITY_PROBE_TARGETS", Target);
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(AvailabilityMonitorService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }

        public async Task InitializeAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedTenant = new Tenant { Slug = "it", DisplayName = "Iter35", RequirePhiApprovedProvider = true };
            db.Tenants.Add(SeedTenant);

            SeedRadiologist = new User
            {
                TenantId = SeedTenant.Id,
                Email = "it-radiologist@radiopad.local",
                DisplayName = "IT Radiologist",
                Role = UserRole.Radiologist,
            };
            db.Users.Add(SeedRadiologist);

            SeedAdmin = new User
            {
                TenantId = SeedTenant.Id,
                Email = "it-admin@radiopad.local",
                DisplayName = "IT Admin",
                Role = UserRole.ItAdmin,
            };
            db.Users.Add(SeedAdmin);

            await db.SaveChangesAsync();
        }

        public new Task DisposeAsync()
        {
            try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* best effort */ }
            return base.DisposeAsync().AsTask();
        }

        public HttpClient CreateRadiologistClient()
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
            c.DefaultRequestHeaders.Add("X-RadioPad-User", SeedRadiologist.Email);
            return c;
        }
        public HttpClient CreateAdminHttpClient()
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
            c.DefaultRequestHeaders.Add("X-RadioPad-User", SeedAdmin.Email);
            return c;
        }
    }

    [Fact]
    public async Task Ok_Probe_Yields_Zero_Error_Rate()
    {
        await using var factory = new StubFactory();
        await factory.InitializeAsync();
        try
        {
            factory.Handler.Status = HttpStatusCode.OK;
            var monitor = factory.Services.GetRequiredService<AvailabilityMonitorService>();
            var snapshots = factory.Services.GetRequiredService<IAvailabilitySnapshotProvider>();

            await monitor.ProbeOnceAsync(default);

            var snap = snapshots.Current;
            Assert.Equal(1, snap.TotalProbes);
            Assert.Equal(0, snap.ErrorCount);
            Assert.Equal(0.0, snap.ErrorRate);
            Assert.Contains(Target, snap.Targets);
            Assert.NotNull(snap.LastCheckedAt);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Three_Failed_Probes_Emit_One_BurnRate_Audit()
    {
        await using var factory = new StubFactory();
        await factory.InitializeAsync();
        try
        {
            factory.Handler.Status = HttpStatusCode.InternalServerError;
            var monitor = factory.Services.GetRequiredService<AvailabilityMonitorService>();
            var snapshots = factory.Services.GetRequiredService<IAvailabilitySnapshotProvider>();

            await monitor.ProbeOnceAsync(default);
            await monitor.ProbeOnceAsync(default);
            await monitor.ProbeOnceAsync(default);

            var snap = snapshots.Current;
            Assert.Equal(3, snap.TotalProbes);
            Assert.Equal(3, snap.ErrorCount);
            Assert.Equal(1.0, snap.ErrorRate);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rows = await db.AuditEvents
                .Where(a => a.TenantId == factory.SeedTenant.Id && a.Action == AuditAction.SystemAlert)
                .ToListAsync();

            var burnRows = rows.Where(r => r.DetailsJson.Contains("availability_burn_rate")).ToList();
            Assert.Single(burnRows);
            Assert.Contains("\"kind\":\"availability_burn_rate\"", burnRows[0].DetailsJson);
            Assert.Contains("\"target\":\"/api/health/ready\"", burnRows[0].DetailsJson);
            Assert.Contains("\"errorRate\":1", burnRows[0].DetailsJson);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Availability_Endpoint_Rbac()
    {
        await using var factory = new StubFactory();
        await factory.InitializeAsync();
        try
        {
            var radiologist = factory.CreateRadiologistClient();
            var rDeny = await radiologist.GetAsync("/api/admin/observability/availability");
            Assert.Equal(HttpStatusCode.Forbidden, rDeny.StatusCode);

            var admin = factory.CreateAdminHttpClient();
            var rOk = await admin.GetAsync("/api/admin/observability/availability");
            Assert.Equal(HttpStatusCode.OK, rOk.StatusCode);
            var body = await rOk.Content.ReadAsStringAsync();
            Assert.Contains("\"windowSec\"", body);
            Assert.Contains("\"targets\"", body);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
