using RadioPad.Application.Abstractions;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD §18.1/§18.2 — computes all product and governance KPIs from raw
/// data-access counts. The controller layer queries the database and passes
/// pre-fetched counts so this service stays independent of EF Core.
/// </summary>
public sealed class AnalyticsService
{
    /// <summary>
    /// Compute the full <see cref="AnalyticsSummary"/> from pre-fetched raw
    /// counts. The caller is responsible for scoping all queries to the
    /// correct tenant + time window before calling this method.
    /// </summary>
    public async Task<AnalyticsSummary> ComputeAsync(
        Guid tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        AnalyticsRawData raw,
        UsageSummary aiUsage,
        CancellationToken ct)
    {
        await Task.CompletedTask; // all computation is synchronous

        // ── Product KPIs (§18.1) ────────────────────────────────────────

        // 1. Draft acceptance rate — AI drafts that reached Acknowledged / total AI drafts
        var draftAcceptanceRate = raw.TotalAiGeneratedReports == 0
            ? 0.0
            : (double)raw.AiDraftsAcknowledged / raw.TotalAiGeneratedReports;

        // 2. Impression acceptance rate — < 20% edit distance (proxy: acknowledged impressions vs generated)
        var impressionAcceptanceRate = raw.TotalAiGeneratedReports == 0
            ? 0.0
            : (double)raw.ImpressionsLightlyEdited / raw.TotalAiGeneratedReports;

        // 3. Time saved per report — average seconds from creation to acknowledgement
        var timeSavedPerReport = raw.AcknowledgedReportCount == 0
            ? 0.0
            : raw.TotalSecondsToAcknowledge / raw.AcknowledgedReportCount;

        // 4. Validation pass rate — reports passing all blockers at first validation / total
        var validationPassRate = raw.TotalReports == 0
            ? 0.0
            : (double)raw.ValidatedReports / raw.TotalReports;

        // 5. Contradiction detection rate — negation_conflict + laterality findings per 100 reports
        var contradictionDetectionRate = raw.TotalReports == 0
            ? 0.0
            : (double)raw.ContradictionFindings / raw.TotalReports * 100.0;

        // 6. Edit distance — average character edit ratio
        var editDistance = raw.EditDistanceSampleCount == 0
            ? 0.0
            : raw.EditDistanceSum / raw.EditDistanceSampleCount;

        // 7. Active radiologists — distinct users with role=Radiologist active in period
        var activeRadiologists = raw.ActiveRadiologists;

        // 8. Rulebook adoption — reports with approved rulebook / total
        var rulebookAdoption = raw.TotalReports == 0
            ? 0.0
            : (double)raw.ReportsWithRulebook / raw.TotalReports;

        // 9. Provider cost per report — total AI cost / completed reports
        var providerCostPerReport = raw.CompletedReports == 0
            ? 0.0m
            : aiUsage.CostTotalUsd / raw.CompletedReports;

        // 10. Turnaround time impact — median time-to-acknowledge in seconds
        var turnaroundTimeImpact = raw.MedianSecondsToAcknowledge;

        // 11. Average quality score
        var avgQualityScore = raw.QualityScoreCount == 0
            ? (double?)null
            : raw.QualityScoreSum / raw.QualityScoreCount;

        // 12. Reports per hour (F10 throughput) — completed reports over the window's wall-clock
        // hours. Zero-length (or inverted) windows report 0 rather than dividing by zero.
        var windowHours = (to - from).TotalHours;
        var reportsPerHour = windowHours <= 0
            ? 0.0
            : raw.CompletedReports / windowHours;

        // ── Governance KPIs (§18.2) ─────────────────────────────────────

        var governanceKpis = new GovernanceKpis(
            UnapprovedPromptUsage: raw.UnapprovedPromptUsageCount,
            PhiViolationsBlocked: raw.PhiViolationsBlocked,
            RulebookRegressionFailures: raw.RulebookRegressionFailures,
            ModelDriftAlerts: raw.ModelDriftAlerts,
            AuditCompleteness: raw.TotalAiRequests == 0
                ? 1.0
                : (double)raw.AiRequestsWithFullTrace / raw.TotalAiRequests);

        var productKpis = new ProductKpis(
            DraftAcceptanceRate: draftAcceptanceRate,
            ImpressionAcceptanceRate: impressionAcceptanceRate,
            TimeSavedPerReport: timeSavedPerReport,
            ValidationPassRate: validationPassRate,
            ContradictionDetectionRate: contradictionDetectionRate,
            EditDistance: editDistance,
            ActiveRadiologists: activeRadiologists,
            RulebookAdoption: rulebookAdoption,
            ProviderCostPerReport: providerCostPerReport,
            TurnaroundTimeImpact: turnaroundTimeImpact,
            AvgQualityScore: avgQualityScore,
            ReportsPerHour: reportsPerHour);

        return new AnalyticsSummary(
            Window: new AnalyticsWindow(from, to),
            Product: productKpis,
            Governance: governanceKpis,
            Ai: aiUsage);
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

/// <summary>PRD §18 — full analytics summary covering product + governance KPIs.</summary>
public sealed record AnalyticsSummary(
    AnalyticsWindow Window,
    ProductKpis Product,
    GovernanceKpis Governance,
    UsageSummary Ai);

public sealed record AnalyticsWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>PRD §18.1 — product KPIs.</summary>
public sealed record ProductKpis(
    /// <summary>% of AI drafts where user didn't reject (reached Acknowledged).</summary>
    double DraftAcceptanceRate,
    /// <summary>% of generated impressions accepted or lightly edited (&lt;20% edit distance).</summary>
    double ImpressionAcceptanceRate,
    /// <summary>Average seconds from report creation to acknowledgement.</summary>
    double TimeSavedPerReport,
    /// <summary>% of reports passing all blocker rules at first validation.</summary>
    double ValidationPassRate,
    /// <summary>Negation/laterality findings per 100 reports.</summary>
    double ContradictionDetectionRate,
    /// <summary>Average character-level edit ratio between AI output and final text.</summary>
    double EditDistance,
    /// <summary>Distinct radiologists who generated/edited reports in period.</summary>
    int ActiveRadiologists,
    /// <summary>% of reports generated with an approved rulebook attached.</summary>
    double RulebookAdoption,
    /// <summary>Total AI cost / total completed reports (USD).</summary>
    decimal ProviderCostPerReport,
    /// <summary>Median seconds to acknowledge (recent period vs baseline).</summary>
    double TurnaroundTimeImpact,
    /// <summary>Average quality score from validation results (null if none).</summary>
    double? AvgQualityScore,
    /// <summary>Completed reports per wall-clock hour over the window (F10 throughput).</summary>
    double ReportsPerHour);

/// <summary>PRD §18.2 — governance KPIs.</summary>
public sealed record GovernanceKpis(
    /// <summary>Count of PolicyViolation audit events for unapproved prompts.</summary>
    int UnapprovedPromptUsage,
    /// <summary>Count of ProviderBlocked audit events for PHI policy.</summary>
    int PhiViolationsBlocked,
    /// <summary>Count of failed validation pack runs.</summary>
    int RulebookRegressionFailures,
    /// <summary>Count of SystemAlert audit events with kind=model_drift.</summary>
    int ModelDriftAlerts,
    /// <summary>% of AiRequest rows with all required trace fields populated.</summary>
    double AuditCompleteness);

/// <summary>
/// Raw counts fetched by the data-access layer (controller / repository)
/// and passed to <see cref="AnalyticsService.ComputeAsync"/> for KPI
/// computation. Keeps the service free of EF Core dependencies.
/// </summary>
public sealed record AnalyticsRawData
{
    // ── Product raw counts ──────────────────────────────────────────
    public int TotalReports { get; init; }
    public int ValidatedReports { get; init; }
    public int CompletedReports { get; init; }
    public int TotalAiGeneratedReports { get; init; }
    public int AiDraftsAcknowledged { get; init; }
    public int ImpressionsLightlyEdited { get; init; }
    public int AcknowledgedReportCount { get; init; }
    public double TotalSecondsToAcknowledge { get; init; }
    public double MedianSecondsToAcknowledge { get; init; }
    public int ContradictionFindings { get; init; }
    public double EditDistanceSum { get; init; }
    public int EditDistanceSampleCount { get; init; }
    public int ActiveRadiologists { get; init; }
    public int ReportsWithRulebook { get; init; }
    public double QualityScoreSum { get; init; }
    public int QualityScoreCount { get; init; }

    // ── Governance raw counts ───────────────────────────────────────
    public int UnapprovedPromptUsageCount { get; init; }
    public int PhiViolationsBlocked { get; init; }
    public int RulebookRegressionFailures { get; init; }
    public int ModelDriftAlerts { get; init; }
    public int TotalAiRequests { get; init; }
    public int AiRequestsWithFullTrace { get; init; }
}
