using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

[ApiController]
[Route("api/copilot/admin")]
public class CopilotAdminController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly CopilotService _copilot;
    private readonly IAuditLog _audit;

    public CopilotAdminController(RadioPadDbContext db, CopilotService copilot, IAuditLog audit)
    {
        _db = db;
        _copilot = copilot;
        _audit = audit;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        return Ok(_copilot.ToDto(settings));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] CopilotSettingsDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        var result = _copilot.Apply(settings, dto);
        if (!result.ok)
            return BadRequest(new { error = result.error, kind = result.kind });

        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotAdminSettingsChanged,
            DetailsJson = JsonSerializer.Serialize(new
            {
                settings.Enabled,
                settings.EmergencyDisabled,
                defaultMode = settings.DefaultMode.ToString(),
                settings.AllowedModes,
                settings.GitHubHost,
                orgConfigured = !string.IsNullOrWhiteSpace(settings.GitHubOrganization),
                githubAppConfigured = !string.IsNullOrWhiteSpace(settings.GitHubAppId),
                oauthConfigured = !string.IsNullOrWhiteSpace(settings.OAuthClientId),
                privateKeySecretConfigured = !string.IsNullOrWhiteSpace(settings.GitHubAppPrivateKeySecretRef),
                oauthSecretConfigured = !string.IsNullOrWhiteSpace(settings.OAuthClientSecretRef),
            }),
        }, ct);
        return Ok(_copilot.ToDto(settings));
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        return Ok(_copilot.Status(settings));
    }

    [HttpPost("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ComplianceReviewer);
        if (deny is not null) return deny;
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        var status = _copilot.Status(settings);
        var run = new CopilotDiagnosticRun
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Status = status.Kind,
            ResultsJson = JsonSerializer.Serialize(new
            {
                status.Kind,
                status.RuntimeStatus,
                status.PhiBlocked,
                settings.SdkRuntimeEnabled,
                settings.CliRuntimeEnabled,
                githubAppConfigured = !string.IsNullOrWhiteSpace(settings.GitHubAppId),
                oauthConfigured = !string.IsNullOrWhiteSpace(settings.OAuthClientId),
                secrets = new
                {
                    appPrivateKey = !string.IsNullOrWhiteSpace(settings.GitHubAppPrivateKeySecretRef),
                    oauthClientSecret = !string.IsNullOrWhiteSpace(settings.OAuthClientSecretRef),
                },
            }),
        };
        _db.CopilotDiagnosticRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotDiagnosticsRun,
            DetailsJson = JsonSerializer.Serialize(new { runId = run.Id, status.Kind, status.RuntimeStatus }),
        }, ct);
        return Ok(new { runId = run.Id, status, results = JsonSerializer.Deserialize<object>(run.ResultsJson) });
    }

    [HttpPost("features/{featureKey}")]
    public async Task<IActionResult> ToggleFeature(string featureKey, [FromBody] CopilotFeatureDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        if (string.IsNullOrWhiteSpace(featureKey) || featureKey.Length > 80)
            return BadRequest(new { error = "featureKey is required and must be <= 80 characters.", kind = "validation" });

        var row = await _db.CopilotFeatureFlags.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.FeatureKey == featureKey, ct);
        if (row is null)
        {
            row = new CopilotFeatureFlag { TenantId = tenant.Id, FeatureKey = featureKey };
            _db.CopilotFeatureFlags.Add(row);
        }
        row.Enabled = dto.Enabled;
        row.RequiredRole = dto.RequiredRole ?? "";
        row.PolicyJson = string.IsNullOrWhiteSpace(dto.PolicyJson) ? "{}" : dto.PolicyJson;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotAdminSettingsChanged,
            DetailsJson = JsonSerializer.Serialize(new { featureKey, row.Enabled, row.RequiredRole }),
        }, ct);
        return Ok(new CopilotFeatureDto(row.FeatureKey, row.Enabled, row.RequiredRole, row.PolicyJson));
    }

    [HttpGet("quotas")]
    public async Task<IActionResult> Quotas(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        return Ok(await _copilot.ListQuotaPoliciesAsync(tenant.Id, ct));
    }

    [HttpPost("quotas")]
    public async Task<IActionResult> SaveQuotas([FromBody] CopilotQuotaPolicyDto[] policies, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        var saved = await _copilot.SaveQuotaPoliciesAsync(tenant.Id, policies, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotAdminSettingsChanged,
            DetailsJson = JsonSerializer.Serialize(new { quotas = saved.Select(q => new { q.ScopeType, q.ScopeKey, q.Feature, q.WindowSeconds, q.MaxRequests, q.MaxConcurrent, q.Enabled }) }),
        }, ct);
        return Ok(saved);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        return Ok(await _copilot.UsageSummaryAsync(tenant.Id, ct));
    }
}

[ApiController]
[Route("api/copilot")]
public class CopilotController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly CopilotService _copilot;
    private readonly IAuditLog _audit;

    public CopilotController(RadioPadDbContext db, CopilotService copilot, IAuditLog audit)
    {
        _db = db;
        _copilot = copilot;
        _audit = audit;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        return Ok(_copilot.Status(settings));
    }

    [HttpGet("account")]
    public async Task<IActionResult> Account(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        return Ok(await _copilot.AccountAsync(tenant.Id, user.Id, settings, ct));
    }

    [HttpGet("entitlement")]
    public async Task<IActionResult> Entitlement(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        return Ok(await _copilot.EntitlementAsync(tenant.Id, user.Id, settings, ct));
    }

    [HttpPost("account/auth/start")]
    public async Task<IActionResult> BeginAuth([FromBody] CopilotAuthStartRequest request, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        var result = await _copilot.BeginAuthAsync(tenant.Id, user.Id, settings, request, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotAccountChanged,
            DetailsJson = JsonSerializer.Serialize(new { action = "auth_start", result.Mode, result.Kind }),
        }, ct);
        return Ok(result);
    }

    [HttpPost("account/local-cli")]
    public async Task<IActionResult> LinkLocalCli([FromBody] CopilotLocalCliAccountRequest request, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        try
        {
            var account = await _copilot.LinkLocalCliAccountAsync(tenant.Id, user.Id, settings, request, ct);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.CopilotAccountChanged,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    action = "local_cli_linked",
                    account.Mode,
                    githubLoginConfigured = !string.IsNullOrWhiteSpace(account.GitHubLogin),
                    account.TokenStatus,
                    account.EntitlementAllowed,
                }),
            }, ct);
            return Ok(account);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { kind = ex.Message, message = "Local CLI account linking is blocked by tenant policy." });
        }
    }

    [HttpDelete("account")]
    public async Task<IActionResult> RevokeAccount(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        await _copilot.RevokeAccountAsync(tenant.Id, user.Id, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotAccountChanged,
            DetailsJson = JsonSerializer.Serialize(new { action = "revoked" }),
        }, ct);
        return NoContent();
    }

    [HttpPost("context/preview")]
    public async Task<IActionResult> ContextPreview([FromBody] CopilotContextPreviewRequest request, CancellationToken ct)
    {
        _ = await ResolveContextAsync(_db, ct);
        return Ok(_copilot.PreviewContext(request));
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromBody] CopilotSessionRequest request, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        var requestId = HttpContext.TraceIdentifier;
        var result = await _copilot.StartSessionAsync(tenant.Id, user.Id, requestId, settings, request, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = result.Error is null ? AuditAction.CopilotRequestLifecycle : AuditAction.CopilotPolicyBlocked,
            DetailsJson = JsonSerializer.Serialize(new
            {
                requestId,
                status = result.Session?.Status ?? "blocked",
                sessionId = result.Session?.SessionId,
                errorKind = result.Error?.Kind,
                inputHash = _copilot.Hash(request.Message),
                contextHash = result.Session?.Context.ContextHash,
            }),
        }, ct);
        if (result.Error is not null) return StatusCode(result.StatusCode, result.Error);
        return StatusCode(result.StatusCode, result.Session);
    }

    [HttpPost("sessions/{sessionId:guid}/cancel")]
    public async Task<IActionResult> CancelSession(Guid sessionId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var session = await _copilot.CancelSessionAsync(tenant.Id, user.Id, sessionId, ct);
        if (session is null) return NotFound(new { kind = "not_found", message = "Copilot session was not found." });
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CopilotRequestLifecycle,
            DetailsJson = JsonSerializer.Serialize(new { action = "cancel", sessionId }),
        }, ct);
        return Ok(session);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] CopilotChatRequest request, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var settings = await _copilot.GetOrCreateSettingsAsync(tenant.Id, ct);
        var requestId = HttpContext.TraceIdentifier;
        var result = await _copilot.StartSessionAsync(
            tenant.Id,
            user.Id,
            requestId,
            settings,
            new CopilotSessionRequest(request.Message, request.Mode, request.ContextKind, null),
            ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = result.Error is null ? AuditAction.CopilotRequestLifecycle : AuditAction.CopilotPolicyBlocked,
            DetailsJson = JsonSerializer.Serialize(new
            {
                requestId,
                kind = result.Error?.Kind,
                runtimeStatus = result.Error?.RuntimeStatus,
                status = result.Session?.Status ?? "blocked",
                sessionId = result.Session?.SessionId,
                feature = request.ContextKind ?? "chat",
                inputHash = _copilot.Hash(request.Message),
            }),
        }, ct);
        if (result.Error is not null) return StatusCode(result.StatusCode, result.Error);
        return Ok(result.Session);
    }
}
