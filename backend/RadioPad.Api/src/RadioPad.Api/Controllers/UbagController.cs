using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Ubag;

namespace RadioPad.Api.Controllers;

[ApiController]
[Route("api/ubag")]
public class UbagController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IUbagClient _ubag;
    private readonly IAuditLog _audit;

    public UbagController(RadioPadDbContext db, IUbagClient ubag, IAuditLog audit)
    {
        _db = db;
        _ubag = ubag;
        _audit = audit;
    }

    public record UbagStatusDto(
        UbagHealth Health,
        UbagBrowserSummary Browser,
        IReadOnlyList<UbagTarget> Targets,
        IReadOnlyList<string> AllowedTargets,
        IReadOnlyList<string> OrderedTargets);

    public record SubmitJobDto(string Target, string Prompt);
    public record RunWorkflowDto(string Prompt, string? Name = null);

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ComplianceReviewer);
        if (deny is not null) return deny;

        var health = await _ubag.GetHealthAsync(ct);
        var browser = await _ubag.GetBrowserSummaryAsync(ct);
        var targets = await _ubag.ListTargetsAsync(ct);
        var contexts = await _ubag.ListBrowserContextsAsync(ct);
        // Derive per-target readiness from browser contexts (login_state == "authenticated").
        // /v1/targets carries no readiness field; the source of truth is /v1/browser/contexts.
        var mergedTargets = targets
            .Select(t =>
            {
                var ctx = contexts.FirstOrDefault(c => string.Equals(c.TargetId, t.Id, StringComparison.OrdinalIgnoreCase));
                var ready = UbagProviderAdapter.IsTargetReady(t.Id, contexts);
                var status = ctx is not null ? ctx.LoginState : t.Status;
                return t with { Ready = ready, Status = status };
            })
            .ToList();
        return Ok(new UbagStatusDto(
            health,
            browser,
            mergedTargets,
            UbagProviderAdapter.ResolveAllowedTargets(),
            OrderedTargets()));
    }

    [HttpPost("jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> SubmitJob([FromBody] SubmitJobDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var validation = ValidatePrompt(dto.Prompt, dto.Target);
        if (validation is not null) return validation;

        var key = $"radiopad-ubag-job-{tenant.Id:N}-{Hash(dto.Target + "|" + dto.Prompt)[..16]}";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var job = await _ubag.CreateJobAsync(new UbagJobRequest(dto.Target.Trim(), dto.Prompt.Trim(), ClientRequestId: key), key, ct);
        sw.Stop();
        await AuditUbagAsync(tenant.Id, user.Id, "job_created", dto.Target.Trim(), job.Id, null, dto.Prompt, job.Output, job.Status, sw.ElapsedMilliseconds, ct);
        return Ok(job);
    }

    [HttpPost("workflows/ordered-web-chain")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> RunOrderedWorkflow([FromBody] RunWorkflowDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var validation = ValidatePrompt(dto.Prompt, "ordered:web-chain");
        if (validation is not null) return validation;

        var targets = OrderedTargets();
        var allowed = UbagProviderAdapter.ResolveAllowedTargets();
        var blocked = targets.Where(t => !allowed.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (blocked.Length > 0)
            return BadRequest(new { error = "Ordered workflow contains targets outside RADIOPAD_UBAG_ALLOWED_TARGETS.", kind = "target_not_allowed", targets = blocked });

        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? $"RadioPad ordered web chain {DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : dto.Name.Trim();
        var key = $"radiopad-ubag-workflow-{tenant.Id:N}-{Hash(name + "|" + dto.Prompt)[..16]}";
        var workflow = await _ubag.CreateWorkflowAsync(new UbagWorkflowRequest(
            name,
            targets.Select((target, index) => new UbagWorkflowStep($"step_{index + 1}_{target}", target, dto.Prompt.Trim())).ToArray(),
            ClientRequestId: key), key, ct);
        var run = await _ubag.RunWorkflowAsync(workflow.Id, $"{key}-run", ct);
        await AuditUbagAsync(tenant.Id, user.Id, "workflow_run_started", "ordered:web-chain", null, run.Id, dto.Prompt, run.Output, run.Status, null, ct);
        return Ok(new { workflow, run, orderedTargets = targets });
    }

    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(string id, CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ComplianceReviewer);
        if (deny is not null) return deny;
        return Ok(await _ubag.GetJobAsync(id, ct));
    }

    [HttpGet("workflows/runs/{id}")]
    public async Task<IActionResult> GetWorkflowRun(string id, CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ComplianceReviewer);
        if (deny is not null) return deny;
        return Ok(await _ubag.GetWorkflowRunAsync(id, ct));
    }

    private IActionResult? ValidatePrompt(string? prompt, string? target)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { error = "Prompt is required.", kind = "validation" });
        if (prompt.Length > 20_000)
            return BadRequest(new { error = "Prompt must be 20,000 characters or fewer.", kind = "validation" });
        if (!string.Equals(target, "ordered:web-chain", StringComparison.OrdinalIgnoreCase)
            && !UbagProviderAdapter.ResolveAllowedTargets().Contains(target ?? "", StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Target is not allowed.", kind = "target_not_allowed" });
        if (LooksLikeSecret(prompt))
            return Conflict(new { error = "UBAG prompts must not contain secrets.", kind = "secret_not_supported" });
        if (LooksLikePhi(prompt))
            return Conflict(new { error = "UBAG is configured for non-PHI prompts only. De-identify before sending.", kind = "phi_not_supported" });
        return null;
    }

    private async Task AuditUbagAsync(
        Guid tenantId,
        Guid userId,
        string eventType,
        string target,
        string? jobId,
        string? runId,
        string input,
        string? output,
        string status,
        long? latencyMs,
        CancellationToken ct)
    {
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.AiResponse,
            DetailsJson = JsonSerializer.Serialize(new
            {
                eventType,
                provider = "UBAG",
                adapter = UbagProviderAdapter.AdapterId,
                target,
                ubagJobId = jobId,
                ubagRunId = runId,
                containsPhi = false,
                inputHash = Hash(input),
                outputHash = Hash(output ?? ""),
                status,
                latencyMs,
            }),
        }, ct);
    }

    private static IReadOnlyList<string> OrderedTargets()
    {
        // Configurable ordered web-chain. Defaults to the targets that are logged in
        // (gemini_web, deepseek_web); chatgpt_web is excluded unless explicitly enabled.
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_ORDERED_TARGETS");
        if (string.IsNullOrWhiteSpace(raw)) raw = "gemini_web,deepseek_web";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool LooksLikeSecret(string? value)
    {
        var v = value ?? string.Empty;
        return v.Contains("ghp_", StringComparison.Ordinal)
            || v.Contains("github_pat_", StringComparison.Ordinal)
            || v.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)
            || v.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || v.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || v.Contains("-----BEGIN", StringComparison.Ordinal);
    }

    private static bool LooksLikePhi(string? value)
    {
        var v = value ?? string.Empty;
        return v.Contains("patient name", StringComparison.OrdinalIgnoreCase)
            || v.Contains("mrn", StringComparison.OrdinalIgnoreCase)
            || v.Contains("date of birth", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(v, @"\b\d{2}/\d{2}/\d{4}\b");
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
