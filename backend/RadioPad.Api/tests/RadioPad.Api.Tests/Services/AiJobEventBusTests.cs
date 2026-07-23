using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// Fan-out + filtering tests for <see cref="AiJobEventBus"/> — the in-process bus behind
/// the SSE stream. The bus MUST never block a publisher (a live job's read loop) and MUST
/// scope job/progress/partial events to the owning tenant+user (PHI-bearing partials must
/// not fan out tenant-wide).
/// </summary>
public class AiJobEventBusTests
{
    private static AiJobEventBus NewBus(int? buffer = null)
    {
        var settings = new Dictionary<string, string?>();
        if (buffer is int b) settings["AiJobs:SseSubscriberBuffer"] = b.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AiJobEventBus(config);
    }

    private static AiJobProgressEvent Progress(Guid tenantId, Guid userId, int tokens) =>
        new(Guid.NewGuid(), tenantId, userId, Guid.NewGuid(), "ai", "impression", tokens, null, null);

    private static int TokensOf(AiJobBusEvent evt)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt.Payload));
        return doc.RootElement.GetProperty("tokens").GetInt32();
    }

    [Fact]
    public void Subscribe_TenantUserFilterApplied()
    {
        var bus = NewBus();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = bus.Subscribe(tenant, user);

        // Matching tenant+user → delivered.
        bus.PublishProgress(Progress(tenant, user, tokens: 5));
        Assert.True(sub.Reader.TryRead(out var mine));
        Assert.Equal("progress", mine!.EventType);
        Assert.Equal(5, TokensOf(mine));

        // Same tenant, different user → NOT delivered.
        bus.PublishProgress(Progress(tenant, Guid.NewGuid(), tokens: 9));
        Assert.False(sub.Reader.TryRead(out _));

        // Different tenant, same user id → NOT delivered.
        bus.PublishProgress(Progress(Guid.NewGuid(), user, tokens: 9));
        Assert.False(sub.Reader.TryRead(out _));
    }

    [Fact]
    public void PublishProgress_WithDelta_EmitsProgressThenPartial()
    {
        var bus = NewBus();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = bus.Subscribe(tenant, user);

        var jobId = Guid.NewGuid();
        bus.PublishProgress(new AiJobProgressEvent(jobId, tenant, user, Guid.NewGuid(), "ai", "impression", 4, null, "hello"));

        Assert.True(sub.Reader.TryRead(out var first));
        Assert.Equal("progress", first!.EventType);
        Assert.True(sub.Reader.TryRead(out var second));
        Assert.Equal("partial", second!.EventType);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(second.Payload));
        Assert.Equal("hello", doc.RootElement.GetProperty("delta").GetString());
    }

    [Fact]
    public void PublishTerminal_EmitsJobEvent_WithJobSummaryShape()
    {
        var bus = NewBus();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = bus.Subscribe(tenant, user);

        var job = new AiJob
        {
            TenantId = tenant, UserId = user, ReportId = Guid.NewGuid(),
            Kind = "ai", Mode = "impression", Status = "ok", CompletedAt = DateTimeOffset.UtcNow,
        };
        bus.PublishTerminal(job);

        Assert.True(sub.Reader.TryRead(out var evt));
        Assert.Equal("job", evt!.EventType);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt.Payload));
        Assert.Equal(job.Id, doc.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void BoundedChannel_DropsOldestOnOverflow()
    {
        const int buffer = 4;
        var bus = NewBus(buffer);
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var sub = bus.Subscribe(tenant, user);

        // Publish buffer+5 events; a full DropOldest channel keeps only the last `buffer`.
        for (var i = 0; i < buffer + 5; i++)
            bus.PublishProgress(Progress(tenant, user, tokens: i));

        var drained = new List<int>();
        while (sub.Reader.TryRead(out var evt)) drained.Add(TokensOf(evt!));

        Assert.Equal(buffer, drained.Count);
        Assert.DoesNotContain(0, drained);                 // oldest dropped
        Assert.Contains(buffer + 5 - 1, drained);          // newest survived
    }

    [Fact]
    public void Dispose_RemovesSubscriber()
    {
        var bus = NewBus();
        Assert.Equal(0, bus.SubscriberCount);

        var sub = bus.Subscribe(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(1, bus.SubscriberCount);

        sub.Dispose();
        Assert.Equal(0, bus.SubscriberCount);

        sub.Dispose(); // idempotent
        Assert.Equal(0, bus.SubscriberCount);
    }

    [Fact]
    public void FirehoseSubscription_SeesAllTenants()
    {
        var bus = NewBus();
        using var firehose = bus.Subscribe(null, null); // NOTIF-producer style

        bus.PublishProgress(Progress(Guid.NewGuid(), Guid.NewGuid(), tokens: 1));
        bus.PublishProgress(Progress(Guid.NewGuid(), Guid.NewGuid(), tokens: 2));

        var seen = new List<int>();
        while (firehose.Reader.TryRead(out var evt)) seen.Add(TokensOf(evt!));

        Assert.Equal(new[] { 1, 2 }, seen);
    }
}
