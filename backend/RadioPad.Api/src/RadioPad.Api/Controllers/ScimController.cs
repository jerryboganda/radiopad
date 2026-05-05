using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-005 / SEC-007 — SCIM 2.0 user provisioning surface for Enterprise
/// IdPs (Okta, Azure AD, OneLogin, etc.). Implements the minimum of RFC 7644
/// needed for the IdP's provisioning agent: list, get, create, replace,
/// patch (`active`), and delete users.
///
/// Authentication is a tenant-scoped bearer token stored in
/// <see cref="TenantSettings.ScimBearerSecret"/>; the tenant slug arrives via
/// the standard <c>X-RadioPad-Tenant</c> header so the same SCIM endpoint
/// can serve every tenant. Bearer comparison is constant-time to avoid
/// timing oracles.
///
/// PHI policy: SCIM only handles identity (email, displayName, active flag,
/// role mapping). It never touches reports, audit chain entries, or AI
/// requests. Soft-delete sets <see cref="User.IsActive"/> = false so the
/// audit chain remains intact (PRD §13.2 immutability).
/// </summary>
[ApiController]
[Route("scim/v2")]
public class ScimController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    private const string SchemaUser = "urn:ietf:params:scim:schemas:core:2.0:User";
    private const string SchemaGroup = "urn:ietf:params:scim:schemas:core:2.0:Group";
    private const string SchemaListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    private const string SchemaError = "urn:ietf:params:scim:api:messages:2.0:Error";
    private const string SchemaPatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    public ScimController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    private async Task<(Tenant? tenant, IActionResult? deny)> AuthorizeAsync(CancellationToken ct)
    {
        var slug = Request.Headers["X-RadioPad-Tenant"].ToString();
        if (string.IsNullOrEmpty(slug))
            return (null, Error(401, "Missing tenant header."));
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
            return (null, Error(401, "Unknown tenant."));
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var expected = ResolveSecret(settings?.ScimBearerSecret) ?? "";
        if (string.IsNullOrEmpty(expected))
            return (null, Error(503, "SCIM is not configured for this tenant."));
        var auth = Request.Headers["Authorization"].ToString();
        var presented = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : "";
        if (!FixedTimeEquals(expected, presented))
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason = "scim:bad_bearer" }),
            }, ct);
            return (null, Error(401, "Invalid bearer token."));
        }
        return (tenant, null);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        if (bytesA.Length != bytesB.Length) return false;
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    private static string? ResolveSecret(string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef)) return null;
        return secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(secretRef[4..])
            : secretRef;
    }

    private IActionResult Error(int status, string detail) => StatusCode(status, new
    {
        schemas = new[] { SchemaError },
        status = status.ToString(),
        detail,
    });

    private static object Resource(User u, string baseUrl) => new
    {
        schemas = new[] { SchemaUser },
        id = u.Id.ToString(),
        userName = u.Email,
        active = u.IsActive,
        name = new { formatted = u.DisplayName },
        emails = new[] { new { value = u.Email, primary = true, type = "work" } },
        // Role surfaces as a SCIM role array; downstream IdPs use this for
        // role-based access workflows.
        roles = new[] { new { value = u.Role.ToString() } },
        meta = new
        {
            resourceType = "User",
            created = u.CreatedAt.ToString("o"),
            lastModified = u.UpdatedAt.ToString("o"),
            location = $"{baseUrl}/Users/{u.Id}",
        },
    };

    private static object GroupResource(ScimGroup group, IReadOnlyCollection<User> members, string baseUrl) => new
    {
        schemas = new[] { SchemaGroup },
        id = group.Id.ToString(),
        externalId = group.ExternalId,
        displayName = group.DisplayName,
        members = members
            .OrderBy(m => m.Email)
            .Select(m => new
            {
                value = m.Id.ToString(),
                display = m.Email,
                @ref = $"{baseUrl}/Users/{m.Id}",
            })
            .ToArray(),
        meta = new
        {
            resourceType = "Group",
            created = group.CreatedAt.ToString("o"),
            lastModified = group.UpdatedAt.ToString("o"),
            location = $"{baseUrl}/Groups/{group.Id}",
        },
    };

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}/scim/v2";

    [HttpGet("Users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100,
        [FromQuery] string? filter = null,
        CancellationToken ct = default)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        if (count is < 0 or > 500) count = 100;
        if (startIndex < 1) startIndex = 1;

        var q = _db.Users.AsNoTracking().Where(u => u.TenantId == tenant!.Id && u.IsActive);
        // Minimal SCIM filter: `userName eq "x"` (used by Okta/Azure AD on lookup).
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                filter, @"userName\s+eq\s+""(?<v>[^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) q = q.Where(u => u.Email == m.Groups["v"].Value);
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(u => u.Email).Skip(startIndex - 1).Take(count).ToListAsync(ct);
        var baseUrl = BaseUrl();
        return Ok(new Dictionary<string, object?>
        {
            ["schemas"] = new[] { SchemaListResponse },
            ["totalResults"] = total,
            ["startIndex"] = startIndex,
            ["itemsPerPage"] = page.Count,
            ["Resources"] = page.Select(u => Resource(u, baseUrl)).ToArray(),
        });
    }

    [HttpGet("Users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant!.Id && x.IsActive, ct);
        if (u is null) return Error(404, "User not found.");
        return Ok(Resource(u, BaseUrl()));
    }

    public record ScimUserDto(string? userName, ScimName? name, bool? active, ScimEmail[]? emails, ScimRole[]? roles);
    public record ScimName(string? formatted, string? givenName, string? familyName);
    public record ScimEmail(string? value, bool? primary, string? type);
    public record ScimRole(string? value);
    public record ScimGroupDto(string? displayName, string? externalId, ScimMember[]? members);
    public record ScimMember(string? value, string? display, string? type);

    [HttpPost("Users")]
    public async Task<IActionResult> CreateUser([FromBody] ScimUserDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var email = dto.userName ?? dto.emails?.FirstOrDefault(e => e?.primary == true)?.value
            ?? dto.emails?.FirstOrDefault()?.value;
        if (string.IsNullOrWhiteSpace(email))
            return Error(400, "userName or emails[0].value is required.");
        var exists = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant!.Id && u.Email == email, ct);
        if (exists is not null) return Conflict(new { schemas = new[] { SchemaError }, status = "409", detail = "User already exists." });

        var u = new User
        {
            TenantId = tenant!.Id,
            Email = email,
            DisplayName = dto.name?.formatted ?? $"{dto.name?.givenName} {dto.name?.familyName}".Trim(),
            Role = MapRole(dto.roles),
            IsActive = dto.active ?? true,
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync(ct);
        return Created($"{BaseUrl()}/Users/{u.Id}", Resource(u, BaseUrl()));
    }

    [HttpPut("Users/{id:guid}")]
    public async Task<IActionResult> ReplaceUser(Guid id, [FromBody] ScimUserDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant!.Id, ct);
        if (u is null) return Error(404, "User not found.");
        if (!string.IsNullOrWhiteSpace(dto.userName)) u.Email = dto.userName!;
        if (dto.name?.formatted is not null) u.DisplayName = dto.name.formatted;
        else if (dto.name is not null) u.DisplayName = $"{dto.name.givenName} {dto.name.familyName}".Trim();
        if (dto.active is not null) u.IsActive = dto.active.Value;
        if (dto.roles is { Length: > 0 }) u.Role = MapRole(dto.roles);
        u.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(Resource(u, BaseUrl()));
    }

    public record ScimPatchOp(string op, string? path, JsonElement? value);
    public record ScimPatchDto(string[]? schemas, ScimPatchOp[]? Operations);

    [HttpPatch("Users/{id:guid}")]
    public async Task<IActionResult> PatchUser(Guid id, [FromBody] ScimPatchDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant!.Id, ct);
        if (u is null) return Error(404, "User not found.");

        foreach (var op in dto.Operations ?? Array.Empty<ScimPatchOp>())
        {
            // Okta + Azure AD send `replace` for `active` on deprovision.
            var path = (op.path ?? "").Trim();
            var verb = (op.op ?? "").ToLowerInvariant();
            if (verb is not ("replace" or "add")) continue;
            if (string.Equals(path, "active", StringComparison.OrdinalIgnoreCase))
            {
                u.IsActive = op.value?.GetBoolean() ?? u.IsActive;
            }
            else if (string.Equals(path, "displayName", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(path, "name.formatted", StringComparison.OrdinalIgnoreCase))
            {
                u.DisplayName = op.value?.GetString() ?? u.DisplayName;
            }
            else if (string.IsNullOrEmpty(path) && op.value is { ValueKind: JsonValueKind.Object } obj)
            {
                // Bulk replace shape: { "active": false, ... }
                if (obj.TryGetProperty("active", out var active)) u.IsActive = active.GetBoolean();
                if (obj.TryGetProperty("displayName", out var dn)) u.DisplayName = dn.GetString() ?? u.DisplayName;
            }
        }
        u.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(Resource(u, BaseUrl()));
    }

    [HttpDelete("Users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant!.Id, ct);
        if (u is null) return Error(404, "User not found.");
        // Soft-delete to preserve foreign keys + audit-chain referential
        // integrity (PRD §13.2). The IdP sees a 204 + the user vanishes from
        // subsequent GET /Users responses via the active filter.
        u.IsActive = false;
        u.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("Groups")]
    public async Task<IActionResult> ListGroups(
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100,
        [FromQuery] string? filter = null,
        CancellationToken ct = default)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        if (count is < 0 or > 500) count = 100;
        if (startIndex < 1) startIndex = 1;

        var q = _db.ScimGroups.AsNoTracking().Where(g => g.TenantId == tenant!.Id);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                filter, @"displayName\s+eq\s+""(?<v>[^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) q = q.Where(g => g.DisplayName == m.Groups["v"].Value);
        }

        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(g => g.DisplayName).Skip(startIndex - 1).Take(count).ToListAsync(ct);
        var baseUrl = BaseUrl();
        var resources = new List<object>();
        foreach (var group in page)
        {
            resources.Add(GroupResource(group, await LoadGroupMembersAsync(group, ct), baseUrl));
        }
        return Ok(new Dictionary<string, object?>
        {
            ["schemas"] = new[] { SchemaListResponse },
            ["totalResults"] = total,
            ["startIndex"] = startIndex,
            ["itemsPerPage"] = resources.Count,
            ["Resources"] = resources.ToArray(),
        });
    }

    [HttpGet("Groups/{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var group = await _db.ScimGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.TenantId == tenant!.Id, ct);
        if (group is null) return Error(404, "Group not found.");
        return Ok(GroupResource(group, await LoadGroupMembersAsync(group, ct), BaseUrl()));
    }

    [HttpPost("Groups")]
    public async Task<IActionResult> CreateGroup([FromBody] ScimGroupDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        if (string.IsNullOrWhiteSpace(dto.displayName)) return Error(400, "displayName is required.");

        var exists = await _db.ScimGroups.AnyAsync(g => g.TenantId == tenant!.Id && g.DisplayName == dto.displayName, ct);
        if (exists) return Conflict(new { schemas = new[] { SchemaError }, status = "409", detail = "Group already exists." });

        var group = new ScimGroup
        {
            TenantId = tenant!.Id,
            DisplayName = dto.displayName.Trim(),
            ExternalId = dto.externalId,
        };
        _db.ScimGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        await ReplaceGroupMembersAsync(tenant, group, MemberIds(dto.members), ct);
        await AuditGroupAsync(tenant.Id, group.Id, "created", ct);
        return Created($"{BaseUrl()}/Groups/{group.Id}", GroupResource(group, await LoadGroupMembersAsync(group, ct), BaseUrl()));
    }

    [HttpPut("Groups/{id:guid}")]
    public async Task<IActionResult> ReplaceGroup(Guid id, [FromBody] ScimGroupDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var currentTenant = tenant!;
        var group = await _db.ScimGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == currentTenant.Id, ct);
        if (group is null) return Error(404, "Group not found.");
        if (string.IsNullOrWhiteSpace(dto.displayName)) return Error(400, "displayName is required.");

        var displayName = dto.displayName.Trim();
        if (await IsDuplicateGroupDisplayNameAsync(currentTenant.Id, group.Id, displayName, ct))
            return Conflict(new { schemas = new[] { SchemaError }, status = "409", detail = "Group already exists." });

        group.DisplayName = displayName;
        group.ExternalId = dto.externalId;
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await ReplaceGroupMembersAsync(currentTenant, group, MemberIds(dto.members), ct);
        await AuditGroupAsync(currentTenant.Id, group.Id, "replaced", ct);
        return Ok(GroupResource(group, await LoadGroupMembersAsync(group, ct), BaseUrl()));
    }

    [HttpPatch("Groups/{id:guid}")]
    public async Task<IActionResult> PatchGroup(Guid id, [FromBody] ScimPatchDto dto, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var currentTenant = tenant!;
        var group = await _db.ScimGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == currentTenant.Id, ct);
        if (group is null) return Error(404, "Group not found.");

        foreach (var op in dto.Operations ?? Array.Empty<ScimPatchOp>())
        {
            var verb = (op.op ?? "").Trim().ToLowerInvariant();
            var path = (op.path ?? "").Trim();
            if (verb is not ("add" or "replace" or "remove")) continue;

            if (string.IsNullOrEmpty(path) && op.value is { ValueKind: JsonValueKind.Object } obj)
            {
                if (verb != "remove" && obj.TryGetProperty("displayName", out var displayNameValue))
                {
                    var displayName = displayNameValue.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(displayName) && displayName != group.DisplayName)
                    {
                        if (await IsDuplicateGroupDisplayNameAsync(currentTenant.Id, group.Id, displayName, ct))
                            return Conflict(new { schemas = new[] { SchemaError }, status = "409", detail = "Group already exists." });
                        group.DisplayName = displayName;
                        group.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }
                if (obj.TryGetProperty("externalId", out var externalIdValue))
                {
                    group.ExternalId = verb == "remove" ? null : externalIdValue.GetString();
                    group.UpdatedAt = DateTimeOffset.UtcNow;
                }
                if (obj.TryGetProperty("members", out var membersValue))
                {
                    if (verb == "replace") await ReplaceGroupMembersAsync(currentTenant, group, MemberIds(membersValue), ct);
                    else if (verb == "add") await AddGroupMembersAsync(currentTenant, group, MemberIds(membersValue), ct);
                    else await RemoveGroupMembersAsync(currentTenant, group, MemberIds(membersValue), removeAllWhenEmpty: true, ct);
                }
            }
            else if (string.Equals(path, "displayName", StringComparison.OrdinalIgnoreCase) && verb != "remove")
            {
                var displayName = op.value?.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(displayName) && displayName != group.DisplayName)
                {
                    if (await IsDuplicateGroupDisplayNameAsync(currentTenant.Id, group.Id, displayName, ct))
                        return Conflict(new { schemas = new[] { SchemaError }, status = "409", detail = "Group already exists." });
                    group.DisplayName = displayName;
                    group.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else if (string.Equals(path, "externalId", StringComparison.OrdinalIgnoreCase))
            {
                group.ExternalId = verb == "remove" ? null : op.value?.GetString();
                group.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else if (string.Equals(path, "members", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(path))
            {
                if (verb == "replace") await ReplaceGroupMembersAsync(currentTenant, group, MemberIds(op.value), ct);
                else if (verb == "add") await AddGroupMembersAsync(currentTenant, group, MemberIds(op.value), ct);
                else await RemoveGroupMembersAsync(currentTenant, group, MemberIds(op.value), removeAllWhenEmpty: true, ct);
            }
            else
            {
                var removedMemberId = TryMemberIdFromPath(path);
                if (removedMemberId is not null && verb == "remove")
                {
                    await RemoveGroupMembersAsync(currentTenant, group, new[] { removedMemberId.Value }, removeAllWhenEmpty: false, ct);
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        await AuditGroupAsync(currentTenant.Id, group.Id, "patched", ct);
        return Ok(GroupResource(group, await LoadGroupMembersAsync(group, ct), BaseUrl()));
    }

    [HttpDelete("Groups/{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken ct)
    {
        var (tenant, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        var currentTenant = tenant!;
        var group = await _db.ScimGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == currentTenant.Id, ct);
        if (group is null) return Error(404, "Group not found.");
        var memberships = await _db.ScimGroupMemberships.Where(m => m.TenantId == currentTenant.Id && m.GroupId == group.Id).ToListAsync(ct);
        var affected = memberships.Select(m => m.UserId).Distinct().ToList();
        _db.ScimGroupMemberships.RemoveRange(memberships);
        _db.ScimGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
        await ProjectRolesAsync(currentTenant.Id, affected, ct);
        await AuditGroupAsync(currentTenant.Id, id, "deleted", ct);
        return NoContent();
    }

    [HttpGet("ServiceProviderConfig")]
    public async Task<IActionResult> ServiceProviderConfig(CancellationToken ct)
    {
        var (_, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        return Ok(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
            documentationUri = "https://radiopad.com/docs/scim",
            patch = new { supported = true },
            bulk = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
            filter = new { supported = true, maxResults = 500 },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
            authenticationSchemes = new[]
            {
                new
                {
                    type = "oauthbearertoken",
                    name = "OAuth Bearer Token",
                    description = "Tenant-scoped bearer token configured under Tenant Settings.",
                    primary = true,
                },
            },
        });
    }

    [HttpGet("ResourceTypes")]
    public async Task<IActionResult> ResourceTypes(CancellationToken ct)
    {
        var (_, deny) = await AuthorizeAsync(ct);
        if (deny is not null) return deny;
        return Ok(new Dictionary<string, object?>
        {
            ["schemas"] = new[] { SchemaListResponse },
            ["totalResults"] = 2,
            ["Resources"] = new[]
            {
                new
                {
                    schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ResourceType" },
                    id = "User",
                    name = "User",
                    endpoint = "/Users",
                    schema = SchemaUser,
                },
                new
                {
                    schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ResourceType" },
                    id = "Group",
                    name = "Group",
                    endpoint = "/Groups",
                    schema = SchemaGroup,
                },
            },
        });
    }

    private async Task<IReadOnlyCollection<User>> LoadGroupMembersAsync(ScimGroup group, CancellationToken ct)
    {
        var ids = await _db.ScimGroupMemberships.AsNoTracking()
            .Where(m => m.TenantId == group.TenantId && m.GroupId == group.Id)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        return await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == group.TenantId && ids.Contains(u.Id))
            .OrderBy(u => u.Email)
            .ToListAsync(ct);
    }

    private static IEnumerable<Guid> MemberIds(ScimMember[]? members) =>
        members?.Select(m => m.value).Where(v => !string.IsNullOrWhiteSpace(v)).SelectMany(ParseGuid) ?? Enumerable.Empty<Guid>();

    private static IEnumerable<Guid> MemberIds(JsonElement? value)
    {
        if (value is null) return Enumerable.Empty<Guid>();
        var v = value.Value;
        if (v.ValueKind == JsonValueKind.Array)
        {
            return v.EnumerateArray().SelectMany(MemberIdsFromElement).ToArray();
        }
        return MemberIdsFromElement(v).ToArray();
    }

    private static IEnumerable<Guid> MemberIdsFromElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String) return ParseGuid(el.GetString());
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.String ? ParseGuid(value.GetString()) : Enumerable.Empty<Guid>();
        }
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("members", out var members))
        {
            return MemberIds(members);
        }
        return Enumerable.Empty<Guid>();
    }

    private async Task<bool> IsDuplicateGroupDisplayNameAsync(Guid tenantId, Guid groupId, string displayName, CancellationToken ct) =>
        await _db.ScimGroups.AnyAsync(g => g.TenantId == tenantId && g.Id != groupId && g.DisplayName == displayName, ct);

    private static IEnumerable<Guid> ParseGuid(string? value) =>
        Guid.TryParse(value, out var id) ? new[] { id } : Array.Empty<Guid>();

    private static Guid? TryMemberIdFromPath(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            path,
            @"members\[value\s+eq\s+""(?<id>[^""]+)""\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    private async Task ReplaceGroupMembersAsync(Tenant tenant, ScimGroup group, IEnumerable<Guid> requestedIds, CancellationToken ct)
    {
        var requested = await ValidTenantUserIdsAsync(tenant.Id, requestedIds, ct);
        var existing = await _db.ScimGroupMemberships
            .Where(m => m.TenantId == tenant.Id && m.GroupId == group.Id)
            .ToListAsync(ct);
        var affected = existing.Select(m => m.UserId).Concat(requested).Distinct().ToArray();
        _db.ScimGroupMemberships.RemoveRange(existing.Where(m => !requested.Contains(m.UserId)));
        foreach (var userId in requested.Where(id => existing.All(m => m.UserId != id)))
        {
            _db.ScimGroupMemberships.Add(new ScimGroupMembership
            {
                TenantId = tenant.Id,
                GroupId = group.Id,
                UserId = userId,
            });
        }
        await _db.SaveChangesAsync(ct);
        await ProjectRolesAsync(tenant.Id, affected, ct);
    }

    private async Task AddGroupMembersAsync(Tenant tenant, ScimGroup group, IEnumerable<Guid> requestedIds, CancellationToken ct)
    {
        var requested = await ValidTenantUserIdsAsync(tenant.Id, requestedIds, ct);
        var existing = await _db.ScimGroupMemberships
            .Where(m => m.TenantId == tenant.Id && m.GroupId == group.Id)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in requested.Where(id => !existing.Contains(id)))
        {
            _db.ScimGroupMemberships.Add(new ScimGroupMembership
            {
                TenantId = tenant.Id,
                GroupId = group.Id,
                UserId = userId,
            });
        }
        await _db.SaveChangesAsync(ct);
        await ProjectRolesAsync(tenant.Id, requested, ct);
    }

    private async Task RemoveGroupMembersAsync(Tenant tenant, ScimGroup group, IEnumerable<Guid> requestedIds, bool removeAllWhenEmpty, CancellationToken ct)
    {
        var requested = requestedIds.Distinct().ToList();
        var q = _db.ScimGroupMemberships.Where(m => m.TenantId == tenant.Id && m.GroupId == group.Id);
        if (requested.Count > 0) q = q.Where(m => requested.Contains(m.UserId));
        else if (!removeAllWhenEmpty) return;
        var existing = await q.ToListAsync(ct);
        var affected = existing.Select(m => m.UserId).Distinct().ToArray();
        _db.ScimGroupMemberships.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
        await ProjectRolesAsync(tenant.Id, affected, ct);
    }

    private async Task<List<Guid>> ValidTenantUserIdsAsync(Guid tenantId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0) return new List<Guid>();
        return await _db.Users
            .Where(u => u.TenantId == tenantId && distinct.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    private async Task ProjectRolesAsync(Guid tenantId, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var roleMap = ParseRoleMap(settings?.ScimGroupRoleMapJson);
        if (roleMap.Count == 0) return;

        var users = await _db.Users.Where(u => u.TenantId == tenantId && ids.Contains(u.Id)).ToListAsync(ct);
        var memberships = await _db.ScimGroupMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.UserId))
            .ToListAsync(ct);
        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = await _db.ScimGroups.AsNoTracking()
            .Where(g => g.TenantId == tenantId && groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, ct);

        foreach (var user in users)
        {
            var projectedRoles = memberships
                .Where(m => m.UserId == user.Id && groups.TryGetValue(m.GroupId, out var g) && roleMap.ContainsKey(g.DisplayName))
                .Select(m => roleMap[groups[m.GroupId].DisplayName])
                .OrderByDescending(RoleRank)
                .ToList();
            if (projectedRoles.Count == 0)
            {
                if (roleMap.Values.Contains(user.Role) && user.Role != UserRole.Radiologist)
                {
                    user.Role = UserRole.Radiologist;
                    user.UpdatedAt = DateTimeOffset.UtcNow;
                }
                continue;
            }
            user.Role = projectedRoles[0];
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, UserRole> ParseRoleMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, UserRole>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            var mapped = new Dictionary<string, UserRole>(StringComparer.OrdinalIgnoreCase);
            foreach (var (groupName, roleName) in raw)
            {
                if (Enum.TryParse<UserRole>(roleName, ignoreCase: true, out var role)) mapped[groupName] = role;
            }
            return mapped;
        }
        catch
        {
            return new Dictionary<string, UserRole>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static int RoleRank(UserRole role) => role switch
    {
        UserRole.ItAdmin => 60,
        UserRole.MedicalDirector => 50,
        UserRole.ComplianceReviewer => 40,
        UserRole.BillingAdmin => 30,
        UserRole.ReportingAdmin => 20,
        _ => 10,
    };

    private Task AuditGroupAsync(Guid tenantId, Guid groupId, string action, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            Action = AuditAction.ScimGroupChanged,
            DetailsJson = JsonSerializer.Serialize(new { action, groupId }),
        }, ct);

    private static UserRole MapRole(ScimRole[]? roles)
    {
        var v = roles?.FirstOrDefault()?.value ?? "Radiologist";
        return Enum.TryParse<UserRole>(v, ignoreCase: true, out var role) ? role : UserRole.Radiologist;
    }
}
