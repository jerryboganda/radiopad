using System;
using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// PRD §18.1 / F10 — product-KPI computation. Focused on the reports-per-hour throughput metric
/// and the divide-by-zero guards the whole service depends on.
/// </summary>
public class AnalyticsServiceTests
{
    private static UsageSummary EmptyUsage() =>
        new(0, 0, 0, 0, 0, 0, 0, Array.Empty<UsageByProvider>());

    [Fact]
    public async Task ComputeAsync_ReportsPerHour_Is_Completed_Over_Window_Hours()
    {
        var from = new DateTimeOffset(2026, 07, 18, 08, 00, 00, TimeSpan.Zero);
        var to = from.AddHours(2);
        var raw = new AnalyticsRawData { CompletedReports = 10 };

        var summary = await new AnalyticsService().ComputeAsync(
            Guid.NewGuid(), from, to, raw, EmptyUsage(), CancellationToken.None);

        Assert.Equal(5.0, summary.Product.ReportsPerHour, 3); // 10 reports / 2 h
    }

    [Fact]
    public async Task ComputeAsync_ReportsPerHour_Is_Zero_For_Empty_Window()
    {
        var from = new DateTimeOffset(2026, 07, 18, 08, 00, 00, TimeSpan.Zero);
        var raw = new AnalyticsRawData { CompletedReports = 4 };

        var summary = await new AnalyticsService().ComputeAsync(
            Guid.NewGuid(), from, from, raw, EmptyUsage(), CancellationToken.None);

        Assert.Equal(0.0, summary.Product.ReportsPerHour); // no divide-by-zero on a zero-length window
    }

    [Fact]
    public async Task ComputeAsync_Populates_Window_And_Survives_Empty_Raw()
    {
        var from = new DateTimeOffset(2026, 07, 18, 08, 00, 00, TimeSpan.Zero);
        var to = from.AddHours(1);

        var summary = await new AnalyticsService().ComputeAsync(
            Guid.NewGuid(), from, to, new AnalyticsRawData(), EmptyUsage(), CancellationToken.None);

        Assert.Equal(from, summary.Window.From);
        Assert.Equal(to, summary.Window.To);
        Assert.Equal(0.0, summary.Product.ReportsPerHour);      // no completed reports
        Assert.Equal(0.0, summary.Product.DraftAcceptanceRate); // no AI drafts → guarded
    }
}
