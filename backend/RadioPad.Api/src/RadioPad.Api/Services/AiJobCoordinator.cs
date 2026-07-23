using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Governance;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Write-through layer between the durable <c>AiJobs</c> table and the in-memory
/// <see cref="AiJobRegistry"/> hot cache, and the home of the job-execution logic
/// that used to live in <c>ReportsController</c>'s detached <c>Task.Run</c> path.
///
/// <para><b>Two roles, two scopes.</b> The controller-invoked methods
/// (<see cref="SubmitAsync"/> / <see cref="RetryAsync"/> /
/// <see cref="RequestCancelAsync"/>) run on the request's scoped
/// <see cref="RadioPadDbContext"/>. <see cref="RunAsync"/> is invoked by
/// <see cref="AiJobRunner"/> off the request thread and opens its OWN fresh DI
/// scope for the minutes-long provider call, exactly as the old code did.</para>
///
/// <para><b>Ordering doctrine:</b> registry FIRST (fast poll visibility), durable
/// row SECOND. A crash between the two leaves a running row that the boot recovery
/// sweep marks <c>server_restart</c> — the safe direction (never a phantom
/// success).</para>
/// </summary>
public sealed class AiJobCoordinator
{
    private readonly RadioPadDbContext _db;
    private readonly AiJobRegistry _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly Channel<AiJobWork> _channel;
    private readonly IAiJobEventBus _bus;
    private readonly ILogger<AiJobCoordinator> _log;

    // AI-013 — per-job streaming publish throttle (contract §3.6). The registry gets EVERY chunk
    // (poll path); the bus is fed a coalesced subset so a fast stream never floods SSE subscribers.
    // Entries are added lazily by OnStreamChunk and removed on terminal (flush-then-remove on
    // success, remove-only on failure/cancel — a cancelled stream's partial text is discarded).
    private readonly ConcurrentDictionary<Guid, StreamThrottle> _streamThrottle = new();
    private readonly int _streamPublishMinIntervalMs;
    private readonly int _streamPublishTokenBatch;

    // PR-B4 — UBAG re-attach budget + poll cadence (see ReattachUbagAsync). The poll
    // interval is configurable (default 2s, mirroring UbagProviderAdapter.PollDelay) so
    // tests can drive the re-poll loop fast without waiting on wall-clock seconds.
    private readonly int _reattachTimeoutSeconds;
    private readonly double _reattachPollSeconds;

    public AiJobCoordinator(
        RadioPadDbContext db,
        AiJobRegistry registry,
        IServiceScopeFactory scopes,
        Channel<AiJobWork> channel,
        IAiJobEventBus bus,
        ILogger<AiJobCoordinator> log,
        IConfiguration? config = null)
    {
        _db = db;
        _registry = registry;
        _scopes = scopes;
        _channel = channel;
        _bus = bus;
        _log = log;
        _streamPublishMinIntervalMs = config?.GetValue<int?>("AiJobs:StreamPublishMinIntervalMs") ?? 150;
        _streamPublishTokenBatch = config?.GetValue<int?>("AiJobs:StreamPublishTokenBatch") ?? 20;
        if (_streamPublishMinIntervalMs < 0) _streamPublishMinIntervalMs = 150;
        if (_streamPublishTokenBatch < 1) _streamPublishTokenBatch = 20;
        _reattachTimeoutSeconds = config?.GetValue<int?>("AiJobs:ReattachTimeoutSeconds") ?? 300;
        if (_reattachTimeoutSeconds < 1) _reattachTimeoutSeconds = 300;
        _reattachPollSeconds = config?.GetValue<double?>("AiJobs:ReattachPollSeconds") ?? 2.0;
        if (_reattachPollSeconds <= 0) _reattachPollSeconds = 2.0;
    }

    /// <summary>Mutable, lock-guarded per-job throttle state for the streaming bus publish.</summary>
    private sealed class StreamThrottle
    {
        public readonly object Gate = new();
        public readonly StringBuilder PendingDelta = new();
        public long LastPublishTicks;
        public int LastPublishedTokens;
        public int LastSeenTokens;
    }

    // ── submit / retry / cancel — controller-invoked, request-scoped _db ──────────────────────────

    /// <summary>
    /// Enqueues a fresh generation attempt, or — DB single-flight — returns the
    /// already-active identical job so a second detached generation never stacks on
    /// the same (tenant, report, kind, mode). Mode/gating/provider validation stays
    /// in the controller (it owns the IActionResult error shapes); this receives an
    /// already-validated tuple.
    /// </summary>
    public async Task<(Guid jobId, string status, bool alreadyExisting)> SubmitAsync(
        Tenant tenant, User user, Guid reportId, string kind, string mode, Guid? providerId, CancellationToken ct,
        string? inputJson = null)
    {
        var existing = await FindActiveAsync(tenant.Id, reportId, kind, mode, ct);
        if (existing is not null)
            return (existing.Id, existing.Status, true);

        var job = await CreateAndEnqueueAsync(
            tenant.Id, user.Id, reportId, kind, mode, providerId, attempt: 1, retryOf: null, ct, inputJson);
        return (job.Id, job.Status, false);
    }

    /// <summary>
    /// Re-runs a failed/cancelled job as a NEW row (Attempt+1, RetryOfJobId set) —
    /// never resurrects the old one. Goes through the same gating a fresh submit
    /// applies so a since-disabled regulated feature (or a deleted provider) cannot
    /// be walked around via retry.
    /// </summary>
    public async Task<Guid> RetryAsync(Tenant tenant, User user, Guid jobId, CancellationToken ct)
    {
        var prior = await _db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenant.Id, ct)
            ?? throw new InvalidOperationException("job_not_found");
        if (prior.Status != "error" && prior.Status != "cancelled")
            throw new InvalidOperationException("job_not_retryable");

        await EnforceGatingAsync(tenant.Id, prior.Kind, prior.Mode, prior.ProviderId, ct);

        // PR-B5 — input-carrying kinds can only be re-run while their InputJson survives; the
        // retention sweep nulls it 24h after completion (same clinical-text-at-rest class as
        // ResultJson), after which the raw input is unrecoverable. A crosscheck job ALWAYS carries
        // input, so a swept one is not retryable → 409 job_input_expired. An ai+cleanup row carries
        // input only when submitted via the dedicated /dictation/cleanup/jobs route; a legacy
        // /ai/jobs cleanup never did and re-runs the generic single-text path, so it must NOT hard-
        // fail here — hence the input-present clause below is tautologically skipped once swept,
        // leaving the legacy retry path intact.
        var inputJson = prior.InputJson;
        var requiresInput = string.Equals(prior.Kind, "crosscheck", StringComparison.Ordinal)
            || (string.Equals(prior.Kind, "ai", StringComparison.Ordinal)
                && string.Equals(prior.Mode, "cleanup", StringComparison.Ordinal)
                && inputJson is not null);
        if (requiresInput && string.IsNullOrEmpty(inputJson))
            throw new InvalidOperationException("job_input_expired");

        // A manual re-submit may have raced this retry — attach rather than stack.
        var existing = await FindActiveAsync(tenant.Id, prior.ReportId, prior.Kind, prior.Mode, ct);
        if (existing is not null) return existing.Id;

        var job = await CreateAndEnqueueAsync(
            tenant.Id, user.Id, prior.ReportId, prior.Kind, prior.Mode, prior.ProviderId,
            attempt: prior.Attempt + 1, retryOf: prior.Id, ct, inputJson);
        return job.Id;
    }

    /// <summary>
    /// Requests cancellation. Returns <c>(changed, status)</c> — the caller (currently
    /// <c>JobsController.Cancel</c>) MUST respond from this return value, never from
    /// a status it read before calling this method: <see cref="RunAsync"/> can flip
    /// queued→running concurrently, and a response built from a stale read would
    /// tell the radiologist "cancelled" for a job that is, in fact, still running.
    ///
    /// <para>The queued→cancelled transition below and <see cref="RunAsync"/>'s
    /// queued→running claim are both atomic conditional updates guarded on
    /// <c>WHERE Status = 'queued'</c> against the SAME row — the database guarantees
    /// exactly one of them wins the race, never a lost update. Whichever loses
    /// re-reads the row's actual current state and acts on that, never on a stale
    /// in-memory guess.</para>
    /// </summary>
    public async Task<(bool changed, string status)> RequestCancelAsync(Guid tenantId, Guid jobId, CancellationToken ct)
    {
        var row = await _db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenantId, ct);
        if (row is null) return (false, "not_found");

        if (row.Status == "queued")
        {
            var now = DateTimeOffset.UtcNow;
            row.Status = "cancelled";
            row.CancelRequested = true;
            row.CompletedAt = now;
            row.UpdatedAt = now;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Status is a concurrency token (RadioPadDbContext.OnModelCreating) — this
                // means RunAsync's queued→running claim committed first. Re-read and act
                // on what actually happened, not on our now-stale "queued" snapshot.
                return await ReRequestCancelAsync(tenantId, jobId, ct);
            }
            _registry.Cancel(jobId); // no-op unless a hot entry somehow exists
            _bus.PublishTerminal(row);
            return (true, "cancelled");
        }

        if (row.Status == "running")
        {
            row.CancelRequested = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The job went terminal (completed/failed/timed out) between our read
                // and this write — CancelRequested no longer matters; report the truth.
                return await ReRequestCancelAsync(tenantId, jobId, ct);
            }
            // Reliable even if RunAsync's claim only JUST committed: RegisterCancellation
            // happens at RunAsync's very first line, before the claim itself.
            _registry.TryRequestCancel(jobId);
            return (true, "running");
        }

        return (false, row.Status); // already terminal — idempotent echo
    }

    /// <summary>One-shot retry after a concurrency conflict — re-reads the row (now
    /// reflecting whichever writer won) and re-applies the same decision logic exactly
    /// once. A second conflict in that same instant is vanishingly unlikely and, if it
    /// somehow happens, surfaces as a 500 rather than silently reporting the wrong
    /// outcome — the honest failure mode.</summary>
    private async Task<(bool changed, string status)> ReRequestCancelAsync(Guid tenantId, Guid jobId, CancellationToken ct)
    {
        var row = await _db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenantId, ct);
        if (row is null) return (false, "not_found");

        if (row.Status == "running")
        {
            row.CancelRequested = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Went terminal in the ~microsecond since we just re-read it. Report
                // the honest current state rather than retrying indefinitely.
                var latest = await _db.AiJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenantId, ct);
                return latest is null ? (false, "not_found") : (false, latest.Status);
            }
            _registry.TryRequestCancel(jobId);
            return (true, "running");
        }

        return (false, row.Status); // terminal by the time we could act
    }

    // ── execution — runner-invoked, its OWN fresh scope ──────────────────────────────────────────

    /// <summary>
    /// Executes one dequeued job. Registers <paramref name="jobCts"/> with the
    /// registry (so a user cancel can flip it), flips the durable row to running,
    /// lights up the hot cache, runs the provider call, then commits the terminal
    /// outcome registry-first / DB-second. Every failure mode maps to the same
    /// errorKinds the old sync path produced, plus <c>timeout</c> (safety timeout /
    /// shutdown) and <c>cancelled</c> (deliberate cancel).
    /// </summary>
    internal async Task RunAsync(AiJobWork work, CancellationTokenSource jobCts)
    {
        var jobId = work.JobId;
        _registry.RegisterCancellation(jobId, jobCts);
        var ct = jobCts.Token;

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

            var row = await db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (row is null) return;            // row vanished (deleted / retention)
            if (row.Status != "queued") return; // already picked up / cancelled — never double-run

            // Claim it: Status is a concurrency token (RadioPadDbContext.OnModelCreating),
            // so this SaveChangesAsync implicitly guards on "Status is still 'queued' in
            // the database" — the mirror image of RequestCancelAsync's queued→cancelled
            // write against the same row. Exactly one of the two wins; the loser's save
            // throws DbUpdateConcurrencyException instead of silently overwriting the
            // winner's outcome with "running".
            var startedAt = DateTimeOffset.UtcNow;
            row.Status = "running";
            row.StartedAt = startedAt;
            row.UpdatedAt = startedAt;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent cancel claimed it first — never double-run, and never
                // overwrite whatever terminal state that cancel already committed.
                return;
            }

            _registry.Create(jobId, row.TenantId, row.ReportId, row.UserId, row.Kind, row.Mode);

            // Resolve the provider service only now that we're actually running — a
            // skipped/cancelled job needs none of it.
            var reporting = scope.ServiceProvider.GetRequiredService<ReportingService>();
            var (tenant, user, report) = await LoadJobContextAsync(db, work.TenantId, work.UserId, work.ReportId, ct);

            if (string.Equals(work.Kind, "generate", StringComparison.Ordinal))
                await RunGenerateAsync(db, reporting, row, tenant, user, report, work.ProviderId, ct);
            else if (string.Equals(work.Kind, "crosscheck", StringComparison.Ordinal))
                await RunCrossCheckAsync(scope, db, row, tenant, report, work.InputJson, ct);
            else if (string.Equals(work.Kind, "ai", StringComparison.Ordinal)
                     && string.Equals(work.Mode, "cleanup", StringComparison.Ordinal)
                     && !string.IsNullOrEmpty(work.InputJson))
                // PR-B5 — the InputJson-presence guard keeps the legacy generic mode=cleanup path
                // (submittable via /ai/jobs with no input) on RunAiAsync, byte-identical.
                await RunCleanupAsync(scope, row, tenant, user, report, work.InputJson, ct);
            else
                await RunAiAsync(db, reporting, row, tenant, user, report, work.Mode, work.ProviderId, ct);
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "provider_not_found")
        {
            await FailAsync(jobId, "Provider not found.", "not_found");
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "context_gone")
        {
            await FailAsync(jobId, "The report, user, or organisation was removed while the job was queued.", "not_found");
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "report_modified")
        {
            await FailAsync(
                jobId,
                "The report was edited while the AI generation was running, so the AI result was discarded to protect your edits. Re-run generation if you still want it.",
                "report_modified");
        }
        catch (QuotaExceededException qex)
        {
            await FailAsync(jobId, qex.Message, "quota_exceeded");
        }
        catch (ProviderPolicyException pex)
        {
            await FailAsync(jobId, pex.Message, "provider_policy");
        }
        catch (ProviderTransportException tex)
        {
            await FailAsync(jobId, tex.Message, "provider_transport");
        }
        catch (RulebookGovernanceException rge)
        {
            await FailAsync(jobId, rge.Message, "rulebook_governance");
        }
        catch (OperationCanceledException)
        {
            if (_registry.WasCancelRequested(jobId))
            {
                // Deliberate cancel (user / coordinator) — distinct from a timeout.
                _registry.Cancel(jobId);
                await MarkTerminalDbAsync(jobId, "cancelled", null, null);
            }
            else
            {
                // Safety timeout fired, or the server is shutting down.
                const string msg = "The AI generation timed out or the server is shutting down.";
                _registry.Fail(jobId, msg, "timeout");
                await MarkTerminalDbAsync(jobId, "error", msg, "timeout");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Async AI job {JobId} (tenant {TenantId}) failed unexpectedly", jobId, work.TenantId);
            await FailAsync(jobId, "Unexpected server error during AI generation.", "server_error");
        }
        finally
        {
            // Drop any leftover streaming throttle state on every terminal path. The success
            // paths already flushed+removed it via FlushStreamProgress; this is the safety net
            // for the failure/cancel paths, where the partial text is intentionally discarded.
            _streamThrottle.TryRemove(jobId, out _);
        }
    }

    /// <summary>AI-013 / PR-B4 — the run hooks for a running job: every streamed chunk drives
    /// <see cref="OnStreamChunk"/>, and the provider-job-created seam persists the UBAG gateway
    /// job id (only the UBAG adapter ever fires it) so a restart mid-run can re-attach.</summary>
    private AiRunHooks StreamHooksFor(AiJob row) =>
        new(new SynchronousProgress<AiStreamChunk>(chunk => OnStreamChunk(row, chunk)),
            OnProviderJobCreated: pid => OnProviderJobCreated(row.Id, pid));

    private async Task RunAiAsync(
        RadioPadDbContext db, ReportingService reporting, AiJob row,
        Tenant tenant, User user, Report report, string mode, Guid? providerId, CancellationToken ct)
    {
        var hooks = StreamHooksFor(row);
        object payload;
        if (providerId is { } pid && pid != Guid.Empty)
        {
            var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == row.TenantId, ct)
                ?? throw new InvalidOperationException("provider_not_found");
            var result = await reporting.RunAsync(tenant, user, report, provider, mode, ct, hooks);
            payload = new
            {
                text = result.Text,
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                promptVersion = result.PromptVersion,
                mode,
                routedBy = "manual",
            };
        }
        else
        {
            var (result, picked) = await reporting.RunAutoAsync(tenant, user, report, mode, ct, hooks);
            payload = new
            {
                text = result.Text,
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                promptVersion = result.PromptVersion,
                mode,
                routedBy = "auto",
                selectedProviderId = picked.Id,
            };
        }

        FlushStreamProgress(row); // emit any buffered partial delta before the terminal event
        _registry.Complete(row.Id, payload); // hot cache first
        await MarkTerminalOkDbAsync(row.Id, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    // PR-B5 — input payloads are round-tripped with Web defaults (camelCase, case-insensitive) so
    // they bind regardless of the anonymous-object member casing the submit endpoints serialize with.
    private static readonly System.Text.Json.JsonSerializerOptions InputJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private sealed record CleanupInput(string? RawDictation);
    private sealed record CrossCheckInput(string? Text, string? SectionKey, bool UseUbag);

    /// <summary>
    /// PR-B5 — durable dictation-cleanup run. Resolves <see cref="IDictationCleanupService"/> from
    /// the run scope and mirrors the sync <c>POST /dictation/cleanup</c> envelope exactly, so the
    /// poll/widget render identically. The result is a SUGGESTION SET only: nothing here writes the
    /// report — the preview/accept gate stays entirely client-side. Streaming hooks are deliberately
    /// not wired (short structured output; the widget shows an indeterminate ring). Inherits
    /// RunAsync's catch ladder (ProviderPolicyException → provider_policy, transport, quota, cancel/
    /// timeout).
    /// </summary>
    private async Task RunCleanupAsync(
        IServiceScope scope, AiJob row,
        Tenant tenant, User user, Report report, string inputJson, CancellationToken ct)
    {
        var input = System.Text.Json.JsonSerializer.Deserialize<CleanupInput>(inputJson, InputJsonOptions)
            ?? new CleanupInput(null);
        var service = scope.ServiceProvider.GetRequiredService<IDictationCleanupService>();
        var result = await service.CleanupAsync(tenant, user, report, input.RawDictation ?? string.Empty, ct);

        object payload = new
        {
            cleanedSections = new
            {
                indication = result.Indication,
                technique = result.Technique,
                findings = result.Findings,
                impression = result.Impression,
                recommendations = result.Recommendations,
            },
            provider = result.Provider,
            model = result.Model,
            latencyMs = result.LatencyMs,
            promptVersion = result.PromptVersion,
        };
        _registry.Complete(row.Id, payload); // hot cache first
        await MarkTerminalOkDbAsync(row.Id, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// PR-B5 — durable cross-check medical-review run. Deserializes <c>{ text, sectionKey, useUbag }</c>;
    /// when <c>useUbag</c> is set it re-resolves the forced UBAG provider with the same query as the
    /// sync <c>POST /crosscheck/review</c> (so a since-disabled UBAG provider naturally falls back to
    /// the router, matching sync behaviour). Mirrors the sync correction envelope exactly. Corrections
    /// are SUGGESTIONS only — never persisted to the report. Inherits RunAsync's catch ladder.
    /// </summary>
    private async Task RunCrossCheckAsync(
        IServiceScope scope, RadioPadDbContext db, AiJob row,
        Tenant tenant, Report report, string? inputJson, CancellationToken ct)
    {
        var input = System.Text.Json.JsonSerializer.Deserialize<CrossCheckInput>(
            string.IsNullOrEmpty(inputJson) ? "{}" : inputJson, InputJsonOptions)
            ?? new CrossCheckInput(null, null, false);

        RadioPad.Domain.Entities.ProviderConfig? forced = null;
        if (input.UseUbag)
        {
            forced = await db.Providers.FirstOrDefaultAsync(
                p => p.TenantId == row.TenantId
                     && p.Enabled
                     && p.Adapter == RadioPad.Infrastructure.Providers.Ubag.UbagProviderAdapter.AdapterId, ct);
        }

        var service = scope.ServiceProvider.GetRequiredService<ICrossCheckReviewService>();
        var result = await service.ReviewAsync(tenant, report, input.Text ?? string.Empty, input.SectionKey, forced, ct);

        object payload = new
        {
            provider = result.Provider,
            model = result.Model,
            latencyMs = result.LatencyMs,
            corrections = result.Corrections.Select(c => new
            {
                id = c.Id,
                sectionKey = c.SectionKey,
                originalText = c.OriginalText,
                correctedText = c.CorrectedText,
                startOffset = c.StartOffset,
                endOffset = c.EndOffset,
                reason = c.Reason,
                category = c.Category,
                source = c.Source,
                confidence = c.Confidence,
                severity = c.Severity,
            }).ToList(),
        };
        _registry.Complete(row.Id, payload); // hot cache first
        await MarkTerminalOkDbAsync(row.Id, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    private async Task RunGenerateAsync(
        RadioPadDbContext db, ReportingService reporting, AiJob row,
        Tenant tenant, User user, Report report, Guid? providerId, CancellationToken ct)
    {
        // Freshness guard: the provider call runs for minutes DETACHED from the
        // client, so the radiologist may edit the report meanwhile (the old sync
        // endpoint could not hit this — a disconnect cancelled it). A stale
        // write-back would silently clobber authored medical text.
        var updatedAtAtSubmit = report.UpdatedAt;
        var hooks = StreamHooksFor(row);

        ReportingService.StructuredReportResult result;
        if (providerId is { } pid && pid != Guid.Empty)
        {
            var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == row.TenantId, ct)
                ?? throw new InvalidOperationException("provider_not_found");
            result = await reporting.GenerateStructuredAsync(tenant, user, report, provider, ct, hooks);
        }
        else
        {
            (result, _) = await reporting.GenerateStructuredAutoAsync(tenant, user, report, ct, hooks);
        }

        await db.Entry(report).ReloadAsync(ct);
        if (report.UpdatedAt != updatedAtAtSubmit)
            throw new InvalidOperationException("report_modified");

        await ApplyStructuredResultAsync(db, report, user, result, ct);

        FlushStreamProgress(row); // emit any buffered partial delta before the terminal event

        // Mirror the pre-existing registry payload for a generate job (the report
        // itself) so the hot-path and DB-fallback poll bodies stay identical. No
        // ResultJson — the report row + its ReportVersion snapshot ARE the result.
        _registry.Complete(row.Id, report);
        await MarkTerminalOkDbAsync(row.Id, resultJson: null); // the report row + its ReportVersion ARE the result
    }

    // ── streaming (AI-013) ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AI-013 — invoked SYNCHRONOUSLY, in arrival order, for every streamed chunk (via
    /// <see cref="SynchronousProgress{T}"/>). Always feeds the registry (the poll path sees every
    /// token); publishes to the bus only when throttled — at least
    /// <c>AiJobs:StreamPublishMinIntervalMs</c> (default 150 ms) has elapsed OR
    /// <c>AiJobs:StreamPublishTokenBatch</c> (default 20) new tokens have accumulated — coalescing
    /// buffered deltas into one <c>partial</c> event. <c>Percent</c> is ALWAYS null (design §3.10:
    /// n_predict/max_tokens is a ceiling, not a target, so there is no honest ratio).
    /// </summary>
    internal void OnStreamChunk(AiJob row, AiStreamChunk chunk)
    {
        _registry.UpdateProgress(row.Id, chunk.OutputTokens, percent: null, partialDelta: chunk.Delta);

        var throttle = _streamThrottle.GetOrAdd(row.Id, static _ => new StreamThrottle());
        bool publish;
        string? delta = null;
        lock (throttle.Gate)
        {
            throttle.LastSeenTokens = chunk.OutputTokens;
            if (!string.IsNullOrEmpty(chunk.Delta))
                throttle.PendingDelta.Append(chunk.Delta);

            var nowTicks = Environment.TickCount64;
            publish = (nowTicks - throttle.LastPublishTicks) >= _streamPublishMinIntervalMs
                      || (chunk.OutputTokens - throttle.LastPublishedTokens) >= _streamPublishTokenBatch;
            if (publish)
            {
                delta = throttle.PendingDelta.Length == 0 ? null : throttle.PendingDelta.ToString();
                throttle.PendingDelta.Clear();
                throttle.LastPublishTicks = nowTicks;
                throttle.LastPublishedTokens = chunk.OutputTokens;
            }
        }

        if (publish)
            _bus.PublishProgress(new AiJobProgressEvent(
                row.Id, row.TenantId, row.UserId, row.ReportId, row.Kind, row.Mode,
                chunk.OutputTokens, null, delta));
    }

    /// <summary>
    /// AI-013 — flushes any partial delta buffered since the last throttled publish as a final
    /// <c>partial</c> event, then removes the throttle entry. Called immediately before a
    /// SUCCESSFUL terminal so the last words of the stream are never lost behind the throttle.
    /// </summary>
    internal void FlushStreamProgress(AiJob row)
    {
        if (!_streamThrottle.TryRemove(row.Id, out var throttle)) return;
        string? delta;
        int tokens;
        lock (throttle.Gate)
        {
            delta = throttle.PendingDelta.Length == 0 ? null : throttle.PendingDelta.ToString();
            throttle.PendingDelta.Clear();
            tokens = throttle.LastSeenTokens;
        }
        if (delta is not null)
            _bus.PublishProgress(new AiJobProgressEvent(
                row.Id, row.TenantId, row.UserId, row.ReportId, row.Kind, row.Mode,
                tokens, null, delta));
    }

    // ── UBAG re-attach (PR-B4) ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// PR-B4 — the <c>OnProviderJobCreated</c> seam: the UBAG adapter fires this the instant a
    /// gateway job exists, so a restart mid-poll can re-attach to it. Fire-and-forget on the
    /// thread pool with a full try/catch — the callback is synchronous by contract and the
    /// (minutes-long) provider run must never block on, or be broken by, this write.
    /// </summary>
    private void OnProviderJobCreated(Guid jobId, string providerJobId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await PersistProviderJobIdAsync(jobId, providerJobId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to persist UBAG gateway job id for AI job {JobId}", jobId);
            }
        });
    }

    /// <summary>
    /// Persists the UBAG gateway job id onto the durable row on a FRESH scope.
    /// <c>ExecuteUpdate</c> is deliberate: it does NOT touch <c>Status</c> (so it can never race
    /// the Status concurrency-token terminal write), and <c>ProviderJobId</c> is a plain string
    /// column with no <c>DateTimeOffset</c>⇒ticks value converter to honour. <c>UpdatedAt</c> is
    /// intentionally left untouched — capturing the gateway id is bookkeeping, not a state change.
    /// </summary>
    internal async Task PersistProviderJobIdAsync(Guid jobId, string providerJobId)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        await db.AiJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ProviderJobId, providerJobId));
    }

    /// <summary>
    /// PR-B4 — re-attaches to a UBAG gateway job that was still running when the server restarted
    /// (its gateway job id was captured in <see cref="AiJob.ProviderJobId"/>). Opens a fresh scope,
    /// makes the job poll-visible + cancellable again, re-polls the gateway job to completion, and
    /// commits the terminal outcome through the SAME registry-first / DB-second path a live run uses
    /// so the poll/widget render identically.
    ///
    /// <para><b>Re-attach is re-poll ONLY — it never issues a fresh AI call.</b> Boot must not fan
    /// out new provider work. For a <c>generate</c> re-attach the
    /// <c>GenerateMissingRecommendationsAsync</c> backfill is therefore SKIPPED (via the extracted
    /// <see cref="ReportingService.ShapeStructuredResult"/>, which omits it) — empty recommendations
    /// are acceptable. The freshness guard uses <see cref="AiJob.StartedAt"/> as the conservative
    /// stand-in for the submit-time snapshot we no longer hold post-restart: any human edit after
    /// the job started discards the AI result to protect authored text.</para>
    ///
    /// <para>Any re-attach that CANNOT CONCLUDE — budget exhausted, a second shutdown, an
    /// unreachable gateway — falls back to <c>server_restart</c>, the same safe state the boot
    /// sweep produces ("Interrupted by a restart — Retry"). A gateway job that concludes as a
    /// definitive failure maps to <c>provider_transport</c>, mirroring the live adapter.</para>
    /// </summary>
    internal async Task ReattachUbagAsync(Guid jobId, CancellationToken appStopping)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var row = await db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId, appStopping);
        // Bail unless the row is still a genuine re-attach candidate: a cancel/sweep/completion
        // may have moved it, or another instance already re-attached, since boot partitioned it.
        if (row is null || row.Status != "running" || string.IsNullOrEmpty(row.ProviderJobId))
            return;

        var providerJobId = row.ProviderJobId!;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
        cts.CancelAfter(TimeSpan.FromSeconds(_reattachTimeoutSeconds));
        var ct = cts.Token;

        // Poll-visible AND cancellable: RequestCancelAsync's running branch flips this CTS via the
        // registry, ending the poll loop; the OperationCanceledException path then best-effort
        // cancels the gateway job. RegisterCancellation must land BEFORE the first await.
        _registry.Create(jobId, row.TenantId, row.ReportId, row.UserId, row.Kind, row.Mode);
        _registry.RegisterCancellation(jobId, cts);

        try
        {
            var ubag = scope.ServiceProvider.GetRequiredService<IUbagClient>();

            UbagJob terminal;
            while (true)
            {
                var job = await ubag.GetJobAsync(providerJobId, ct);
                if (job.Terminal || !string.IsNullOrWhiteSpace(job.ManualAction))
                {
                    terminal = job;
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(_reattachPollSeconds), ct);
            }

            // Terminal shaping mirrors UbagProviderAdapter.CompleteAsync's classification: a
            // manual_action / error / failed / empty-output terminal is a definitive provider
            // failure → provider_transport (NOT the "cannot conclude" server_restart fallback).
            if (!string.IsNullOrWhiteSpace(terminal.ManualAction)
                || !string.IsNullOrWhiteSpace(terminal.Error)
                || terminal.Failed
                || string.IsNullOrWhiteSpace(terminal.Output))
            {
                var msg = !string.IsNullOrWhiteSpace(terminal.ManualAction)
                    ? $"ubag: manual_action_required:{terminal.Target}"
                    : (!string.IsNullOrWhiteSpace(terminal.Error) || terminal.Failed)
                        ? $"ubag: {terminal.Error ?? terminal.Status}"
                        : "ubag: empty_output";
                await FailAsync(jobId, msg, "provider_transport");
                return;
            }

            var providerName = row.ProviderId is { } pid && pid != Guid.Empty
                ? (await db.Providers.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == row.TenantId, ct))?.Name ?? "ubag"
                : "ubag";
            var model = string.IsNullOrWhiteSpace(terminal.Target) ? "ubag" : terminal.Target;

            if (string.Equals(row.Kind, "generate", StringComparison.Ordinal))
            {
                // Shape identically to a live generate — but WITHOUT the recommendations backfill
                // (that is a second AI call; boot must not fan out fresh provider work).
                var result = ReportingService.ShapeStructuredResult(
                    terminal.Output!, providerName, model, terminal.LatencyMs ?? 0, "reattach");

                var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == row.ReportId && r.TenantId == row.TenantId, ct)
                    ?? throw new InvalidOperationException("context_gone");
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == row.UserId && u.TenantId == row.TenantId, ct)
                    ?? throw new InvalidOperationException("context_gone");

                // Freshness guard: StartedAt is the honest conservative stand-in for the
                // submit-time snapshot we can no longer hold post-restart — discard the AI
                // result if a human edited the report after the job started.
                if (row.StartedAt is { } startedAt && report.UpdatedAt > startedAt)
                    throw new InvalidOperationException("report_modified");

                await ApplyStructuredResultAsync(db, report, user, result, ct);
                _registry.Complete(jobId, report);
                await MarkTerminalOkDbAsync(jobId, resultJson: null); // report row + ReportVersion ARE the result
            }
            else
            {
                // Kind == "ai": same envelope keys as RunAiAsync so the poll/widget render
                // identically; routedBy "reattach" marks the origin.
                var payload = new
                {
                    text = terminal.Output,
                    provider = providerName,
                    model,
                    latencyMs = terminal.LatencyMs ?? 0,
                    promptVersion = "reattach",
                    mode = row.Mode,
                    routedBy = "reattach",
                };
                _registry.Complete(jobId, payload);
                await MarkTerminalOkDbAsync(jobId, System.Text.Json.JsonSerializer.Serialize(payload));
            }
        }
        catch (OperationCanceledException) when (_registry.WasCancelRequested(jobId))
        {
            // Deliberate user cancel — best-effort release the gateway worker, then mark cancelled.
            try
            {
                var ubag = scope.ServiceProvider.GetRequiredService<IUbagClient>();
                await ubag.CancelJobAsync(providerJobId, CancellationToken.None);
            }
            catch { /* best-effort — the gateway's own job timeout is the backstop */ }
            _registry.Cancel(jobId);
            await MarkTerminalDbAsync(jobId, "cancelled", null, null);
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "report_modified")
        {
            await FailAsync(
                jobId,
                "The report was edited while the AI generation was running, so the AI result was discarded to protect your edits. Re-run generation if you still want it.",
                "report_modified");
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "context_gone")
        {
            await FailAsync(jobId, "The report or user was removed while the job was interrupted.", "not_found");
        }
        catch (Exception ex)
        {
            // Any re-attach that cannot conclude — budget/shutdown OperationCanceledException with
            // no user cancel, an unreachable gateway, an unexpected fault — falls back to the SAME
            // safe state the boot sweep produces. server_restart is the REQUIRED failure mode here.
            if (ex is not OperationCanceledException)
                _log.LogWarning(ex, "UBAG re-attach for job {JobId} could not conclude; falling back to server_restart", jobId);
            const string msg = "Interrupted by a server restart. Retry to run it again.";
            _registry.Fail(jobId, msg, "server_restart");
            await MarkTerminalDbAsync(jobId, "error", msg, "server_restart");
        }
    }

    // ── shared helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<AiJob?> FindActiveAsync(Guid tenantId, Guid reportId, string kind, string mode, CancellationToken ct) =>
        await _db.AiJobs
            .Where(j => j.TenantId == tenantId && j.ReportId == reportId && j.Kind == kind && j.Mode == mode
                        && (j.Status == "queued" || j.Status == "running"))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<AiJob> CreateAndEnqueueAsync(
        Guid tenantId, Guid userId, Guid reportId, string kind, string mode, Guid? providerId,
        int attempt, Guid? retryOf, CancellationToken ct, string? inputJson = null)
    {
        var job = new AiJob
        {
            TenantId = tenantId,
            ReportId = reportId,
            UserId = userId,
            Kind = kind,
            Mode = mode,
            ProviderId = providerId,
            Status = "queued",
            Attempt = attempt,
            RetryOfJobId = retryOf,
            InputJson = inputJson,
        };
        _db.AiJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        var work = new AiJobWork(job.Id, tenantId, userId, reportId, kind, mode, providerId, inputJson);
        if (!_channel.Writer.TryWrite(work))
        {
            // Unbounded channel — TryWrite only fails if the writer is completed
            // (host shutting down). The row stays "queued" and the boot recovery
            // sweep marks it server_restart on next start.
            _log.LogWarning("AI job {JobId} could not be enqueued (channel closed); it will be swept on restart", job.Id);
        }
        return job;
    }

    /// <summary>
    /// The security-relevant slice of a fresh submit's validation, re-expressed as
    /// typed exceptions for the retry path (submit validation lives in the
    /// controller for its IActionResult shapes). Keeps the regulated-feature gate
    /// non-bypassable via retry.
    /// </summary>
    private async Task EnforceGatingAsync(Guid tenantId, string kind, string mode, Guid? providerId, CancellationToken ct)
    {
        if (string.Equals(kind, "ai", StringComparison.OrdinalIgnoreCase)
            && string.Equals(mode, "impression", StringComparison.OrdinalIgnoreCase))
        {
            var settings = await _db.TenantSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (!RegulatedFeatures.IsEnabled(settings?.FeatureFlagsJson, RegulatedFeature.AutoImpression))
                throw new InvalidOperationException("regulated_feature_disabled");
        }

        if (providerId is { } pid && pid != Guid.Empty
            && !await _db.Providers.AnyAsync(p => p.Id == pid && p.TenantId == tenantId, ct))
            throw new InvalidOperationException("provider_not_found");
    }

    private async Task FailAsync(Guid jobId, string message, string errorKind)
    {
        _registry.Fail(jobId, message, errorKind); // hot cache first
        await MarkTerminalDbAsync(jobId, "error", message, errorKind);
    }

    /// <summary>
    /// Persists a terminal outcome on a FRESH scope with an uncancelled token — the
    /// RunAsync scope's DbContext/token is unusable on the cancel/timeout/error
    /// paths. First-terminal-wins is enforced by the <c>Status IN (queued, running)</c>
    /// guard so a late writer can't overwrite a decided row.
    /// </summary>
    private async Task MarkTerminalDbAsync(Guid jobId, string status, string? error, string? errorKind)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (row is null) return;
            if (row.Status != "queued" && row.Status != "running") return; // first outcome wins
            var now = DateTimeOffset.UtcNow;
            row.Status = status;
            row.Error = error;
            row.ErrorKind = errorKind;
            row.CompletedAt = now;
            row.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Status is a concurrency token: another writer (the queued→running
                // claim, or a concurrent cancel) already moved this row since our read
                // above — first-terminal-wins, so this write correctly loses.
                return;
            }
            _bus.PublishTerminal(row);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to persist terminal state ({Status}/{ErrorKind}) for AI job {JobId}", status, errorKind, jobId);
        }
    }

    /// <summary>
    /// Persists a terminal SUCCESS outcome — the counterpart to
    /// <see cref="MarkTerminalDbAsync"/> for the "ok" path. Runs on a FRESH scope
    /// with no cancellation token: the job's own <c>ct</c> can fire (safety timeout,
    /// shutdown) in the instant between the provider call returning and this write
    /// landing, and that must never turn an actually-successful generation into a
    /// spurious "timeout" — especially for a "generate" job, which has ALREADY
    /// committed report sections + a ReportVersion by the time this runs. Same
    /// atomic first-terminal-wins guard as <see cref="MarkTerminalDbAsync"/>.
    /// </summary>
    private async Task MarkTerminalOkDbAsync(Guid jobId, string? resultJson)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.AiJobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (row is null) return;
            if (row.Status != "queued" && row.Status != "running") return; // a cancel/timeout already decided this job first
            var now = DateTimeOffset.UtcNow;
            row.Status = "ok";
            row.ResultJson = resultJson;
            row.CompletedAt = now;
            row.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Status is a concurrency token: a concurrent cancel/timeout already
                // moved this row since our read above — that decision stands even
                // though the generation itself actually succeeded underneath it.
                return;
            }
            _bus.PublishTerminal(row);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to persist ok state for AI job {JobId}", jobId);
        }
    }

    internal static async Task<(Tenant tenant, User user, Report report)> LoadJobContextAsync(
        RadioPadDbContext db, Guid tenantId, Guid userId, Guid reportId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && r.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        return (tenant, user, report);
    }

    /// <summary>
    /// Applies a structured generation result onto the report (each populated
    /// section adopted + AI-highlighted) and records a "generate" ReportVersion
    /// snapshot. Shared verbatim by the sync <c>POST /generate</c> endpoint and the
    /// async generate job so both persist identically.
    /// </summary>
    public static async Task ApplyStructuredResultAsync(
        RadioPadDbContext db, Report report, User user, ReportingService.StructuredReportResult result, CancellationToken ct)
    {
        var highlights = ParseHighlights(report.AiHighlightsJson);
        void Adopt(string key, string value, Action<string> set)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            set(value);
            highlights[key] = true;
        }
        Adopt("indication", result.Indication, v => report.Indication = v);
        Adopt("technique", result.Technique, v => report.Technique = v);
        Adopt("comparison", result.Comparison, v => report.Comparison = v);
        Adopt("findings", result.Findings, v => report.Findings = v);
        Adopt("impression", result.Impression, v => report.Impression = v);
        Adopt("recommendations", result.Recommendations, v => report.Recommendations = v);
        report.AiHighlightsJson = System.Text.Json.JsonSerializer.Serialize(highlights);
        report.UpdatedAt = DateTimeOffset.UtcNow;

        var nextSeq = await db.ReportVersions.Where(v => v.ReportId == report.Id).CountAsync(ct);
        db.ReportVersions.Add(new ReportVersion
        {
            ReportId = report.Id,
            Sequence = nextSeq + 1,
            AuthorUserId = user.Id,
            Action = "generate",
            RulebookId = report.RulebookId,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                report.Indication, report.Technique, report.Comparison,
                report.Findings, report.Impression, report.Recommendations,
                report.AiHighlightsJson,
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, bool> ParseHighlights(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
        }
        catch (System.Text.Json.JsonException)
        {
            return new();
        }
    }
}
