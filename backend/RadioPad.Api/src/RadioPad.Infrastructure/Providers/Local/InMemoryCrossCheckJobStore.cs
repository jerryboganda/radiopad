using System.Collections.Concurrent;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// In-memory <see cref="ICrossCheckJobStore"/>. Jobs are short-lived (seconds);
/// a 10-minute TTL sweep keeps the map from growing. Non-durable across restart
/// by design — the client re-submits if a job id 404s.
/// </summary>
public sealed class InMemoryCrossCheckJobStore : ICrossCheckJobStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CrossCheckJob> _jobs = new();

    public CrossCheckJob Create()
    {
        Evict();
        var job = new CrossCheckJob
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _jobs[job.Id] = job;
        return job;
    }

    public CrossCheckJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public void Update(CrossCheckJob job) => _jobs[job.Id] = job;

    private void Evict()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var kv in _jobs)
            if (kv.Value.CreatedAt < cutoff)
                _jobs.TryRemove(kv.Key, out _);
    }
}
