using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Transcription;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Transcription;
using Xunit;
using SttResult = RadioPad.Application.Abstractions.TranscriptionResult;

namespace RadioPad.Infrastructure.Tests;

public class TranscriptionEngineTests
{
    private class DummyNotifier : ITranscriptionNotifier
    {
        public DictationAudioDto? LastNotifiedDto { get; private set; }

        public Task NotifyTranscriptionCompletedAsync(DictationAudioDto dto, CancellationToken ct = default)
        {
            LastNotifiedDto = dto;
            return Task.CompletedTask;
        }
    }

    /// <summary>Stand-in for the real on-device sherpa-onnx MedASR client so tests never need the
    /// actual ONNX model bundle — asserts the ENGINE calls into it rather than fabricating text.</summary>
    private class FakeLocalSttClient : ILocalSttClient
    {
        private readonly string _text;
        public bool Available { get; set; }
        public bool WasCalled { get; private set; }

        public FakeLocalSttClient(bool available, string text = "FINDINGS: fake local ASR output for test.")
        {
            Available = available;
            _text = text;
        }

        public Task<SttResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null)
        {
            WasCalled = true;
            return Task.FromResult(new SttResult(_text, "medASR-6gram-local", "sherpa-onnx-ctc", 5));
        }
    }

    /// <summary>Stand-in for the real cloud pipeline (<see cref="ITranscriptionService"/>) that the
    /// orchestrator now delegates cloud engine requests to instead of the simplified, one-shot
    /// <see cref="UbagTranscriptionEngine"/>. Captures the call so tests can assert the orchestrator
    /// actually routed through the real, tenant/report-aware service.</summary>
    private class FakeCloudTranscriptionService : ITranscriptionService
    {
        public bool WasCalled { get; private set; }
        public Tenant? LastTenant { get; private set; }
        public Report? LastReport { get; private set; }

        public Task<SttResult> TranscribeAsync(
            Tenant tenant, User user, Report report, Stream audio, string fileName, long sizeBytes,
            string contentType, CancellationToken ct, string? sttMode = null)
        {
            WasCalled = true;
            LastTenant = tenant;
            LastReport = report;
            return Task.FromResult(new SttResult(
                "FINDINGS: fake cloud transcription output for test.", "ubag-cloud", "chatgpt", 42));
        }
    }

    private static RadioPadDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new RadioPadDbContext(options);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_UsesRealLocalSttClient_WhenAvailable()
    {
        var localStt = new FakeLocalSttClient(available: true);
        var engine = new MedAsrTranscriptionEngine(localStt, NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.True(result.Success);
        Assert.Equal("medASR-6gram", result.EngineUsed);
        Assert.True(localStt.WasCalled);
        Assert.Contains("fake local ASR output", result.TranscribedText);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_NotAvailable_ReturnsHonestFailure_NoFakeFindings()
    {
        // Previously this returned the SAME hardcoded "Lungs are clear bilaterally..." canned text
        // no matter what — a patient-safety hazard. It must now fail clearly instead.
        var localStt = new FakeLocalSttClient(available: false);
        var engine = new MedAsrTranscriptionEngine(localStt, NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.False(result.Success);
        Assert.False(localStt.WasCalled);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain("Lungs are clear", result.TranscribedText);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_NoClientRegistered_ReturnsHonestFailure()
    {
        var engine = new MedAsrTranscriptionEngine(null, NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_EmptyStream_ReturnsFailure()
    {
        var engine = new MedAsrTranscriptionEngine(new FakeLocalSttClient(available: true), NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream();
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.False(result.Success);
        Assert.Equal("medASR-6gram", result.EngineUsed);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void MedAsrTranscriptionEngine_IsLocal_And_DisplayName_AreCorrect()
    {
        var engine = new MedAsrTranscriptionEngine(new FakeLocalSttClient(available: true), NullLogger<MedAsrTranscriptionEngine>.Instance);

        Assert.True(engine.IsLocal);
        Assert.True(engine.IsAvailable);
        Assert.Equal("medASR (Local, 6-gram LM)", engine.DisplayName);
    }

    [Fact]
    public async Task UbagTranscriptionEngine_TranscribeAsync_NoClientConfigured_ReturnsHonestFailure_NoFakeFindings()
    {
        // Previously this returned the SAME hardcoded "renal cell carcinoma" impression no matter
        // what — a patient-safety hazard. It must now fail clearly instead of fabricating findings.
        var engine = new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain("renal cell carcinoma", result.TranscribedText);
    }

    [Fact]
    public void UbagTranscriptionEngine_IsLocal_And_DisplayName_AreCorrect()
    {
        var engine = new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance);

        Assert.False(engine.IsLocal);
        Assert.False(engine.IsAvailable);
        Assert.Equal("UBAG (Cloud AI)", engine.DisplayName);
    }

    private static (Report report, Tenant tenant, User user) SeedTenantedReport(RadioPadDbContext db, string radiologyId, string patientName, int age, string gender)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "it", DisplayName = "Integration Test Tenant" };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "it-radiologist@radiopad.local", DisplayName = "Test Radiologist", Role = UserRole.Radiologist };
        var report = new Report
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            RadiologyId = radiologyId,
            PatientName = patientName,
            PatientAge = age,
            PatientGender = gender,
            Status = ReportStatus.Draft
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.Reports.Add(report);
        return (report, tenant, user);
    }

    [Fact]
    public async Task TranscriptionOrchestrator_ProcessDictationAsync_LocalEngine_UpdatesDictationState()
    {
        using var db = CreateInMemoryDb();
        var (report, _, _) = SeedTenantedReport(db, "RAD-TEST-001", "John Doe", 45, "Male");

        var dictation = new DictationAudio
        {
            Id = Guid.NewGuid(),
            ReportId = report.Id,
            StoragePath = "test_audio.wav",
            DurationSeconds = 12.5,
            TranscriptionEngine = "medASR-6gram",
            Status = "Pending"
        };
        db.DictationAudios.Add(dictation);
        await db.SaveChangesAsync();

        var localStt = new FakeLocalSttClient(available: true);
        var engines = new ITranscriptionEngine[]
        {
            new MedAsrTranscriptionEngine(localStt, NullLogger<MedAsrTranscriptionEngine>.Instance),
            new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance)
        };

        var notifier = new DummyNotifier();
        var orchestrator = new TranscriptionOrchestrator(
            engines,
            db,
            NullLogger<TranscriptionOrchestrator>.Instance,
            notifier
        );

        var dto = await orchestrator.ProcessDictationAsync(dictation.Id, "medASR-6gram");

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("medASR-6gram", dto.TranscriptionEngine);
        Assert.True(localStt.WasCalled);
        Assert.False(string.IsNullOrWhiteSpace(dto.TranscribedText));
        Assert.NotNull(dto.TranscribedAt);
        Assert.NotNull(notifier.LastNotifiedDto);
        Assert.Equal(dto.Id, notifier.LastNotifiedDto.Id);
    }

    [Fact]
    public async Task TranscriptionOrchestrator_ProcessDictationAsync_WithCloudOverride_DelegatesToRealTranscriptionService()
    {
        using var db = CreateInMemoryDb();
        var (report, tenant, _) = SeedTenantedReport(db, "RAD-TEST-002", "Jane Smith", 38, "Female");

        var dictation = new DictationAudio
        {
            Id = Guid.NewGuid(),
            ReportId = report.Id,
            StoragePath = "test_audio.wav",
            DurationSeconds = 8.0,
            TranscriptionEngine = "medASR-6gram",
            Status = "Pending"
        };
        db.DictationAudios.Add(dictation);
        await db.SaveChangesAsync();

        var engines = new ITranscriptionEngine[]
        {
            new MedAsrTranscriptionEngine(new FakeLocalSttClient(available: true), NullLogger<MedAsrTranscriptionEngine>.Instance),
            new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance)
        };

        var notifier = new DummyNotifier();
        var cloudTranscription = new FakeCloudTranscriptionService();
        var orchestrator = new TranscriptionOrchestrator(
            engines,
            db,
            NullLogger<TranscriptionOrchestrator>.Instance,
            notifier,
            cloudTranscription
        );

        var dto = await orchestrator.ProcessDictationAsync(dictation.Id, "ubag");

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("ubag-cloud", dto.TranscriptionEngine);
        Assert.Contains("fake cloud transcription output", dto.TranscribedText);
        Assert.True(cloudTranscription.WasCalled, "orchestrator must delegate cloud-engine requests to the real ITranscriptionService, not the simplified UbagTranscriptionEngine");
        Assert.Equal(tenant.Id, cloudTranscription.LastTenant?.Id);
        Assert.Equal(report.Id, cloudTranscription.LastReport?.Id);
    }

    [Fact]
    public async Task TranscriptionOrchestrator_ProcessDictationAsync_UnknownDictation_Throws()
    {
        using var db = CreateInMemoryDb();
        var engines = new ITranscriptionEngine[]
        {
            new MedAsrTranscriptionEngine(new FakeLocalSttClient(available: true), NullLogger<MedAsrTranscriptionEngine>.Instance),
        };
        var orchestrator = new TranscriptionOrchestrator(engines, db, NullLogger<TranscriptionOrchestrator>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => orchestrator.ProcessDictationAsync(Guid.NewGuid()));
    }
}
