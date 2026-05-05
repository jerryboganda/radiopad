using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Services;
using RadioPad.Api.Tests.Integration;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 PERF-004 — verifies the OpenTelemetry histograms exist and
/// record values via <see cref="PerfBudgets.RecordAsync{T}"/>, and that
/// the SLO Alertmanager webhook endpoint is RBAC-gated and writes a
/// <see cref="AuditAction.SystemAlert"/> audit row.
/// </summary>
public class PerfBudgetTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public PerfBudgetTests(RadioPadAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Histograms_Exist_And_Record_Via_Helper()
    {
        var samples = new System.Collections.Concurrent.ConcurrentBag<(string Name, double Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PerfBudgets.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<double>((inst, val, _, _) => samples.Add((inst.Name, val)));
        listener.Start();

        var result = await PerfBudgets.RecordAsync(
            PerfBudgets.ValidateDurationMs,
            async () => { await Task.Delay(1); return 42; });

        Assert.Equal(42, result);

        await PerfBudgets.RecordAsync(
            PerfBudgets.SignDurationMs,
            async () => { await Task.Delay(1); });

        var snapshot = samples.ToArray();
        Assert.Contains(snapshot, s => s.Name == "radiopad.report.validate.duration_ms");
        Assert.Contains(snapshot, s => s.Name == "radiopad.report.sign.duration_ms");

        // All five histograms should be discoverable on the meter.
        var instrumentNames = new HashSet<string>();
        using var inventory = new MeterListener
        {
            InstrumentPublished = (inst, _) =>
            {
                if (inst.Meter.Name == PerfBudgets.MeterName) instrumentNames.Add(inst.Name);
            },
        };
        inventory.Start();
        Assert.Contains("radiopad.report.validate.duration_ms", instrumentNames);
        Assert.Contains("radiopad.report.sign.duration_ms", instrumentNames);
        Assert.Contains("radiopad.ai.draft.duration_ms", instrumentNames);
        Assert.Contains("radiopad.dicom.qido.duration_ms", instrumentNames);
        Assert.Contains("radiopad.api.request.duration_ms", instrumentNames);
    }

    [Fact]
    public async Task SloAlerts_Webhook_Rejects_NonAdmin_With_403()
    {
        var client = _factory.CreateTenantClient(); // role = Radiologist
        var resp = await client.PostAsJsonAsync("/api/admin/observability/slo-alerts",
            new { status = "firing", receiver = "radiopad-soc", alerts = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task SloAlerts_Webhook_Accepts_Admin_And_Writes_Audit()
    {
        var client = _factory.CreateAdminClient(); // role = ItAdmin
        var payload = new
        {
            status = "firing",
            receiver = "radiopad-soc",
            alerts = new object[]
            {
                new
                {
                    labels = new { alertname = "RadioPadValidateP95BudgetBurn", severity = "page" },
                    annotations = new { summary = "burning" },
                    status = "firing",
                },
                new
                {
                    labels = new { alertname = "RadioPadSignP95BudgetBurn", severity = "page" },
                    annotations = new { summary = "burning" },
                    status = "firing",
                },
            },
        };
        var resp = await client.PostAsJsonAsync("/api/admin/observability/slo-alerts", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var rows = await db.AuditEvents
            .Where(a => a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.SystemAlert)
            .OrderByDescending(a => a.CreatedAt)
            .Take(1)
            .ToListAsync();
        Assert.NotEmpty(rows);
        var details = rows[0].DetailsJson;
        Assert.Contains("RadioPadValidateP95BudgetBurn", details);
        Assert.Contains("alertmanager_webhook", details);
        Assert.Contains("payloadSha256", details);
    }
}
