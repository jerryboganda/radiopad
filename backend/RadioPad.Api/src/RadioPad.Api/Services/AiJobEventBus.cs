using System.Collections.Concurrent;
using System.Threading.Channels;
using RadioPad.Domain.Entities;

namespace RadioPad.Api.Services;

/// <summary>
/// One streamed progress tick for a running AI job (contract A). <see cref="Tokens"/>
/// is the cumulative output-token count for the CURRENT provider attempt (it resets on
/// failover — see <see cref="AiJobRegistry.UpdateProgress"/>). <see cref="Percent"/> is
/// null on every streaming path in v1 (indeterminate — no provider reports an honest
/// expected-total); the field stays nullable so a future determinate source can light it
/// up without a wire change. <see cref="PartialDelta"/> is null on a pure progress tick.
/// </summary>
public sealed record AiJobProgressEvent(
    Guid JobId, Guid TenantId, Guid UserId, Guid ReportId,
    string Kind, string Mode, int Tokens,
    double? Percent,
    string? PartialDelta);

/// <summary>An inbox/notification snapshot destined for one recipient (NOTIF producer).</summary>
public sealed record NotificationEvent(Guid TenantId, Guid RecipientUserId, object Snapshot);

/// <summary>One bus event as seen by a subscriber. EventType ∈ "job"|"progress"|"partial"|"notification".</summary>
public sealed record AiJobBusEvent(string EventType, object Payload);

public interface IAiJobEventBus
{
    /// <summary>Publishes a terminal job transition as a JobSummary-shaped "job" event patch.</summary>
    void PublishTerminal(AiJob job);

    /// <summary>Publishes a "progress" event, plus a "partial" event when <c>PartialDelta</c> is non-null.</summary>
    void PublishProgress(AiJobProgressEvent evt);

    /// <summary>Publishes a "notification" event to the recipient user.</summary>
    void PublishNotification(NotificationEvent evt);

    /// <summary>
    /// Subscribes to the bus. A subscriber receives an event when
    /// <c>(TenantId is null || TenantId == evt.TenantId) &amp;&amp; (UserId is null || UserId == evt.UserId)</c>;
    /// pass <c>null</c> for both to firehose every tenant (internal use only — the NOTIF producer).
    /// </summary>
    IAiJobEventSubscription Subscribe(Guid? tenantId, Guid? userId);
}

public interface IAiJobEventSubscription : IDisposable
{
    ChannelReader<AiJobBusEvent> Reader { get; }
}

/// <summary>
/// In-process fan-out bus feeding the SSE stream (PR-B1). Publishers (the coordinator,
/// the NOTIF producer) push events; each SSE connection holds one <see cref="Subscribe"/>
/// subscription and drains its own bounded channel.
///
/// <para><b>User-scoped by design.</b> job/progress/partial events filter on BOTH
/// tenantId AND userId — a user sees only their OWN jobs, never the whole tenant.
/// Justification: (a) it mirrors <c>JobsController.List</c>'s <c>UserId == user.Id</c>
/// working-set semantics (the stream replaces that poll); (b) <c>partial</c> events carry
/// raw clinical model output, and fanning PHI-bearing partial text to every ReportsEdit
/// user in the tenant violates the PHI-minimization posture for zero UX gain — a colleague
/// on the same report already has the tenant-scoped report poll + refetch path.</para>
///
/// <para><b>Never blocks a publisher.</b> Each subscription channel is a bounded
/// DropOldest channel (<c>AiJobs:SseSubscriberBuffer</c>, default 64), so a slow SSE
/// client loses its oldest events rather than back-pressuring the job that produced them
/// — the poll fallback + "first terminal wins" reducer make that loss safe.</para>
/// </summary>
public sealed class AiJobEventBus : IAiJobEventBus
{
    private sealed record Subscriber(Guid? TenantId, Guid? UserId, Channel<AiJobBusEvent> Channel);

    private readonly ConcurrentDictionary<Guid, Subscriber> _subs = new();
    private readonly int _bufferCapacity;

    public AiJobEventBus(IConfiguration config)
    {
        var cap = config.GetValue<int?>("AiJobs:SseSubscriberBuffer") ?? 64;
        if (cap < 1) cap = 64;
        _bufferCapacity = cap;
    }

    /// <summary>Live subscription count — test-only visibility into fan-out bookkeeping.</summary>
    internal int SubscriberCount => _subs.Count;

    public IAiJobEventSubscription Subscribe(Guid? tenantId, Guid? userId)
    {
        var channel = Channel.CreateBounded<AiJobBusEvent>(new BoundedChannelOptions(_bufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subs[id] = new Subscriber(tenantId, userId, channel);
        return new Subscription(this, id, channel.Reader);
    }

    private void Remove(Guid id)
    {
        if (_subs.TryRemove(id, out var sub))
            sub.Channel.Writer.TryComplete();
    }

    public void PublishTerminal(AiJob job)
    {
        // Build the JobSummary-shaped patch HERE — no DB access, no report descriptor.
        // The client merges this into rows it already holds (or refetches the list for
        // an unknown id). Exactly the JobsController.List row shape minus `report`.
        var payload = new
        {
            jobId = job.Id,
            // tenantId/userId are additive over the pure JobSummary shape: the SSE client
            // already filters by tenant+user server-side and ignores unknown fields, while the
            // NOTIF producer's firehose subscription needs them to route the derived
            // AiJob-category notification to the owning recipient (PR-N3).
            tenantId = job.TenantId,
            userId = job.UserId,
            kind = job.Kind,
            mode = job.Mode,
            status = job.Status,
            errorKind = job.ErrorKind,
            error = job.Error,
            attempt = job.Attempt,
            retryOfJobId = job.RetryOfJobId,
            reportId = job.ReportId,
            createdAt = job.CreatedAt,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            elapsedMs = (long)((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.CreatedAt).TotalMilliseconds,
        };
        Deliver(job.TenantId, job.UserId, new AiJobBusEvent("job", payload));
    }

    public void PublishProgress(AiJobProgressEvent evt)
    {
        Deliver(evt.TenantId, evt.UserId,
            new AiJobBusEvent("progress", new { jobId = evt.JobId, tokens = evt.Tokens, percent = evt.Percent }));
        if (evt.PartialDelta is not null)
            Deliver(evt.TenantId, evt.UserId,
                new AiJobBusEvent("partial", new { jobId = evt.JobId, delta = evt.PartialDelta }));
    }

    public void PublishNotification(NotificationEvent evt) =>
        Deliver(evt.TenantId, evt.RecipientUserId, new AiJobBusEvent("notification", evt.Snapshot));

    private void Deliver(Guid tenantId, Guid userId, AiJobBusEvent busEvent)
    {
        foreach (var sub in _subs.Values)
        {
            if ((sub.TenantId is null || sub.TenantId == tenantId)
                && (sub.UserId is null || sub.UserId == userId))
            {
                // TryWrite never blocks: the channel is bounded/DropOldest, so a slow
                // subscriber silently drops its oldest event instead of stalling this
                // publisher (which may be a live job's streaming read loop).
                sub.Channel.Writer.TryWrite(busEvent);
            }
        }
    }

    private sealed class Subscription : IAiJobEventSubscription
    {
        private readonly AiJobEventBus _owner;
        private readonly Guid _id;
        private int _disposed;

        public Subscription(AiJobEventBus owner, Guid id, ChannelReader<AiJobBusEvent> reader)
        {
            _owner = owner;
            _id = id;
            Reader = reader;
        }

        public ChannelReader<AiJobBusEvent> Reader { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.Remove(_id);
        }
    }
}
