using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Integration;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-31 INT-006 — verifies MLLP framing roundtrip, ORU^R01 parsing into
/// a Draft <see cref="Report"/>, ACK shape, and accession-number dedupe.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class Hl7MllpListenerTests : IClassFixture<RadioPadAppFactory>
{
    private const string Facility = "GENERAL_HOSPITAL_RAD";
    private readonly RadioPadAppFactory _factory;
    public Hl7MllpListenerTests(RadioPadAppFactory f) => _factory = f;

    private async Task ConfigureSendingFacilityAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
        s.Hl7SendingFacility = Facility;
        await db.SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task MllpFramer_Wrap_And_Read_Roundtrip()
    {
        var payload = "MSH|^~\\&|RADTEST|FAC|||20260503120000||ORU^R01|MID1|P|2.5\rPID|1||PT-001\r";
        var bytes = MllpFramer.Wrap(payload);
        Assert.Equal(0x0B, bytes[0]);
        Assert.Equal(0x1C, bytes[^2]);
        Assert.Equal(0x0D, bytes[^1]);
        await using var ms = new MemoryStream(bytes);
        var read = await MllpFramer.ReadFrameAsync(ms, default);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task Oru_Creates_Draft_Report_And_Returns_AA_Ack()
    {
        await ConfigureSendingFacilityAsync();
        try
        {
            var hl7 = "MSH|^~\\&|RIS|" + Facility + "|RADIOPAD|RADIOPAD|20260503120000||ORU^R01|MID-ORU-1|P|2.5\r" +
                      "PID|1||PT-MLLP-1^^^FAC^MR\r" +
                      "OBR|1|PLACER|ACC-MLLP-1|71250^CT CHEST^L|||20260503115500||||||||||||||||CT||||||||||Acute chest pain\r";
            var handler = _factory.Services.GetRequiredService<Hl7MessageHandler>();
            var result = await handler.HandleAsync(hl7, default);
            Assert.True(result.Accepted);
            Assert.False(result.Deduplicated);
            Assert.NotNull(result.ReportId);
            Assert.Contains("MSA|AA|MID-ORU-1", result.Ack);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = await db.Reports.FindAsync(result.ReportId!.Value);
            Assert.NotNull(report);
            Assert.Equal("ACC-MLLP-1", report!.Study.AccessionNumber);
            Assert.Equal("CT", report.Study.Modality);
            Assert.Equal(_factory.SeedTenant.Id, report.TenantId);
            Assert.Equal(ReportStatus.Draft, report.Status);
            Assert.Equal("PT-MLLP-1", report.Study.PatientReference);

            var audited = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id
                && a.ReportId == report.Id
                && a.Action == AuditAction.OrderIngested);
            Assert.True(audited);
        }
        finally { await ResetAsync(); }
    }

    [Fact]
    public async Task Duplicate_Accession_Returns_AA_With_Deduplicated_True()
    {
        await ConfigureSendingFacilityAsync();
        try
        {
            var hl7 = "MSH|^~\\&|RIS|" + Facility + "|RADIOPAD|RADIOPAD|20260503120000||ORU^R01|MID-DUP-1|P|2.5\r" +
                      "OBR|1||ACC-MLLP-DUP|71250^CT CHEST^L|||||||||||||||||||CT||||||||||\r";
            var handler = _factory.Services.GetRequiredService<Hl7MessageHandler>();
            var first = await handler.HandleAsync(hl7, default);
            Assert.True(first.Accepted);
            Assert.False(first.Deduplicated);

            var second = await handler.HandleAsync(hl7, default);
            Assert.True(second.Accepted);
            Assert.True(second.Deduplicated);
            Assert.Equal(first.ReportId, second.ReportId);
            Assert.Contains("MSA|AA|", second.Ack);
        }
        finally { await ResetAsync(); }
    }

    [Fact]
    public async Task Unknown_Sending_Facility_Is_Rejected()
    {
        var hl7 = "MSH|^~\\&|RIS|UNKNOWN_FAC|RADIOPAD|RADIOPAD|20260503120000||ORU^R01|MID-X|P|2.5\r" +
                  "OBR|1||ACC-X|71250^CT^L|||||||||||||||||||CT||||||||||\r";
        var handler = _factory.Services.GetRequiredService<Hl7MessageHandler>();
        var result = await handler.HandleAsync(hl7, default);
        Assert.False(result.Accepted);
        Assert.Contains("MSA|AR|MID-X|Unknown sending facility", result.Ack);
    }

    [Fact]
    public void Listener_Disabled_When_Port_Not_Set()
    {
        var prev = Environment.GetEnvironmentVariable(Hl7MllpListener.PortEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(Hl7MllpListener.PortEnvVar, null);
            var handler = _factory.Services.GetRequiredService<Hl7MessageHandler>();
            var listener = new Hl7MllpListener(handler, NullLogger<Hl7MllpListener>.Instance);
            // ExecuteAsync should return immediately when no port is configured.
            var task = listener.StartAsync(default);
            Assert.True(task.IsCompleted);
        }
        finally { Environment.SetEnvironmentVariable(Hl7MllpListener.PortEnvVar, prev); }
    }
}
