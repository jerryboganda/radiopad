using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Phase 2 of the durable async AI-job platform — the user-facing, report-agnostic
/// surface over the <c>AiJobs</c> table. Where the report-scoped endpoints on
/// <see cref="ReportsController"/> own submit + poll for a single report, this
/// controller powers the top-right jobs widget: the caller's own job list, a single
/// job's detail (incl. its result), and the cancel/retry lifecycle actions.
///
/// <para>Every action gates on <see cref="RbacPermission.ReportsEdit"/> — the same
/// permission the submit endpoints require, since cancel/retry are edit-class actions
/// and the list is a working-set view. Errors use the shared problem shape
/// <c>{ error, kind }</c> that the rest of the AI-job endpoints emit.</para>
/// </summary>
[ApiController]
[Route("api/jobs")]
public class JobsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly AiJobCoordinator _coordinator;
    private readonly IAuditLog _audit;

    public JobsController(RadioPadDbContext db, AiJobCoordinator coordinator, IAuditLog audit)
    {
        _db = db;
        _coordinator = coordinator;
        _audit = audit;
    }

    /// <summary>The widget descriptor lifted from the job's report — accession +
    /// modality/body-part for the row label, and status for the badge.</summary>
    public sealed record JobReportDescriptor(string Accession, string Modality, string BodyPart, ReportStatus Status);

    /// <summary>
    /// The caller's OWN async AI jobs, newest first. Deliberately scoped to
    /// <c>UserId == user.Id</c> (not the whole tenant) — this is the signed-in
    /// radiologist's working set, the source of truth the jobs widget rehydrates from.
    ///
    /// <para>When <paramref name="active"/> is true the result is trimmed to what the
    /// widget still cares about: everything queued/running plus terminal rows that
    /// completed in the last 24 h (older terminal rows are noise). When it is
    /// omitted/false the newest <paramref name="limit"/> rows are returned with no time
    /// filter. Rows carry no <c>result</c> payload — the list stays light; fetch
    /// <see cref="Get"/> for a single job's result.</para>
    ///
    /// <para>Like the report-scoped poll endpoint, this is a poll-adjacent read and is
    /// deliberately NOT rate-limited by the "ai" policy — it must never consume the AI
    /// submission budget.</para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool active = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        limit = Math.Clamp(limit, 1, 200);

        var query = _db.AiJobs.Where(j => j.TenantId == tenant.Id && j.UserId == user.Id);
        if (active)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            query = query.Where(j =>
                j.Status == "queued" || j.Status == "running" || j.CompletedAt > cutoff);
        }

        var rows = await query
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // Resolve the report descriptors in one tenant-scoped batch, then stitch in
        // memory — a job whose report was deleted still surfaces with a null descriptor
        // rather than dropping out of the list.
        var reportIds = rows.Select(j => j.ReportId).Distinct().ToList();
        var descriptors = await _db.Reports
            .Where(r => r.TenantId == tenant.Id && reportIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Study.AccessionNumber, r.Study.Modality, r.Study.BodyPart, r.Status })
            .ToListAsync(ct);
        var byReport = descriptors.ToDictionary(
            x => x.Id,
            x => new JobReportDescriptor(x.AccessionNumber, x.Modality, x.BodyPart, x.Status));

        var jobs = rows.Select(j => new
        {
            jobId = j.Id,
            kind = j.Kind,
            mode = j.Mode,
            status = j.Status,
            errorKind = j.ErrorKind,
            error = j.Error,
            attempt = j.Attempt,
            retryOfJobId = j.RetryOfJobId,
            reportId = j.ReportId,
            report = byReport.TryGetValue(j.ReportId, out var d) ? d : null,
            createdAt = j.CreatedAt,
            startedAt = j.StartedAt,
            completedAt = j.CompletedAt,
            elapsedMs = Elapsed(j.CreatedAt, j.CompletedAt),
        }).ToList();

        return Ok(new { jobs });
    }

    /// <summary>
    /// A single job's detail — the same row shape as <see cref="List"/> plus its
    /// <c>result</c>. Tenant-scoped but NOT restricted to the job's creator: any
    /// <see cref="RbacPermission.ReportsEdit"/> user in the tenant may view it (a
    /// colleague picking up a report). The <c>result</c> is the endpoint-shaped payload
    /// for a completed <c>ai</c>-kind job; it is null for a <c>generate</c> job (the
    /// report row IS the result — the widget knows to refetch the report there) and for
    /// any non-terminal or failed job.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        var row = await _db.AiJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenant.Id, ct);
        if (row is null)
            return NotFound(new { error = "AI job not found.", kind = "job_not_found" });

        var rd = await _db.Reports
            .Where(r => r.Id == row.ReportId && r.TenantId == tenant.Id)
            .Select(r => new { r.Study.AccessionNumber, r.Study.Modality, r.Study.BodyPart, r.Status })
            .FirstOrDefaultAsync(ct);
        var report = rd is null
            ? null
            : new JobReportDescriptor(rd.AccessionNumber, rd.Modality, rd.BodyPart, rd.Status);

        object? result = null;
        if (row.Status == "ok" && !string.IsNullOrWhiteSpace(row.ResultJson))
            result = JsonSerializer.Deserialize<JsonElement>(row.ResultJson);

        return Ok(new
        {
            jobId = row.Id,
            kind = row.Kind,
            mode = row.Mode,
            status = row.Status,
            errorKind = row.ErrorKind,
            error = row.Error,
            attempt = row.Attempt,
            retryOfJobId = row.RetryOfJobId,
            reportId = row.ReportId,
            report,
            createdAt = row.CreatedAt,
            startedAt = row.StartedAt,
            completedAt = row.CompletedAt,
            elapsedMs = Elapsed(row.CreatedAt, row.CompletedAt),
            result,
        });
    }

    /// <summary>
    /// Requests cancellation of a queued or running job. Lookup-first: an unknown id is
    /// a 404 before the coordinator is touched. A queued job is cancelled immediately
    /// (terminal, zero provider cost) → <c>200 { status: "cancelled" }</c>; a running
    /// job has its cancel flag set and CTS fired but is not yet actually stopped →
    /// <c>202 { status: "running", cancelRequested: true }</c>. An already-terminal job
    /// is an idempotent <c>200 { status: &lt;current&gt; }</c> with no audit row.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        // Lookup first only for the 404 case and to capture the ReportId for the
        // audit row — NOT to decide the response. The response below is built
        // ENTIRELY from RequestCancelAsync's return value: RunAsync can flip
        // queued→running concurrently with this request, and answering from a
        // status read here (before the call) could tell the radiologist "cancelled"
        // for a job that is, in fact, still running. See the doc comment on
        // AiJobCoordinator.RequestCancelAsync for how the two race safely.
        var row = await _db.AiJobs.FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenant.Id, ct);
        if (row is null)
            return NotFound(new { error = "AI job not found.", kind = "job_not_found" });
        var reportId = row.ReportId;

        var (changed, status) = await _coordinator.RequestCancelAsync(tenant.Id, id, ct);

        if (!changed)
        {
            // Either already terminal when the call ran (idempotent 200, no audit
            // row for a no-op) or it vanished between our lookup and the call.
            if (status == "not_found")
                return NotFound(new { error = "AI job not found.", kind = "job_not_found" });
            return Ok(new { status });
        }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = reportId,
            Action = AuditAction.AiJobCancelled,
            DetailsJson = JsonSerializer.Serialize(new { jobId = id, resultingStatus = status }),
        }, ct);

        // A queued job is now terminal (cancelled) — say so, not "running". A running
        // job is only *requested* to cancel; it may take a moment to actually stop.
        if (status == "cancelled")
            return Ok(new { status = "cancelled" });

        return Accepted(new { status = "running", cancelRequested = true });
    }

    /// <summary>
    /// Retries a failed or cancelled job. Rate-limited under the "ai" policy — a retry
    /// submits real provider work, so it draws on the same budget as a fresh submit.
    /// The coordinator re-runs a NEW row (Attempt+1, RetryOfJobId set) and re-validates
    /// gating so a since-disabled regulated feature can't be walked around via retry.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        try
        {
            var newId = await _coordinator.RetryAsync(tenant, user, id, ct);

            var reportId = await _db.AiJobs
                .Where(j => j.Id == newId)
                .Select(j => j.ReportId)
                .FirstOrDefaultAsync(ct);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                ReportId = reportId,
                Action = AuditAction.AiJobRetried,
                DetailsJson = JsonSerializer.Serialize(new { jobId = newId, retryOfJobId = id }),
            }, ct);

            return Accepted(new { jobId = newId });
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "job_not_found")
        {
            return NotFound(new { error = "AI job not found.", kind = "job_not_found" });
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "job_not_retryable")
        {
            return Conflict(new
            {
                error = "Only failed or cancelled jobs can be retried.",
                kind = "job_not_retryable",
            });
        }
        // Gating is re-applied inside RetryAsync (it must not drift from submit). Map its
        // typed failures to the same shapes the submit endpoints emit rather than leaking
        // a raw 500 to the client.
        catch (InvalidOperationException ioe) when (ioe.Message == "regulated_feature_disabled")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "This AI capability is switched off for this organisation.",
                kind = "regulated_feature_disabled",
            });
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "provider_not_found")
        {
            return BadRequest(new { error = "Provider not found.", kind = "not_found" });
        }
    }

    private static long Elapsed(DateTimeOffset created, DateTimeOffset? completed) =>
        (long)((completed ?? DateTimeOffset.UtcNow) - created).TotalMilliseconds;
}
