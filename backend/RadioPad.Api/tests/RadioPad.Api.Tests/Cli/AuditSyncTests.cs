using System.Text.Json;
using RadioPad.Cli.Commands;
using Xunit;

namespace RadioPad.Api.Tests.Cli;

/// <summary>
/// Iter-31 G / CLI-009 — exercises <see cref="AuditSync.Filter"/> + the
/// watermark file format. Network-bound paths are excluded; these tests
/// focus on the resumability invariants.
/// </summary>
public class AuditSyncTests
{
    private static JsonElement Event(string id, DateTimeOffset at) =>
        JsonDocument.Parse($"{{\"id\":\"{id}\",\"createdAt\":\"{at:O}\"}}").RootElement;

    [Fact]
    public void Filter_FromEmptyState_WritesAll_AndAdvancesWatermark()
    {
        var t0 = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var events = new[]
        {
            Event("a", t0),
            Event("b", t0.AddSeconds(1)),
            Event("c", t0.AddSeconds(2)),
        };
        var (toWrite, state) = AuditSync.Filter(events, new AuditSync.SyncState(null, null), null, null);

        Assert.Equal(3, toWrite.Count);
        Assert.Equal(t0.AddSeconds(2), state.LastCreatedAt);
        Assert.Equal("c", state.LastEventId);
    }

    [Fact]
    public void Filter_SkipsAlreadySeenEvents()
    {
        var t0 = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var events = new[]
        {
            Event("a", t0),
            Event("b", t0.AddSeconds(1)),
            Event("c", t0.AddSeconds(2)),
        };
        var current = new AuditSync.SyncState(t0.AddSeconds(1), "b");

        var (toWrite, state) = AuditSync.Filter(events, current, null, null);

        Assert.Single(toWrite);
        Assert.Equal("c", toWrite[0].GetProperty("id").GetString());
        Assert.Equal("c", state.LastEventId);
    }

    [Fact]
    public void Filter_RespectsExplicitFromAndToWindow()
    {
        var t0 = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var events = new[]
        {
            Event("a", t0),
            Event("b", t0.AddSeconds(10)),
            Event("c", t0.AddSeconds(20)),
        };
        var (toWrite, _) = AuditSync.Filter(
            events,
            new AuditSync.SyncState(null, null),
            from: t0.AddSeconds(5),
            to: t0.AddSeconds(15));

        Assert.Single(toWrite);
        Assert.Equal("b", toWrite[0].GetProperty("id").GetString());
    }

    [Fact]
    public void State_RoundTrips_Through_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-sync-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AuditSync.SyncState(DateTimeOffset.Parse("2026-05-01T10:00:00Z"), "abc");
            AuditSync.WriteState(path, original);
            var read = AuditSync.ReadState(path);
            Assert.Equal(original.LastCreatedAt, read.LastCreatedAt);
            Assert.Equal(original.LastEventId, read.LastEventId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
