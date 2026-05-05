using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-31 MCP-001..007 — tool registry + sandbox tests. Covers default-deny
/// for External-scope tools, sandbox 5-second timeout, and the registry CRUD
/// + invocation audit chain.
/// </summary>
public class Iter31McpTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter31McpTests(RadioPadAppFactory f) => _factory = f;

    private async Task ElevateUserToAdminAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var u = await db.Users.FirstAsync(x => x.Id == _factory.SeedUser.Id);
        u.Role = UserRole.ItAdmin;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExternalScopeTool_Unapproved_Returns403MpcBlocked()
    {
        await ElevateUserToAdminAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tool = new McpTool
        {
            TenantId = _factory.SeedTenant.Id,
            Name = "ext-tool-deny",
            Kind = McpToolKind.BuiltIn,
            Scope = McpToolScope.External,
            Approved = false,
        };
        db.McpTools.Add(tool);
        await db.SaveChangesAsync();

        var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/mcp/tools/{tool.Id}/invoke",
            new { inputJson = "{}", outputJson = "{}" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
        Assert.Equal("mcp_blocked", body!["kind"].ToString());
    }

    [Fact]
    public async Task ExternalScopeTool_ApprovedButTenantFlagOff_Returns403()
    {
        await ElevateUserToAdminAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var t = await db.Tenants.FirstAsync(x => x.Id == _factory.SeedTenant.Id);
        t.AllowExternalMcp = false;
        var tool = new McpTool
        {
            TenantId = _factory.SeedTenant.Id,
            Name = "ext-tool-flag-off",
            Kind = McpToolKind.BuiltIn,
            Scope = McpToolScope.External,
            Approved = true,
            ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.McpTools.Add(tool);
        await db.SaveChangesAsync();

        var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/mcp/tools/{tool.Id}/invoke",
            new { inputJson = "{}", outputJson = "{}" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task RegistryRoundTrip_RegisterApproveInvokeRevoke_AuditsExpectedActions()
    {
        await ElevateUserToAdminAsync();
        var client = _factory.CreateTenantClient();

        var register = await client.PostAsJsonAsync("/api/mcp/tools", new
        {
            name = $"tool-{Guid.NewGuid():N}",
            kind = (int)McpToolKind.BuiltIn,
            scope = (int)McpToolScope.ReadOnly,
            allowedConnectorPaths = new[] { @"^/v1/" },
        });
        Assert.True(register.IsSuccessStatusCode, await register.Content.ReadAsStringAsync());
        var registered = await register.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var idStr = registered!["id"].ToString();
        var id = Guid.Parse(idStr!);

        var approve = await client.PostAsync($"/api/mcp/tools/{id}/approve", null);
        Assert.True(approve.IsSuccessStatusCode);

        var invoke = await client.PostAsJsonAsync($"/api/mcp/tools/{id}/invoke",
            new { inputJson = "{\"q\":\"hello\"}", outputJson = "{\"r\":\"world\"}" });
        Assert.True(invoke.IsSuccessStatusCode, await invoke.Content.ReadAsStringAsync());

        var revoke = await client.PostAsync($"/api/mcp/tools/{id}/revoke", null);
        Assert.True(revoke.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.DetailsJson.Contains(id.ToString()))
            .Select(e => e.Action)
            .ToListAsync();
        Assert.Contains(AuditAction.McpToolApproved, actions);
        Assert.Contains(AuditAction.McpToolCalled, actions);
        Assert.Contains(AuditAction.McpToolRevoked, actions);

        var calls = await db.McpToolCalls.AsNoTracking()
            .Where(c => c.TenantId == _factory.SeedTenant.Id && c.ToolId == id)
            .ToListAsync();
        Assert.Single(calls);
        Assert.NotEmpty(calls[0].InputHash);
        Assert.NotEmpty(calls[0].OutputHash);
    }

    [Fact]
    public async Task Sandbox_TimeoutCancelsRunningTool()
    {
        var sandbox = new InProcessMcpSandbox();
        sandbox.Register("slow-tool", async (input, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return "{}";
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await sandbox.InvokeAsync(
                "slow-tool",
                "{}",
                Array.Empty<string>(),
                TimeSpan.FromMilliseconds(200), // tighter timeout to keep CI fast — proves the cancellation path
                default);
        });
    }

    [Fact]
    public async Task Sandbox_FiveSecondTimeoutTriggersCancellation()
    {
        // Explicit 5-second budget per spec — proves the documented timeout
        // boundary is honoured. Body sleeps 6s to force the cancellation path.
        var sandbox = new InProcessMcpSandbox();
        sandbox.Register("sleep-6s", async (input, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            return "{}";
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await sandbox.InvokeAsync(
                "sleep-6s",
                "{}",
                Array.Empty<string>(),
                TimeSpan.FromSeconds(5),
                default);
        });
        sw.Stop();
        // Allow some slack but ensure we did NOT wait a full 6s.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5.9), $"Sandbox waited too long: {sw.Elapsed}");
    }

    [Fact]
    public void Sandbox_ConnectorAllowlist_RegexMatches()
    {
        var allow = new[] { @"^https://api\.example\.com/v1/" };
        Assert.True(InProcessMcpSandbox.CheckConnector("https://api.example.com/v1/foo", allow));
        Assert.False(InProcessMcpSandbox.CheckConnector("https://evil.example.org/foo", allow));
    }
}
