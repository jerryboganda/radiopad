using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Drives every <see cref="EndpointPermissionMatrix"/> entry end-to-end: a role that
/// HOLDS the required permission must NOT get 403; a role that LACKS it MUST get 403.
/// This turns the matrix from documentation into an enforced invariant — a regression
/// that drops a gate, or a matrix row whose route/permission drifts from the code, fails
/// here. (For mutating routes the permission gate runs immediately after ResolveContext,
/// before body/entity validation, so an empty body still surfaces the 403.)
/// </summary>
public class RbacMatrixEnforcementTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    private readonly Dictionary<UserRole, string> _emailByRole = new();

    // Minimal model-valid bodies so the in-action permission gate is actually reached
    // for body-required mutations (ASP.NET [ApiController] 400s on a missing required
    // field BEFORE the action runs). Gate runs before any body PROCESSING, so content
    // need only satisfy model validation (required fields present); enums are ints.
    private static readonly Dictionary<string, string> Bodies = new()
    {
        ["POST /api/providers"] = "{\"name\":\"P\",\"adapter\":\"mock\",\"model\":\"m\",\"endpointUrl\":\"\",\"apiKeySecretRef\":\"\",\"compliance\":1,\"enabled\":true,\"priority\":10,\"costPerInputKToken\":0,\"costPerOutputKToken\":0,\"maxCostPerCallUsd\":0,\"quality\":0.5,\"retentionLabel\":\"\"}",
        ["POST /api/rulebooks"] = "{\"yaml\":\"rulebook_id: t\\nname: T\\nversion: 1.0.0\\nstatus: draft\"}",
        ["POST /api/rulebooks/{id}/rollback"] = "{\"version\":\"1.0.0\"}",
        ["POST /api/templates"] = "{\"templateId\":\"t\",\"name\":\"T\",\"modality\":\"CT\",\"bodyPart\":\"Chest\",\"subspecialty\":\"x\",\"sectionsJson\":\"[]\"}",
        ["POST /api/prompts/overrides"] = "{\"rulebookId\":\"x\",\"blockKey\":\"impression\",\"body\":\"y\"}",
        ["POST /api/prompts/test-golden"] = "{\"rulebookId\":\"x\"}",
        ["POST /api/reports/{id}/ai"] = "{\"mode\":\"impression\"}",
        ["POST /api/reports/{id}/rewrite"] = "{\"mode\":\"concise\"}",
        ["POST /api/reports/{id}/sign"] = "{\"role\":\"Primary\"}",
        ["POST /api/reports/{id}/addendum"] = "{\"body\":\"x\"}",
        ["POST /api/mcp/tools/{id}/invoke"] = "{\"inputJson\":\"{}\"}",
    };

    public RbacMatrixEnforcementTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EveryMatrixEntry_AllowsPermittedRole_AndForbidsUnpermittedRole()
    {
        var failures = new List<string>();

        foreach (var e in EndpointPermissionMatrix.All)
        {
            var rolesWith = RolePermissionMap.RolesFor(e.Permission).ToHashSet();
            Assert.NotEmpty(rolesWith); // every matrix permission must be granted to someone
            var permitted = rolesWith.First();
            var denied = Enum.GetValues<UserRole>().FirstOrDefault(r => !rolesWith.Contains(r));
            Assert.True(rolesWith.Count < Enum.GetValues<UserRole>().Length,
                $"{e.Method} {e.RouteTemplate}: permission {e.Permission} is held by EVERY role — gate is meaningless");

            var url = BuildUrl(e.RouteTemplate);
            var body = Bodies.TryGetValue($"{e.Method} {e.RouteTemplate}", out var b) ? b : "{}";

            var deniedStatus = await SendAsync(denied, e.Method, url, body);
            if (deniedStatus != (int)HttpStatusCode.Forbidden)
                failures.Add($"DENY {e.Method} {e.RouteTemplate}: role {denied} lacks {e.Permission} but got {deniedStatus} (expected 403)");

            var permittedStatus = await SendAsync(permitted, e.Method, url, body);
            if (permittedStatus == (int)HttpStatusCode.Forbidden)
                failures.Add($"ALLOW {e.Method} {e.RouteTemplate}: role {permitted} has {e.Permission} but got 403");
        }

        Assert.True(failures.Count == 0,
            $"RBAC matrix enforcement failures ({failures.Count}):\n - " + string.Join("\n - ", failures));
    }

    private static string BuildUrl(string template)
    {
        var url = template;
        // substitute any route placeholder with a concrete (non-existent) value so the
        // request reaches the action and its permission gate.
        url = System.Text.RegularExpressions.Regex.Replace(url, @"\{[^}]+\}", Guid.NewGuid().ToString());
        if (!url.StartsWith("/")) url = "/" + url;
        return url;
    }

    private async Task<int> SendAsync(UserRole role, string method, string url, string bodyJson)
    {
        using var client = await ClientForRoleAsync(role);
        HttpResponseMessage resp;
        var m = method.ToUpperInvariant();
        if (m == "GET") resp = await client.GetAsync(url);
        else if (m == "DELETE") resp = await client.DeleteAsync(url);
        else
        {
            var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            var req = new HttpRequestMessage(new HttpMethod(m), url) { Content = content };
            resp = await client.SendAsync(req);
        }
        return (int)resp.StatusCode;
    }

    private async Task<HttpClient> ClientForRoleAsync(UserRole role)
    {
        if (!_emailByRole.TryGetValue(role, out var email))
        {
            email = $"rbac-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@radiopad.local";
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            if (!await db.Users.AnyAsync(u => u.Email == email && u.TenantId == _factory.SeedTenant.Id))
            {
                db.Users.Add(new User
                {
                    TenantId = _factory.SeedTenant.Id,
                    Email = email,
                    DisplayName = role.ToString(),
                    Role = role,
                });
                await db.SaveChangesAsync();
            }
            _emailByRole[role] = email;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }
}
