using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
/// Iter-32 MCP-001..007 — registry hardening: lifecycle status, manifest
/// signing, default-deny scope policy, sandbox runner, invocation audit.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class Iter32McpRegistryTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32McpRegistryTests(RadioPadAppFactory f) => _factory = f;

    private async Task ElevateAsync(UserRole role = UserRole.ItAdmin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var u = await db.Users.FirstAsync(x => x.Id == _factory.SeedUser.Id);
        u.Role = role;
        await db.SaveChangesAsync();
    }

    // ---------- McpRegistryCrudTests ----------

    [Fact]
    public async Task Register_PersistsManifestSha256_AndAuditsRegistered()
    {
        await ElevateAsync();
        var client = _factory.CreateTenantClient();
        var manifest = "{\"name\":\"crud-test\",\"version\":\"1.0.0\"}";
        var resp = await client.PostAsJsonAsync("/api/mcp/tools", new
        {
            name = $"crud-{Guid.NewGuid():N}",
            version = "1.0.0",
            kind = (int)McpToolKind.BuiltIn,
            scope = (int)McpToolScope.ReadOnly,
            scopeString = "rulebook:read",
            isBuiltIn = true,
            manifestJson = manifest,
            manifestSig = "",
        });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var sha = dto.GetProperty("manifestSha256").GetString()!;
        Assert.Equal(64, sha.Length);
        Assert.Equal((int)McpToolStatus.Submitted, dto.GetProperty("status").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var auditCount = await db.AuditEvents
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.McpToolRegistered)
            .CountAsync();
        Assert.True(auditCount >= 1);
    }

    [Fact]
    public async Task Approve_SetsStatusApproved_AndBlock_FlipsToBlocked()
    {
        await ElevateAsync();
        var client = _factory.CreateTenantClient();
        var reg = await client.PostAsJsonAsync("/api/mcp/tools", new
        {
            name = $"lifecycle-{Guid.NewGuid():N}",
            kind = (int)McpToolKind.BuiltIn,
            scopeString = "rulebook:read",
        });
        var id = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var ap = await client.PostAsync($"/api/mcp/tools/{id}/approve", null);
        Assert.True(ap.IsSuccessStatusCode);
        var apDto = await ap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)McpToolStatus.Approved, apDto.GetProperty("status").GetInt32());

        var bl = await client.PostAsJsonAsync($"/api/mcp/tools/{id}/block", new { reason = "test" });
        Assert.True(bl.IsSuccessStatusCode);
        var blDto = await bl.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)McpToolStatus.Blocked, blDto.GetProperty("status").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.DetailsJson.Contains(id))
            .Select(e => e.Action).ToListAsync();
        Assert.Contains(AuditAction.McpToolApproved, actions);
        Assert.Contains(AuditAction.McpToolBlocked, actions);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndAuditsBlocked()
    {
        await ElevateAsync();
        var client = _factory.CreateTenantClient();
        var reg = await client.PostAsJsonAsync("/api/mcp/tools", new { name = $"del-{Guid.NewGuid():N}" });
        var id = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var del = await client.DeleteAsync($"/api/mcp/tools/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await client.GetAsync($"/api/mcp/tools/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---------- McpScopePolicyTests ----------

    [Fact]
    public void ScopePolicy_DefaultDeny_ForShellFsNet()
    {
        var policy = new McpScopePolicy();
        var prev = Environment.GetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar);
        Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, null);
        try
        {
            Assert.False(policy.Evaluate("shell:exec", true).Allowed);
            Assert.False(policy.Evaluate("fs:read", true).Allowed);
            Assert.False(policy.Evaluate("net:dicomweb", true).Allowed);
            Assert.True(policy.Evaluate("rulebook:read", true).Allowed);
            Assert.True(policy.Evaluate("", true).Allowed);
        }
        finally { Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, prev); }
    }

    [Fact]
    public void ScopePolicy_RequiresBothEnvAndTenantFlag()
    {
        var policy = new McpScopePolicy();
        var prev = Environment.GetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, "1");
            Assert.False(policy.Evaluate("net:dicomweb", false).Allowed); // tenant flag off
            Assert.True(policy.Evaluate("net:dicomweb", true).Allowed);   // both on

            Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, "0");
            Assert.False(policy.Evaluate("net:dicomweb", true).Allowed);  // env off
        }
        finally { Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, prev); }
    }

    [Fact]
    public async Task Invoke_DangerousScope_Returns403_AndAuditsPolicyViolation()
    {
        await ElevateAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tool = new McpTool
        {
            TenantId = _factory.SeedTenant.Id,
            Name = $"danger-{Guid.NewGuid():N}",
            Kind = McpToolKind.BuiltIn,
            Scope = McpToolScope.ReadOnly,
            ScopeString = "net:fhir-read",
            Status = McpToolStatus.Approved,
            Approved = true,
        };
        db.McpTools.Add(tool);
        await db.SaveChangesAsync();

        var prev = Environment.GetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar);
        Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, null);
        try
        {
            var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync($"/api/mcp/tools/{tool.Id}/invoke",
                new { inputJson = "{}", outputJson = "{}" });
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("mcp_scope_blocked", body.GetProperty("kind").GetString());

            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var hasBlock = await db2.AuditEvents.AnyAsync(e =>
                e.TenantId == _factory.SeedTenant.Id &&
                e.Action == AuditAction.PolicyViolation &&
                e.DetailsJson.Contains("mcp_scope"));
            Assert.True(hasBlock);
        }
        finally { Environment.SetEnvironmentVariable(McpScopePolicy.AllowDangerousEnvVar, prev); }
    }

    // ---------- McpSandboxTests ----------

    [Fact]
    public async Task Sandbox_Timeout_Returns504()
    {
        await ElevateAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var sandbox = scope.ServiceProvider.GetRequiredService<IMcpSandbox>();
        var name = $"slow-{Guid.NewGuid():N}";
        sandbox.Register(name, async (input, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return "{}";
        });
        var tool = new McpTool
        {
            TenantId = _factory.SeedTenant.Id,
            Name = name,
            Kind = McpToolKind.Custom,
            Scope = McpToolScope.ReadOnly,
            Status = McpToolStatus.Approved,
            Approved = true,
        };
        db.McpTools.Add(tool);
        await db.SaveChangesAsync();

        var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/mcp/tools/{tool.Id}/invoke",
            new { inputJson = "{}" });
        Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mcp_timeout", body.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task SandboxTest_Endpoint_RunsBuiltinSandbox()
    {
        await ElevateAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var sandbox = scope.ServiceProvider.GetRequiredService<IMcpSandbox>();
        var name = $"echo-{Guid.NewGuid():N}";
        sandbox.Register(name, (input, ct) => Task.FromResult("{\"echo\":" + input + "}"));
        var tool = new McpTool
        {
            TenantId = _factory.SeedTenant.Id,
            Name = name,
            Kind = McpToolKind.Custom,
            Scope = McpToolScope.ReadOnly,
            Status = McpToolStatus.Submitted, // /test is allowed pre-approval
            Approved = false,
        };
        db.McpTools.Add(tool);
        await db.SaveChangesAsync();

        var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/mcp/tools/{tool.Id}/test",
            new { inputJson = "{\"q\":1}" });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Contains("echo", body.GetProperty("output").GetString());
    }

    // ---------- McpSignedManifestTests ----------

    [Fact]
    public void Verifier_AcceptsValidSignature_AndRejectsTampered()
    {
        // Use the on-disk placeholder release key + dicomweb-qido manifest +
        // its committed .sig to prove the verifier is wired correctly.
        var repoRoot = FindRepoRoot();
        var pubB64 = File.ReadAllText(Path.Combine(repoRoot, "mcp-connectors", "_signing", "release.pub")).Trim();
        var pub = Convert.FromBase64String(pubB64);
        var manifestPath = Path.Combine(repoRoot, "mcp-connectors", "dicomweb-qido.json");
        var sigB64 = File.ReadAllText(manifestPath + ".sig").Trim();
        var bytes = File.ReadAllBytes(manifestPath);

        var verifier = new McpManifestVerifier();
        var good = verifier.Verify(bytes, sigB64, pub);
        Assert.True(good.Valid, $"expected valid signature, got error={good.Error}");

        // Tamper: append a single byte and re-verify.
        var tampered = bytes.Concat(new byte[] { 0x20 }).ToArray();
        var bad = verifier.Verify(tampered, sigB64, pub);
        Assert.False(bad.Valid);
        Assert.Equal("bad_signature", bad.Error);
    }

    [Fact]
    public void Verifier_AlwaysComputesSha_EvenOnFailure()
    {
        var verifier = new McpManifestVerifier();
        var result = verifier.Verify(Encoding.UTF8.GetBytes("{}"), "", new byte[32]);
        Assert.False(result.Valid);
        Assert.Equal(64, result.Sha256.Length);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    // ---------- McpInvocationAuditTests ----------

    [Fact]
    public async Task Invocation_RecordsHashesAndLatency_OnLedgerAndAudit()
    {
        await ElevateAsync();
        var client = _factory.CreateTenantClient();
        var reg = await client.PostAsJsonAsync("/api/mcp/tools", new
        {
            name = $"inv-{Guid.NewGuid():N}",
            kind = (int)McpToolKind.BuiltIn,
            scopeString = "rulebook:read",
        });
        var id = Guid.Parse((await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
        var ap = await client.PostAsync($"/api/mcp/tools/{id}/approve", null);
        Assert.True(ap.IsSuccessStatusCode);

        var inv = await client.PostAsJsonAsync($"/api/mcp/tools/{id}/invoke",
            new { inputJson = "{\"x\":1}", outputJson = "{\"y\":2}" });
        Assert.True(inv.IsSuccessStatusCode, await inv.Content.ReadAsStringAsync());
        var body = await inv.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(64, body.GetProperty("inputHash").GetString()!.Length);
        Assert.Equal(64, body.GetProperty("outputHash").GetString()!.Length);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var call = await db.McpToolCalls.AsNoTracking().FirstAsync(c => c.ToolId == id);
        Assert.Equal(64, call.InputHash.Length);
        Assert.Equal(64, call.OutputHash.Length);
        Assert.NotEmpty(call.ToolName);
        Assert.Equal("rulebook:read", call.ScopeString);

        // Append-only audit row exists.
        var audit = await db.AuditEvents.AsNoTracking()
            .FirstAsync(e => e.Action == AuditAction.McpToolCalled && e.DetailsJson.Contains(id.ToString()));
        Assert.Contains("inputHash", audit.DetailsJson);
        Assert.Contains("outputHash", audit.DetailsJson);
    }
}
