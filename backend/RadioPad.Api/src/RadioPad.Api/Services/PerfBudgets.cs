using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-33 PERF-004 — OpenTelemetry histograms backing the continuous
/// P95 budget SLOs. The static <see cref="Meter"/> is named
/// <c>RadioPad.PerfBudgets</c>; <c>Program.cs</c> registers it with the
/// OpenTelemetry metrics pipeline (OTLP exporter when
/// <c>RADIOPAD_OTEL_OTLP_ENDPOINT</c> is set, otherwise the metrics live
/// only in-process and can be observed via a
/// <see cref="MeterListener"/> for tests).
///
/// PRD targets:
///   validate   &lt; 250 ms P95
///   sign       &lt; 500 ms P95
///   AI draft   &lt; 4 s   P95
///   QIDO       &lt; 600 ms P95
/// </summary>
public static class PerfBudgets
{
    public const string MeterName = "RadioPad.PerfBudgets";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Histogram<double> ValidateDurationMs = Meter.CreateHistogram<double>(
        name: "radiopad.report.validate.duration_ms",
        unit: "ms",
        description: "ReportingService.ValidateAsync wall-clock duration.");

    public static readonly Histogram<double> SignDurationMs = Meter.CreateHistogram<double>(
        name: "radiopad.report.sign.duration_ms",
        unit: "ms",
        description: "POST /api/reports/{id}/sign wall-clock duration.");

    public static readonly Histogram<double> AiDraftDurationMs = Meter.CreateHistogram<double>(
        name: "radiopad.ai.draft.duration_ms",
        unit: "ms",
        description: "IAiGateway.RouteAsync wall-clock duration (AI draft / suggest / cleanup).");

    public static readonly Histogram<double> DicomQidoDurationMs = Meter.CreateHistogram<double>(
        name: "radiopad.dicom.qido.duration_ms",
        unit: "ms",
        description: "DICOMweb QIDO-RS study search wall-clock duration.");

    public static readonly Histogram<double> ApiRequestDurationMs = Meter.CreateHistogram<double>(
        name: "radiopad.api.request.duration_ms",
        unit: "ms",
        description: "Per-route HTTP request wall-clock duration (tagged route, tenant, status).");

    /// <summary>
    /// Times <paramref name="work"/> with a <see cref="Stopwatch"/> and
    /// records the elapsed milliseconds on <paramref name="histogram"/>.
    /// Records the duration on both the success and failure paths so
    /// breaches show up in the SLO regardless of outcome.
    /// </summary>
    public static async Task<T> RecordAsync<T>(
        Histogram<double> histogram,
        Func<Task<T>> work,
        params KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(histogram);
        ArgumentNullException.ThrowIfNull(work);
        var sw = Stopwatch.StartNew();
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            histogram.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }

    /// <summary>Same as <see cref="RecordAsync{T}"/> but for tasks with no result.</summary>
    public static async Task RecordAsync(
        Histogram<double> histogram,
        Func<Task> work,
        params KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(histogram);
        ArgumentNullException.ThrowIfNull(work);
        var sw = Stopwatch.StartNew();
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            histogram.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }
}
