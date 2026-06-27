using RadioPad.Application.Stt;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// Short-lived, report-scoped store for async cross-check jobs. In-memory and
/// non-durable across an API restart (acceptable: a job lives for seconds and the
/// client simply re-submits if it vanishes). Lets the controller return 202 + a
/// job id and the client poll for the result while a processing badge shows.
/// </summary>
public interface ICrossCheckJobStore
{
    /// <summary>Create and store a new queued job, returning it (with a fresh id).</summary>
    CrossCheckJob Create();

    /// <summary>Get a job by id, or null if unknown/expired.</summary>
    CrossCheckJob? Get(string id);

    /// <summary>Persist mutations to a job obtained from <see cref="Create"/>/<see cref="Get"/>.</summary>
    void Update(CrossCheckJob job);
}
