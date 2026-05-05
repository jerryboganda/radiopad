using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-31/Iter-32 MCP-001..007 — administrative registry for Model Context
/// Protocol tools. Lifecycle: Submitted → Approved → Blocked. RBAC:
/// ReportingAdmin / MedicalDirector / ItAdmin can register; only
/// MedicalDirector / ItAdmin can approve/block. Default-deny applies to any
/// scope token starting with <c>shell:</c>, <c>fs:</c>, or <c>net:</c>.
/// </summary>
[ApiController]
[Route("api/mcp/tools")]
public class McpToolRegistryController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IMcpSandbox _sandbox;
    private readonly IMcpScopePolicy _scopePolicy;
    private readonly McpInvocationService _invocations;

    public McpToolRegistryController(
        RadioPadDbContext db,
        IAuditLog audit,
        IMcpSandbox sandbox,
        IMcpScopePolicy scopePolicy,
        McpInvocationService invocations)
    {
        _db = db;
        _audit = audit;
        _sandbox = sandbox;
        _scopePolicy = scopePolicy;
        _invocations = invocations;
    }

    public record ToolDto(
        Guid Id,
        string Name,
        string Version,
        McpToolKind Kind,
        bool IsBuiltIn,
        McpToolScope Scope,
        string ScopeString,
        McpToolStatus Status,
        bool Approved,
        Guid? ApprovedBy,
        DateTimeOffset? ApprovedAt,
        string ManifestSha256,
        bool ManifestSigned,
        IReadOnlyList<string> AllowedConnectorPaths,
        DateTimeOffset CreatedAt);

    private static ToolDto ToDto(McpTool t) => new(
        t.Id, t.Name, t.Version, t.Kind, t.IsBuiltIn, t.Scope, t.ScopeString,
        t.Status, t.Approved, t.ApprovedBy, t.ApprovedAt,
        t.ManifestSha256, !string.IsNullOrEmpty(t.ManifestSig),
        SplitConnectors(t.AllowedConnectorPaths), t.CreatedAt);

    private static IReadOnlyList<string> SplitConnectors(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rows = await _db.McpTools.AsNoTracking()
            .Where(t => t.TenantId == tenant.Id)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var tool = await _db.McpTools.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();
        return Ok(ToDto(tool));
    }

    public record RegisterDto(
        string Name,
        string? Version,
        McpToolKind? Kind,
        McpToolScope? Scope,
        string? ScopeString,
        bool? IsBuiltIn,
        string? ManifestJson,
        string? ManifestSig,
        IReadOnlyList<string>? AllowedConnectorPaths);

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { kind = "validation", error = "name is required." });

        var existing = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Name == dto.Name, ct);
        if (existing is not null)
            return Conflict(new { kind = "conflict", error = "Tool name already registered for this tenant." });

        var manifest = dto.ManifestJson ?? "";
        var sha = string.IsNullOrEmpty(manifest)
            ? ""
            : McpManifestVerifier.ComputeSha256(Encoding.UTF8.GetBytes(manifest));

        var tool = new McpTool
        {
            TenantId = tenant.Id,
            Name = dto.Name.Trim(),
            Version = string.IsNullOrWhiteSpace(dto.Version) ? "1.0.0" : dto.Version.Trim(),
            Kind = dto.Kind ?? McpToolKind.BuiltIn,
            Scope = dto.Scope ?? McpToolScope.ReadOnly,
            ScopeString = dto.ScopeString?.Trim() ?? "",
            IsBuiltIn = dto.IsBuiltIn ?? false,
            ManifestJson = manifest,
            ManifestSha256 = sha,
            ManifestSig = dto.ManifestSig ?? "",
            Status = McpToolStatus.Submitted,
            Approved = false,
            AllowedConnectorPaths = string.Join("\n", dto.AllowedConnectorPaths ?? Array.Empty<string>()),
        };
        _db.McpTools.Add(tool);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.McpToolRegistered,
            DetailsJson = JsonSerializer.Serialize(new
            {
                toolId = tool.Id,
                name = tool.Name,
                version = tool.Version,
                scope = tool.ScopeString,
                manifestSha256 = sha,
                signed = !string.IsNullOrEmpty(tool.ManifestSig),
            }),
        }, ct);

        return Ok(ToDto(tool));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();
        tool.Status = McpToolStatus.Approved;
        tool.Approved = true;
        tool.ApprovedBy = user.Id;
        tool.ApprovedAt = DateTimeOffset.UtcNow;
        tool.UpdatedAt = tool.ApprovedAt.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.McpToolApproved,
            DetailsJson = JsonSerializer.Serialize(new { toolId = tool.Id, name = tool.Name, scope = tool.ScopeString }),
        }, ct);
        return Ok(ToDto(tool));
    }

    public record BlockDto(string? Reason);

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id, [FromBody] BlockDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();
        tool.Status = McpToolStatus.Blocked;
        tool.Approved = false;
        tool.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.McpToolBlocked,
            DetailsJson = JsonSerializer.Serialize(new { toolId = tool.Id, name = tool.Name, reason = dto?.Reason ?? "manual" }),
        }, ct);
        return Ok(ToDto(tool));
    }

    /// <summary>Back-compat alias for <c>/block</c>: writes
    /// <see cref="AuditAction.McpToolRevoked"/> instead of
    /// <see cref="AuditAction.McpToolBlocked"/> so existing dashboards keep working.</summary>
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();
        tool.Status = McpToolStatus.Blocked;
        tool.Approved = false;
        tool.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.McpToolRevoked,
            DetailsJson = JsonSerializer.Serialize(new { toolId = tool.Id, name = tool.Name }),
        }, ct);
        return Ok(ToDto(tool));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();
        _db.McpTools.Remove(tool);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.McpToolBlocked,
            DetailsJson = JsonSerializer.Serialize(new { toolId = tool.Id, name = tool.Name, reason = "deleted" }),
        }, ct);
        return NoContent();
    }

    public record InvokeDto(string InputJson, string? OutputJson, Guid? ReportId);

    /// <summary>
    /// Records a tool invocation. For BuiltIn tools the caller has already
    /// produced an output and supplies it for hashing. For Custom tools the
    /// handler routes through <see cref="IMcpSandbox"/> with a 5-second hard
    /// timeout. Always passes through <see cref="IMcpScopePolicy"/> first.
    /// </summary>
    [HttpPost("{id:guid}/invoke")]
    public async Task<IActionResult> Invoke(Guid id, [FromBody] InvokeDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var scopeAllowDangerous = settings?.AllowDangerousMcp ?? false;

        // MCP-005 — default-deny for dangerous-scope tokens.
        var scopeDecision = _scopePolicy.Evaluate(tool.ScopeString, scopeAllowDangerous);
        if (!scopeDecision.Allowed)
        {
            await _invocations.RecordPolicyBlockAsync(tenant, user, tool, scopeDecision.DangerousTokens, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                type = "https://radiopad.dev/errors/mcp-scope-blocked",
                title = "MCP scope blocked",
                status = 403,
                kind = "mcp_scope_blocked",
                reason = scopeDecision.Reason,
                offendingTokens = scopeDecision.DangerousTokens,
            });
        }

        // Lifecycle gate: only Approved tools may be invoked.
        if (tool.Status != McpToolStatus.Approved)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                type = "https://radiopad.dev/errors/mcp-blocked",
                title = "MCP tool not approved",
                status = 403,
                kind = "mcp_blocked",
                reason = tool.Status == McpToolStatus.Blocked ? "blocked" : "not_approved",
            });
        }

        // External-scope (legacy enum) tools also require Tenant.AllowExternalMcp.
        if (tool.Scope == McpToolScope.External && !tenant.AllowExternalMcp)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                kind = "mcp_blocked",
                reason = "external_default_deny",
            });
        }

        var input = dto.InputJson ?? "";
        var result = await _invocations.RunAsync(
            tenant, user, tool, dto.ReportId, input,
            innerCt =>
            {
                if (tool.Kind == McpToolKind.Custom)
                {
                    return _sandbox.InvokeAsync(
                        tool.Name,
                        input,
                        SplitConnectors(tool.AllowedConnectorPaths),
                        TimeSpan.FromSeconds(5),
                        innerCt);
                }
                return Task.FromResult(dto.OutputJson ?? "");
            },
            ct);

        if (result.Status == "timeout")
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                kind = "mcp_timeout",
                reason = "sandbox_timeout",
                inputHash = result.InputHash,
                outputHash = result.OutputHash,
                latencyMs = result.LatencyMs,
            });
        }

        return Ok(new
        {
            status = result.Status,
            inputHash = result.InputHash,
            outputHash = result.OutputHash,
            latencyMs = result.LatencyMs,
        });
    }

    /// <summary>
    /// Iter-32 MCP-006 — sandboxed test-run for an admin to validate a tool.
    /// Runs the in-process sandbox body with a hard 5-second wall, soft
    /// 256 MiB memory cap, and BelowNormal thread priority. Network access
    /// is denied unless the scope explicitly contains <c>net:</c>. Returns
    /// the captured output.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, [FromBody] InvokeDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin, UserRole.ReportingAdmin);
        if (deny is not null) return deny;

        var tool = await _db.McpTools.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.Id == id, ct);
        if (tool is null) return NotFound();

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var scopeAllowDangerous = settings?.AllowDangerousMcp ?? false;
        var scopeDecision = _scopePolicy.Evaluate(tool.ScopeString, scopeAllowDangerous);
        if (!scopeDecision.Allowed)
        {
            await _invocations.RecordPolicyBlockAsync(tenant, user, tool, scopeDecision.DangerousTokens, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                kind = "mcp_scope_blocked",
                reason = scopeDecision.Reason,
                offendingTokens = scopeDecision.DangerousTokens,
            });
        }

        var sandboxResult = await SandboxRunner.RunAsync(
            toolName: tool.Name,
            inputJson: dto.InputJson ?? "",
            wall: TimeSpan.FromSeconds(5),
            memoryBytes: 256L * 1024 * 1024,
            sandbox: _sandbox,
            allowedConnectors: SplitConnectors(tool.AllowedConnectorPaths),
            ct: ct);

        return Ok(new
        {
            status = sandboxResult.Status,
            output = sandboxResult.Output,
            latencyMs = sandboxResult.LatencyMs,
            memoryBytes = sandboxResult.PeakMemoryBytes,
        });
    }
}

/// <summary>
/// Iter-32 MCP-006 — sandbox runner. Enforces wall-clock + soft memory caps
/// and lowers thread priority while the sandbox body runs.
/// </summary>
internal static class SandboxRunner
{
    public sealed record Result(string Status, string Output, int LatencyMs, long PeakMemoryBytes);

    public static async Task<Result> RunAsync(
        string toolName,
        string inputJson,
        TimeSpan wall,
        long memoryBytes,
        IMcpSandbox sandbox,
        IReadOnlyList<string> allowedConnectors,
        CancellationToken ct)
    {
        var prevPriority = System.Threading.Thread.CurrentThread.Priority;
        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var startMem = GC.GetTotalMemory(false);
        var status = "ok";
        var output = "";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(wall);
            output = await sandbox.InvokeAsync(toolName, inputJson, allowedConnectors, wall, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            status = "timeout";
        }
        catch (McpSandboxException)
        {
            status = "error";
        }
        catch (Exception)
        {
            status = "error";
        }
        finally
        {
            System.Threading.Thread.CurrentThread.Priority = prevPriority;
        }
        sw.Stop();
        var endMem = GC.GetTotalMemory(false);
        var peak = Math.Max(0, endMem - startMem);
        if (peak > memoryBytes && status == "ok") status = "memory_exceeded";
        return new Result(status, output, (int)sw.ElapsedMilliseconds, peak);
    }
}
