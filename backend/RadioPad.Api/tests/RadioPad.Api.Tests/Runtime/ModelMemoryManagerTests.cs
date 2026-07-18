using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Runtime;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §4.4 / §1 — the load/unload manager keeps combined resident model memory under the ≤5 GB
/// ceiling (CPU-only), evicting STT first and offering a low-memory mode that unloads STT during
/// formatting.
/// </summary>
public class ModelMemoryManagerTests
{
    private const long GB = 1L << 30;

    private sealed class FakeModel : IManagedModel
    {
        public FakeModel(string id, ManagedModelKind kind, long bytes)
        {
            Id = id;
            Kind = kind;
            EstimatedResidentBytes = bytes;
        }

        public string Id { get; }
        public ManagedModelKind Kind { get; }
        public long EstimatedResidentBytes { get; }
        public bool IsResident { get; private set; }
        public int Loads { get; private set; }
        public int Unloads { get; private set; }

        public Task LoadAsync(CancellationToken ct) { IsResident = true; Loads++; return Task.CompletedTask; }
        public Task UnloadAsync(CancellationToken ct) { IsResident = false; Unloads++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Both_Models_Fit_Under_Budget_Stay_Resident()
    {
        var mgr = new ModelMemoryManager(ceilingBytes: 5 * GB, reservedBytes: 1 * GB);
        var stt = new FakeModel("medasr", ManagedModelKind.Stt, GB / 2);
        var llm = new FakeModel("medgemma", ManagedModelKind.Llm, (long)(2.8 * GB));
        mgr.Register(stt);
        mgr.Register(llm);

        Assert.True(await mgr.EnsureResidentAsync("medasr", CancellationToken.None));
        Assert.True(await mgr.EnsureResidentAsync("medgemma", CancellationToken.None));
        Assert.True(stt.IsResident);
        Assert.True(llm.IsResident);
    }

    [Fact]
    public async Task Evicts_Stt_When_Llm_Would_Exceed_Budget()
    {
        var mgr = new ModelMemoryManager(ceilingBytes: 4 * GB, reservedBytes: 1 * GB); // budget = 3 GB
        var stt = new FakeModel("medasr", ManagedModelKind.Stt, 1 * GB);
        var llm = new FakeModel("medgemma", ManagedModelKind.Llm, (long)(2.5 * GB));
        mgr.Register(stt);
        mgr.Register(llm);

        Assert.True(await mgr.EnsureResidentAsync("medasr", CancellationToken.None));
        Assert.True(await mgr.EnsureResidentAsync("medgemma", CancellationToken.None));

        Assert.False(stt.IsResident);   // evicted to make room
        Assert.True(llm.IsResident);
    }

    [Fact]
    public async Task Returns_False_When_Model_Cannot_Fit_Even_After_Eviction()
    {
        var mgr = new ModelMemoryManager(ceilingBytes: 3 * GB, reservedBytes: 1 * GB); // budget = 2 GB
        var llm = new FakeModel("big", ManagedModelKind.Llm, (long)(2.5 * GB));
        mgr.Register(llm);

        Assert.False(await mgr.EnsureResidentAsync("big", CancellationToken.None));
        Assert.False(llm.IsResident);
    }

    [Fact]
    public async Task LowMemoryMode_Unloads_Stt_During_Formatting_Then_Reloads()
    {
        var mgr = new ModelMemoryManager(ceilingBytes: 5 * GB, reservedBytes: 1 * GB, lowMemoryMode: true);
        var stt = new FakeModel("medasr", ManagedModelKind.Stt, GB / 2);
        mgr.Register(stt);
        await mgr.EnsureResidentAsync("medasr", CancellationToken.None);

        bool residentDuringFormatting = true;
        await mgr.RunFormattingAsync(_ =>
        {
            residentDuringFormatting = stt.IsResident;
            return Task.FromResult(0);
        }, CancellationToken.None);

        Assert.False(residentDuringFormatting);   // STT unloaded while the LLM formats
        Assert.True(stt.IsResident);               // reloaded afterwards
    }

    [Fact]
    public async Task Snapshot_Reports_Ceiling_Reserved_Resident_And_Available()
    {
        var mgr = new ModelMemoryManager(ceilingBytes: 5 * GB, reservedBytes: 1 * GB);
        var stt = new FakeModel("medasr", ManagedModelKind.Stt, GB / 2);
        mgr.Register(stt);
        await mgr.EnsureResidentAsync("medasr", CancellationToken.None);

        var snap = mgr.Snapshot();

        Assert.Equal(5 * GB, snap.CeilingBytes);
        Assert.Equal(1 * GB, snap.ReservedBytes);
        Assert.Equal(GB / 2, snap.ResidentBytes);
        Assert.Equal(4 * GB - GB / 2, snap.AvailableBytes); // budget (4 GB) − resident
        Assert.Contains(snap.Models, m => m.Id == "medasr" && m.Resident);
    }
}
