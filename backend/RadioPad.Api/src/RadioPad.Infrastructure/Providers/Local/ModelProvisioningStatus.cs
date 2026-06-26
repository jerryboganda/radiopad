using System.Collections.Concurrent;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>Lifecycle phase of a model download/install, surfaced to the UI.</summary>
public enum ProvisionState { NotStarted, Downloading, Verifying, Extracting, Installing, Ready, Failed }

/// <summary>Immutable progress snapshot for one model (lock-free read for the controller).</summary>
public sealed record ModelProvisionSnapshot(
    string ModelId,
    ProvisionState State,
    long BytesDownloaded,
    long TotalBytes,
    string? Error,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? UpdatedUtc);

/// <summary>
/// In-memory tracker of per-model download/install progress, written by
/// <see cref="SttModelProvisioner"/> (both the startup auto-download and the
/// manual download endpoint) and read by the local-models controller for polling.
/// </summary>
public interface IModelProvisioningStatus
{
    void SetState(string modelId, ProvisionState state, string? error = null);
    void SetTotal(string modelId, long totalBytes);
    void ReportBytes(string modelId, long bytesDownloaded);
    ModelProvisionSnapshot? Get(string modelId);
    IReadOnlyCollection<ModelProvisionSnapshot> All();
}

/// <summary>Singleton <see cref="IModelProvisioningStatus"/> over a concurrent map.</summary>
public sealed class ModelProvisioningStatus : IModelProvisioningStatus
{
    private readonly ConcurrentDictionary<string, ModelProvisionSnapshot> _map = new(StringComparer.Ordinal);

    public void SetState(string modelId, ProvisionState state, string? error = null) =>
        _map.AddOrUpdate(
            modelId,
            _ => new ModelProvisionSnapshot(
                modelId, state, 0, 0, error,
                state == ProvisionState.Downloading ? DateTimeOffset.UtcNow : null,
                DateTimeOffset.UtcNow),
            (_, prev) => prev with
            {
                State = state,
                Error = error,
                BytesDownloaded = state == ProvisionState.NotStarted ? 0 : prev.BytesDownloaded,
                TotalBytes = state == ProvisionState.NotStarted ? 0 : prev.TotalBytes,
                StartedUtc = state == ProvisionState.Downloading && prev.StartedUtc is null
                    ? DateTimeOffset.UtcNow
                    : prev.StartedUtc,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });

    public void SetTotal(string modelId, long totalBytes) =>
        _map.AddOrUpdate(
            modelId,
            _ => new ModelProvisionSnapshot(
                modelId, ProvisionState.Downloading, 0, totalBytes, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            (_, prev) => prev with { TotalBytes = totalBytes, UpdatedUtc = DateTimeOffset.UtcNow });

    public void ReportBytes(string modelId, long bytesDownloaded) =>
        _map.AddOrUpdate(
            modelId,
            _ => new ModelProvisionSnapshot(
                modelId, ProvisionState.Downloading, bytesDownloaded, 0, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            (_, prev) => prev with
            {
                BytesDownloaded = bytesDownloaded,
                State = ProvisionState.Downloading,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });

    public ModelProvisionSnapshot? Get(string modelId) =>
        _map.TryGetValue(modelId, out var s) ? s : null;

    public IReadOnlyCollection<ModelProvisionSnapshot> All() => _map.Values.ToArray();
}

/// <summary>
/// No-op sink so <see cref="SttModelProvisioner"/> works when constructed without
/// DI (its unit tests pass only a logger).
/// </summary>
internal sealed class NullModelProvisioningStatus : IModelProvisioningStatus
{
    public static readonly NullModelProvisioningStatus Instance = new();
    public void SetState(string modelId, ProvisionState state, string? error = null) { }
    public void SetTotal(string modelId, long totalBytes) { }
    public void ReportBytes(string modelId, long bytesDownloaded) { }
    public ModelProvisionSnapshot? Get(string modelId) => null;
    public IReadOnlyCollection<ModelProvisionSnapshot> All() => Array.Empty<ModelProvisionSnapshot>();
}
