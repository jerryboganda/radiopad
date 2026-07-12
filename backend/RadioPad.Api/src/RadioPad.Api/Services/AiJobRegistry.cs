using System.Collections.Concurrent;

namespace RadioPad.Api.Services;

/// <summary>
/// In-memory registry for asynchronous report-AI jobs (impression/rewrite modes
/// and whole-report generation). The synchronous <c>POST /reports/{id}/ai</c> and
/// <c>/generate</c> endpoints hold the HTTP request open for the whole provider
/// call — minutes for UBAG browser-driven targets — which any proxy timeout or
/// the webview's ~300s no-response kill turns into a raw "Failed to fetch",
/// and the aborted request cancels the in-flight job via
/// <c>HttpContext.RequestAborted</c> (prod incident 2026-07-12). The job pattern
/// decouples generation from the connection: submit returns an id immediately,
/// the client polls with fast requests, and a dropped tab no longer cancels
/// the run.
///
/// Deliberately in-memory (single-container deployment, jobs live for minutes):
/// a restart forgets running jobs, and the poll endpoint's 404 tells the client
/// to re-submit. Mirrors the UBAG gateway's own job semantics that
/// <c>UbagProviderAdapter</c> consumes.
/// </summary>
public sealed class AiJobRegistry
{
    /// <summary>Immutable job snapshot; terminal transitions swap the whole record.</summary>
    public sealed record AiJobState(
        Guid Id,
        Guid TenantId,
        Guid ReportId,
        Guid UserId,
        string Kind,           // "ai" | "generate"
        string Mode,           // ai mode, or "generate"
        DateTimeOffset CreatedAt,
        string Status,         // "running" | "ok" | "error"
        object? Payload,       // endpoint-shaped result object when Status == "ok"
        string? Error,
        string? ErrorKind,
        DateTimeOffset? CompletedAt);

    private const int MaxJobs = 500;
    private static readonly TimeSpan TerminalRetention = TimeSpan.FromMinutes(15);
    // A running job is only evicted after this window — a backstop against
    // leaked entries if a background task dies without reporting (should not
    // happen; ExecuteAsync catches everything).
    private static readonly TimeSpan RunningRetention = TimeSpan.FromHours(2);
    // Cap eviction never removes a job that completed within this floor: its
    // poller (2s cadence) has not had a fair chance to read the result yet,
    // and losing a completed generate job invites a duplicate re-run on top
    // of the already-committed DB write.
    private static readonly TimeSpan CapEvictionFloor = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, AiJobState> _jobs = new();

    public AiJobState Create(Guid tenantId, Guid reportId, Guid userId, string kind, string mode)
    {
        Evict();
        var job = new AiJobState(
            Guid.NewGuid(), tenantId, reportId, userId, kind, mode,
            DateTimeOffset.UtcNow, "running", null, null, null, null);
        _jobs[job.Id] = job;
        return job;
    }

    public bool TryGet(Guid id, out AiJobState job) => _jobs.TryGetValue(id, out job!);

    /// <summary>
    /// Single-flight lookup: the running job for (tenant, report, kind, mode),
    /// if any. Submit endpoints return this instead of stacking a second
    /// concurrent generation onto the same report — overlapping detached jobs
    /// would interleave section writes and duplicate ReportVersion sequences.
    /// </summary>
    public bool TryGetRunning(Guid tenantId, Guid reportId, string kind, string mode, out AiJobState job)
    {
        job = _jobs.Values.FirstOrDefault(j =>
            j.Status == "running" && j.TenantId == tenantId && j.ReportId == reportId
            && j.Kind == kind && j.Mode == mode)!;
        return job is not null;
    }

    public void Complete(Guid id, object payload) => Terminal(id, "ok", payload, null, null);

    public void Fail(Guid id, string error, string errorKind) => Terminal(id, "error", null, error, errorKind);

    private void Terminal(Guid id, string status, object? payload, string? error, string? kind)
    {
        // Only running → terminal is legal; a second completion (e.g. safety
        // timeout racing the real result) keeps the first outcome.
        if (!_jobs.TryGetValue(id, out var cur) || cur.Status != "running") return;
        var next = cur with { Status = status, Payload = payload, Error = error, ErrorKind = kind, CompletedAt = DateTimeOffset.UtcNow };
        _jobs.TryUpdate(id, next, cur);
    }

    /// <summary>Lazy eviction on submit: drop old terminal jobs, cap the table.</summary>
    private void Evict()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, job) in _jobs)
        {
            var expired = job.Status == "running"
                ? now - job.CreatedAt > RunningRetention
                : job.CompletedAt is { } done && now - done > TerminalRetention;
            if (expired) _jobs.TryRemove(id, out _);
        }
        if (_jobs.Count <= MaxJobs) return;
        // Soft cap: evict oldest-COMPLETED first (CreatedAt would target exactly
        // the long-running job that just finished), and never inside the floor —
        // briefly exceeding MaxJobs is cheaper than losing a result an active
        // poller is about to read.
        foreach (var job in _jobs.Values
            .Where(j => j.Status != "running" && j.CompletedAt is { } done && now - done > CapEvictionFloor)
            .OrderBy(j => j.CompletedAt)
            .Take(_jobs.Count - MaxJobs))
        {
            _jobs.TryRemove(job.Id, out _);
        }
    }
}
