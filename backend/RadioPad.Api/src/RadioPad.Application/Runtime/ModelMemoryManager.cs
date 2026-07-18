namespace RadioPad.Application.Runtime;

/// <summary>Category of a memory-managed on-device model (drives eviction priority — STT first).</summary>
public enum ManagedModelKind
{
    Stt,
    Other,
    Llm,
}

/// <summary>A model whose native/sidecar residency the memory manager coordinates.</summary>
public interface IManagedModel
{
    string Id { get; }
    ManagedModelKind Kind { get; }
    long EstimatedResidentBytes { get; }
    bool IsResident { get; }
    Task LoadAsync(CancellationToken ct);
    Task UnloadAsync(CancellationToken ct);
}

/// <summary>Per-model residency line for the status/debug panel.</summary>
public sealed record ModelResidency(string Id, ManagedModelKind Kind, long EstimatedBytes, bool Resident);

/// <summary>A snapshot of on-device model memory for the debug/status panel (brief §4.4).</summary>
public sealed record MemorySnapshot(
    long CeilingBytes,
    long ReservedBytes,
    long ResidentBytes,
    long AvailableBytes,
    IReadOnlyList<ModelResidency> Models);

/// <summary>
/// Brief §4.4 — coordinates MedASR + MedGemma lifecycles so combined resident model memory never
/// breaches the ≤5 GB ceiling (§1). It evicts STT before the LLM, refuses to load a model that
/// cannot fit even after eviction, and offers a low-memory mode that unloads STT for the duration of
/// a formatting call. The actual load/unload is delegated to <see cref="IManagedModel"/> (native
/// engine or sidecar); this type owns only the budget/eviction policy. CPU-only is enforced
/// elsewhere (LocalSttModels.ResolveProvider).
/// </summary>
public sealed class ModelMemoryManager
{
    /// <summary>Default hard ceiling for all RadioPad AI models combined (brief §1).</summary>
    public const long DefaultCeilingBytes = 5L << 30;

    /// <summary>Default OS + desktop-shell reservation kept outside the model budget (brief §4.4 ~1 GB).</summary>
    public const long DefaultReservedBytes = 1L << 30;

    private readonly object _gate = new();
    private readonly Dictionary<string, IManagedModel> _models = new(StringComparer.Ordinal);
    private readonly long _ceilingBytes;
    private readonly long _reservedBytes;
    private readonly bool _lowMemoryMode;

    public ModelMemoryManager(
        long ceilingBytes = DefaultCeilingBytes,
        long reservedBytes = DefaultReservedBytes,
        bool lowMemoryMode = false)
    {
        _ceilingBytes = ceilingBytes;
        _reservedBytes = reservedBytes;
        _lowMemoryMode = lowMemoryMode;
    }

    private long Budget => _ceilingBytes - _reservedBytes;

    public void Register(IManagedModel model)
    {
        lock (_gate)
            _models[model.Id] = model;
    }

    /// <summary>Ensures <paramref name="id"/> is resident, evicting lower-priority resident models
    /// (STT first) as needed. Returns false if it cannot fit even after evicting everything else.</summary>
    public async Task<bool> EnsureResidentAsync(string id, CancellationToken ct)
    {
        IManagedModel model;
        List<IManagedModel> evictionCandidates;

        lock (_gate)
        {
            if (!_models.TryGetValue(id, out model!))
                return false;
            if (model.IsResident)
                return true;

            evictionCandidates = _models.Values
                .Where(m => m.IsResident && m.Id != id)
                .OrderBy(m => (int)m.Kind) // Stt(0) evicted before Other(1) before Llm(2)
                .ToList();
        }

        if (model.EstimatedResidentBytes > Budget)
            return false; // cannot fit on its own, no point evicting

        var index = 0;
        while (ResidentBytes() + model.EstimatedResidentBytes > Budget)
        {
            if (index >= evictionCandidates.Count)
                return false;
            await evictionCandidates[index++].UnloadAsync(ct);
        }

        await model.LoadAsync(ct);
        return true;
    }

    /// <summary>Explicitly unloads a model (e.g. the manager, or a caller freeing memory).</summary>
    public async Task UnloadAsync(string id, CancellationToken ct)
    {
        IManagedModel? model;
        lock (_gate)
            _models.TryGetValue(id, out model);
        if (model is { IsResident: true })
            await model.UnloadAsync(ct);
    }

    /// <summary>
    /// Runs a formatting delegate. In low-memory mode every resident STT model is unloaded for the
    /// duration and reloaded afterwards, so the peak never has STT + a large LLM co-resident.
    /// </summary>
    public async Task<T> RunFormattingAsync<T>(Func<CancellationToken, Task<T>> formatting, CancellationToken ct)
    {
        if (!_lowMemoryMode)
            return await formatting(ct);

        List<IManagedModel> toReload;
        lock (_gate)
            toReload = _models.Values.Where(m => m.Kind == ManagedModelKind.Stt && m.IsResident).ToList();

        foreach (var m in toReload)
            await m.UnloadAsync(ct);

        try
        {
            return await formatting(ct);
        }
        finally
        {
            foreach (var m in toReload)
                await m.LoadAsync(ct);
        }
    }

    /// <summary>Current on-device model memory for the debug/status panel.</summary>
    public MemorySnapshot Snapshot()
    {
        lock (_gate)
        {
            var resident = _models.Values.Where(m => m.IsResident).Sum(m => m.EstimatedResidentBytes);
            var models = _models.Values
                .Select(m => new ModelResidency(m.Id, m.Kind, m.EstimatedResidentBytes, m.IsResident))
                .ToList();
            return new MemorySnapshot(_ceilingBytes, _reservedBytes, resident, Budget - resident, models);
        }
    }

    private long ResidentBytes()
    {
        lock (_gate)
            return _models.Values.Where(m => m.IsResident).Sum(m => m.EstimatedResidentBytes);
    }
}
