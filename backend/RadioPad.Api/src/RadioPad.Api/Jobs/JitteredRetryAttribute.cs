using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N1 — RadioPad's single, global Hangfire retry policy. On a job failure it
/// reschedules with exponential backoff plus jitter
/// (<c>30s * 2^attempt + random(0..30s)</c>) up to <see cref="Attempts"/> times,
/// after which the job is left in the Hangfire <see cref="FailedState"/> — the
/// Failed set is our DLQ (payloads are ids only, so it is inherently
/// PHI-redacted). Registered once in <see cref="HangfireSetup"/>, which also
/// removes Hangfire's built-in 10-attempt <c>AutomaticRetryAttribute</c> so this
/// is the only retry filter in effect. Mirrors the state-election contract of
/// Hangfire's own retry filter.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class JitteredRetryAttribute : JobFilterAttribute, IElectStateFilter
{
    /// <summary>Maximum retry attempts before the job is sent to the Failed set.</summary>
    public const int Attempts = 5;

    private static readonly Random Rng = new();

    public void OnStateElection(ElectStateContext context)
    {
        // We only react to a candidate Failed state; every other transition
        // (Succeeded, Deleted, Scheduled from another filter) is left untouched.
        if (context.CandidateState is not FailedState) return;

        var retryAttempt = context.GetJobParameter<int>("RetryCount", allowStale: true) + 1;
        if (retryAttempt > Attempts)
        {
            // Exhausted — leave the Failed state in place (DLQ).
            return;
        }

        context.SetJobParameter("RetryCount", retryAttempt);

        int jitterSeconds;
        lock (Rng) { jitterSeconds = Rng.Next(0, 31); } // 0..30s inclusive
        var delaySeconds = 30.0 * Math.Pow(2, retryAttempt) + jitterSeconds;
        var delay = TimeSpan.FromSeconds(delaySeconds);

        context.CandidateState = new ScheduledState(delay)
        {
            Reason = $"Retry attempt {retryAttempt} of {Attempts}: scheduled in ~{delay.TotalSeconds:0}s (exponential backoff + jitter).",
        };
    }
}
