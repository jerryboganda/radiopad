using System.Collections.Concurrent;
using System.Text;

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
/// Deliberately in-memory (this hot cache only, not the system as a whole):
/// it serves the ~2s poll cadence from memory so a poll never touches the
/// database. <c>AiJobCoordinator</c> (RadioPad.Api/Services) write-throughs
/// every transition to the durable <c>AiJobs</c> table, so a restart no
/// longer forgets a job outright — the boot recovery sweep marks orphaned
/// rows failed (errorKind "server_restart") and the poll endpoint falls back
/// to the DB on a registry miss. The desktop sidecar's local-generation jobs
/// are the one path with no DB behind them at all (its SQLite is throwaway
/// by doctrine) — those really do vanish on a sidecar restart, by design.
/// Mirrors the UBAG gateway's own job semantics that <c>UbagProviderAdapter</c>
/// consumes.
/// </summary>
public sealed class AiJobRegistry
{
    /// <summary>Immutable job snapshot; terminal transitions swap the whole record.</summary>
    public sealed record AiJobState(
        Guid Id,
        Guid TenantId,
        Guid ReportId,
        Guid UserId,
        string Kind,           // "ai" | "generate" | "local-generate"
        string Mode,           // ai mode, "generate", or "report" (local-generate)
        DateTimeOffset CreatedAt,
        string Status,         // "running" | "ok" | "error" | "cancelled"
        object? Payload,       // endpoint-shaped result object when Status == "ok"
        string? Error,
        string? ErrorKind,
        DateTimeOffset? CompletedAt);

    /// <summary>
    /// A running job's live progress (contract D): cumulative output tokens, an
    /// (always-null-in-v1) determinate percent, and the accumulated partial text.
    /// Progress lives ONLY here (never on the durable <c>AiJobs</c> row) and is lost on
    /// eviction/restart by design.
    /// </summary>
    public sealed record AiJobProgress(int Tokens, double? Percent, string? PartialText);

    private const int MaxJobs = 500;
    private const int DefaultPartialBufferMaxChars = 262144;
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

    // Cancellation is a side map, not a record field — AiJobState is an immutable
    // snapshot and a CancellationTokenSource is mutable, disposable, per-attempt
    // state that has no business surviving a `with` copy. _cancelRequested is
    // tracked separately from CTS presence so ExecuteJobCoreAsync-style callers
    // can tell a deliberate cancel apart from the safety-timeout firing the same
    // OperationCanceledException.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancelSources = new();
    private readonly ConcurrentDictionary<Guid, byte> _cancelRequested = new();

    // Progress/partial side map — token counts + streamed partial text for running
    // jobs. Kept off the immutable AiJobState snapshot (progress mutates far more often
    // than the state record transitions) and off the durable row entirely (contract D).
    private readonly ConcurrentDictionary<Guid, ProgressBox> _progress = new();
    private readonly int _partialBufferMaxChars;

    /// <param name="config">Optional — supplies <c>AiJobs:PartialBufferMaxChars</c>
    /// (default 262144). Null (the parameterless test path) uses the default.</param>
    public AiJobRegistry(IConfiguration? config = null)
    {
        var max = config?.GetValue<int?>("AiJobs:PartialBufferMaxChars") ?? DefaultPartialBufferMaxChars;
        if (max < 1) max = DefaultPartialBufferMaxChars;
        _partialBufferMaxChars = max;
    }

    public AiJobState Create(Guid tenantId, Guid reportId, Guid userId, string kind, string mode)
    {
        Evict();
        var job = new AiJobState(
            Guid.NewGuid(), tenantId, reportId, userId, kind, mode,
            DateTimeOffset.UtcNow, "running", null, null, null, null);
        _jobs[job.Id] = job;
        return job;
    }

    /// <summary>
    /// Materializes a hot-cache entry under a pre-assigned id, so the registry's
    /// id matches the durable <c>AiJobs</c> row <c>AiJobCoordinator</c> just flipped
    /// to "running" — unlike <see cref="Create(Guid,Guid,Guid,string,string)"/>,
    /// which always mints a fresh id for callers with no durable row behind them
    /// (the desktop sidecar's local-generation jobs).
    /// </summary>
    public AiJobState Create(Guid id, Guid tenantId, Guid reportId, Guid userId, string kind, string mode)
    {
        Evict();
        var job = new AiJobState(
            id, tenantId, reportId, userId, kind, mode,
            DateTimeOffset.UtcNow, "running", null, null, null, null);
        _jobs[job.Id] = job;
        return job;
    }

    public bool TryGet(Guid id, out AiJobState job) => _jobs.TryGetValue(id, out job!);

    /// <summary>
    /// Snapshot of one user's own jobs (their tenant only), running first then
    /// newest-first — used to rehydrate a client-side widget that has no durable
    /// row to fall back on (the desktop sidecar's local-generation jobs; the
    /// hosted path rehydrates from the <c>AiJobs</c> table via JobsController
    /// instead, since a registry miss there just means "evicted or restarted").
    /// </summary>
    public IReadOnlyList<AiJobState> ListForUser(Guid tenantId, Guid userId, int max = 50) =>
        _jobs.Values
            .Where(j => j.TenantId == tenantId && j.UserId == userId)
            .OrderByDescending(j => j.Status == "running")
            .ThenByDescending(j => j.CreatedAt)
            .Take(max)
            .ToArray();

    /// <summary>
    /// Registers the CancellationTokenSource a running job's executor is
    /// observing, so <see cref="TryRequestCancel"/> can flip it. Overwrites any
    /// prior registration for the same id (there should never be one — a job
    /// runs exactly once).
    /// </summary>
    public void RegisterCancellation(Guid id, CancellationTokenSource cts) => _cancelSources[id] = cts;

    /// <summary>
    /// Requests cancellation: flips the registered CTS so the executor's next await
    /// observes an OperationCanceledException. Returns false only when no CTS is
    /// registered for this id — either the job never ran here, or it already went
    /// terminal (<see cref="Terminal"/> removes the entry on every completion path).
    ///
    /// <para>Deliberately gates on <c>_cancelSources</c> alone, NOT on a <c>_jobs</c>
    /// snapshot existing. <c>AiJobCoordinator.RunAsync</c> calls
    /// <see cref="RegisterCancellation"/> the instant it dequeues a job — before the
    /// durable row is even claimed queued→running, let alone before a hot
    /// <c>AiJobState</c> snapshot is created via <see cref="Create(Guid,Guid,Guid,Guid,string,string)"/>.
    /// Requiring a snapshot here reopened exactly the race this method exists to
    /// close: a cancel arriving in that window used to silently no-op, and the job
    /// ran to completion — including a "generate" job writing report sections —
    /// after the radiologist had already been told it was cancelled.</para>
    /// </summary>
    public bool TryRequestCancel(Guid id)
    {
        if (!_cancelSources.TryGetValue(id, out var cts)) return false;
        _cancelRequested[id] = 1;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* job completed on its own the instant before; harmless race */ }
        return true;
    }

    /// <summary>True once <see cref="TryRequestCancel"/> has been called for this job id.</summary>
    public bool WasCancelRequested(Guid id) => _cancelRequested.ContainsKey(id);

    /// <summary>Terminal transition to "cancelled" — distinct from Fail's "error" so the
    /// widget can render "you cancelled this" instead of treating it as a failure.</summary>
    public void Cancel(Guid id) => Terminal(id, "cancelled", null, null, null);

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

    /// <summary>
    /// Records one streamed progress tick for a running job: bumps the cumulative token
    /// count and, when <paramref name="partialDelta"/> is non-null, appends it to the
    /// partial-text buffer (capped at <c>AiJobs:PartialBufferMaxChars</c>, keeping the
    /// TAIL — the preview UI shows the end of the stream anyway).
    ///
    /// <para><b>Failover-reset rule:</b> a NON-MONOTONIC token count
    /// (<paramref name="tokens"/> &lt; the current count) means a provider-failover retry
    /// restarted token counting at ~1, so the buffer is cleared first — this is how the
    /// failed attempt's partial text is discarded without a separate signal.</para>
    /// </summary>
    public void UpdateProgress(Guid id, int tokens, double? percent, string? partialDelta)
    {
        var box = _progress.GetOrAdd(id, static _ => new ProgressBox());
        box.Update(tokens, percent, partialDelta, _partialBufferMaxChars);
    }

    /// <summary>The live progress for a job, or null if it has no hot progress entry.</summary>
    public AiJobProgress? ProgressOf(Guid id) =>
        _progress.TryGetValue(id, out var box) ? box.Snapshot() : null;

    private void Terminal(Guid id, string status, object? payload, string? error, string? kind)
    {
        // Only running → terminal is legal; a second completion (e.g. safety
        // timeout racing the real result) keeps the first outcome.
        if (!_jobs.TryGetValue(id, out var cur) || cur.Status != "running") return;
        var next = cur with { Status = status, Payload = payload, Error = error, ErrorKind = kind, CompletedAt = DateTimeOffset.UtcNow };
        _jobs.TryUpdate(id, next, cur);
        _cancelSources.TryRemove(id, out _);
        _cancelRequested.TryRemove(id, out _);
        _progress.TryRemove(id, out _); // partial/progress is meaningless once terminal
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
            if (expired)
            {
                _jobs.TryRemove(id, out _);
                // The RunningRetention backstop bypasses Terminal(), so it must
                // clean these up itself — a leaked entry here is a leaked CTS.
                _cancelSources.TryRemove(id, out _);
                _cancelRequested.TryRemove(id, out _);
                _progress.TryRemove(id, out _);
            }
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
            _progress.TryRemove(job.Id, out _);
        }
    }

    /// <summary>Mutable, lock-guarded holder for one job's live progress. A tick from a
    /// streaming read loop mutates in place rather than swapping an immutable record —
    /// progress updates arrive far more often than state transitions.</summary>
    private sealed class ProgressBox
    {
        private readonly object _lock = new();
        private readonly StringBuilder _buffer = new();
        private int _tokens;
        private double? _percent;

        public void Update(int tokens, double? percent, string? partialDelta, int maxChars)
        {
            lock (_lock)
            {
                // Non-monotonic tokens = a provider-failover retry restarted counting at
                // ~1 → discard the failed attempt's text before accepting the new one.
                if (tokens < _tokens)
                    _buffer.Clear();
                _tokens = tokens;
                _percent = percent;
                if (!string.IsNullOrEmpty(partialDelta))
                {
                    _buffer.Append(partialDelta);
                    if (_buffer.Length > maxChars)
                        _buffer.Remove(0, _buffer.Length - maxChars); // keep the tail
                }
            }
        }

        public AiJobProgress Snapshot()
        {
            lock (_lock)
            {
                return new AiJobProgress(_tokens, _percent, _buffer.Length == 0 ? null : _buffer.ToString());
            }
        }
    }
}
