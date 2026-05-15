using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Services;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class MeasurementExtractionTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    private readonly MeasurementExtractionService _svc = new();

    public MeasurementExtractionTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public void SingleMeasurement_ExtractsValueUnitAndFinding()
    {
        var results = _svc.Extract("3.2 cm nodule in the right lung", "findings");

        Assert.Single(results);
        var m = results[0];
        Assert.Equal(3.2, m.Value);
        Assert.Equal("cm", m.Unit);
        Assert.Null(m.SecondValue);
        Assert.Null(m.ThirdValue);
        Assert.Equal("nodule", m.Finding);
        Assert.Equal("findings", m.Section);
    }

    [Fact]
    public void BiaxialMeasurement_ExtractsValueAndSecondValue()
    {
        var results = _svc.Extract("5 x 3 mm mass in the liver", "findings");

        Assert.Single(results);
        var m = results[0];
        Assert.Equal(5.0, m.Value);
        Assert.Equal("mm", m.Unit);
        Assert.Equal("3", m.SecondValue);
        Assert.Null(m.ThirdValue);
        Assert.Equal("mass", m.Finding);
        Assert.Equal("liver", m.AnatomicalLocation);
    }

    [Fact]
    public void TriaxialMeasurement_ExtractsAllThreeValues()
    {
        var results = _svc.Extract("12 × 8 × 6 mm lymph node", "findings");

        Assert.Single(results);
        var m = results[0];
        Assert.Equal(12.0, m.Value);
        Assert.Equal("mm", m.Unit);
        Assert.Equal("8", m.SecondValue);
        Assert.Equal("6", m.ThirdValue);
        Assert.Equal("lymph node", m.Finding);
    }

    [Fact]
    public void AnatomicalLocationAndLaterality_AreCaptured()
    {
        var results = _svc.Extract("right upper lobe 3 cm nodule", "findings");

        Assert.Single(results);
        var m = results[0];
        Assert.Equal("right", m.Laterality);
        Assert.Equal("lobe", m.AnatomicalLocation);
        Assert.Equal("nodule", m.Finding);
    }

    [Fact]
    public void MultipleMeasurements_AllExtracted()
    {
        var text = "There is a 2 cm nodule in the right lung and a 5 x 3 mm cyst in the left kidney.";
        var results = _svc.Extract(text, "findings");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void NoMeasurements_ReturnsEmptyList()
    {
        var results = _svc.Extract("Lungs are clear. No acute findings.", "findings");

        Assert.Empty(results);
    }

    [Fact]
    public void NegatedMeasurement_StillExtracted()
    {
        var results = _svc.Extract("No nodule measuring 5 mm identified.", "findings");

        Assert.Single(results);
        Assert.Equal(5.0, results[0].Value);
        Assert.Equal("mm", results[0].Unit);
        Assert.Equal("nodule", results[0].Finding);
    }

    [Fact]
    public async Task Endpoint_ReturnsMeasurementsForReport()
    {
        using var client = _factory.CreateTenantClient();

        // Create a report with a finding that contains a measurement
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = "ACC-MEAS-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        // Patch findings to include a measurement
        var patch = await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Right upper lobe 3.2 cm nodule. Left kidney 5 x 3 mm cyst.",
            impression = "1. Nodule. 2. Cyst.",
        });
        Assert.True(patch.IsSuccessStatusCode);

        // Hit the measurements endpoint
        var resp = await client.GetAsync($"/api/reports/{id}/measurements");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var measurements = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(measurements.GetArrayLength() >= 2,
            $"Expected at least 2 measurements, got {measurements.GetArrayLength()}");
    }

    [Fact]
    public async Task Endpoint_ReturnsNotFoundForMissingReport()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/reports/{Guid.NewGuid()}/measurements");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
