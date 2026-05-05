using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Spins up an in-process Kestrel-less host wired to a temp SQLite database
/// so integration tests exercise the full middleware + controller pipeline
/// without touching the dev database.
/// </summary>
public class RadioPadAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"radiopad-it-{Guid.NewGuid():N}.db");

    public Tenant SeedTenant { get; private set; } = null!;
    public User SeedUser { get; private set; } = null!;
    public User SeedAdmin { get; private set; } = null!;
    public User SeedBillingAdmin { get; private set; } = null!;
    public User SeedComplianceReviewer { get; private set; } = null!;
    public ProviderConfig MockProvider { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:RadioPad", $"Data Source={DbPath}");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        await db.Database.EnsureCreatedAsync();

        SeedTenant = new Tenant { Slug = "it", DisplayName = "Integration", RequirePhiApprovedProvider = true };
        db.Tenants.Add(SeedTenant);

        SeedUser = new User
        {
            TenantId = SeedTenant.Id,
            Email = "it-radiologist@radiopad.local",
            DisplayName = "IT Radiologist",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(SeedUser);

        SeedAdmin = new User
        {
            TenantId = SeedTenant.Id,
            Email = "it-admin@radiopad.local",
            DisplayName = "IT Admin",
            Role = UserRole.ItAdmin,
        };
        db.Users.Add(SeedAdmin);

        SeedBillingAdmin = new User
        {
            TenantId = SeedTenant.Id,
            Email = "it-billing@radiopad.local",
            DisplayName = "IT Billing Admin",
            Role = UserRole.BillingAdmin,
        };
        db.Users.Add(SeedBillingAdmin);

        SeedComplianceReviewer = new User
        {
            TenantId = SeedTenant.Id,
            Email = "it-compliance@radiopad.local",
            DisplayName = "IT Compliance Reviewer",
            Role = UserRole.ComplianceReviewer,
        };
        db.Users.Add(SeedComplianceReviewer);

        MockProvider = new ProviderConfig
        {
            TenantId = SeedTenant.Id,
            Name = "Mock",
            Adapter = "mock",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        };
        db.Providers.Add(MockProvider);
        await db.SaveChangesAsync();
    }

    public new Task DisposeAsync()
    {
        try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* best effort */ }
        return base.DisposeAsync().AsTask();
    }

    public HttpClient CreateTenantClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", SeedUser.Email);
        return client;
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", SeedAdmin.Email);
        return client;
    }

    public HttpClient CreateBillingAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", SeedBillingAdmin.Email);
        return client;
    }

    public HttpClient CreateComplianceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", SeedComplianceReviewer.Email);
        return client;
    }
}
