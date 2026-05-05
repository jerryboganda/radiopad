using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-32 MCP-004 — every MCP tool invocation goes through this service so
/// the (tenant, user, study/report ref, tool, scope, inputHash, outputHash,
/// latencyMs, status) tuple is recorded once, in one place, append-only.
/// Bodies are SHA-256-hashed before persistence; the audit log NEVER carries
/// the raw input or output.
/// </summary>
public sealed class McpInvocationService
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public McpInvocationService(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    public sealed record InvocationResult(string Status, string InputHash, string OutputHash, int LatencyMs);

    /// <summary>
    /// Run <paramref name="invoke"/> and persist a single
    /// <see cref="McpToolCall"/> + <see cref="AuditAction.McpToolCalled"/>
    /// row. The delegate may throw — this method translates timeouts into
    /// <c>status = "timeout"</c> and any other exception into
    /// <c>status = "error"</c>; both still produce a ledger row.
    /// </summary>
    public async Task<InvocationResult> RunAsync(
        Tenant tenant,
        User user,
        McpTool tool,
        Guid? reportId,
        string inputJson,
        Func<CancellationToken, Task<string>> invoke,
        CancellationToken ct)
    {
        var inputHash = Sha256(inputJson ?? "");
        var status = "ok";
        var output = "";
        var sw = Stopwatch.StartNew();
        try
        {
            output = await invoke(ct).ConfigureAwait(false) ?? "";
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
        sw.Stop();
        var outputHash = Sha256(output);

        _db.McpToolCalls.Add(new McpToolCall
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ToolId = tool.Id,
            ReportId = reportId,
            ToolName = tool.Name,
            ScopeString = tool.ScopeString,
            InputHash = inputHash,
            OutputHash = outputHash,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Status = status,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var details = JsonSerializer.Serialize(new
        {
            toolId = tool.Id,
            toolName = tool.Name,
            scope = tool.ScopeString,
            status,
            inputHash,
            outputHash,
            latencyMs = (int)sw.ElapsedMilliseconds,
        });
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = reportId,
            Action = AuditAction.McpToolCalled,
            DetailsJson = details,
        }, ct).ConfigureAwait(false);

        return new InvocationResult(status, inputHash, outputHash, (int)sw.ElapsedMilliseconds);
    }

    public async Task RecordPolicyBlockAsync(
        Tenant tenant,
        User user,
        McpTool tool,
        IReadOnlyList<string> offendingTokens,
        CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new
        {
            toolId = tool.Id,
            toolName = tool.Name,
            scope = tool.ScopeString,
            reason = "mcp_scope",
            offendingTokens,
        });
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PolicyViolation,
            DetailsJson = details,
        }, ct).ConfigureAwait(false);
    }

    public static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
