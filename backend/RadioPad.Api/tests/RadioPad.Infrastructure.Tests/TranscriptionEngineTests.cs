using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Transcription;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Transcription;
using Xunit;

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

    private static RadioPadDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new RadioPadDbContext(options);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_ReturnsValidText()
    {
        var engine = new MedAsrTranscriptionEngine(NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.True(result.Success);
        Assert.Equal("medASR-6gram", result.EngineUsed);
        Assert.Contains("FINDINGS:", result.TranscribedText);
    }

    [Fact]
    public async Task MedAsrTranscriptionEngine_TranscribeAsync_EmptyStream_ReturnsFailure()
    {
        var engine = new MedAsrTranscriptionEngine(NullLogger<MedAsrTranscriptionEngine>.Instance);
        using var stream = new MemoryStream();
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.False(result.Success);
        Assert.Equal("medASR-6gram", result.EngineUsed);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task UbagTranscriptionEngine_TranscribeAsync_ReturnsCloudDictation()
    {
        var engine = new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var context = new TranscriptionContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await engine.TranscribeAsync(stream, context);

        Assert.True(result.Success);
        Assert.Equal("ubag", result.EngineUsed);
        Assert.Contains("UBAG Cloud AI", result.TranscribedText);
    }

    [Fact]
    public async Task TranscriptionOrchestrator_ProcessDictationAsync_UpdatesDictationState()
    {
        using var db = CreateInMemoryDb();
        var report = new Report
        {
            Id = Guid.NewGuid(),
            RadiologyId = "RAD-TEST-001",
            PatientName = "John Doe",
            PatientAge = 45,
            PatientGender = "Male",
            Status = ReportStatus.Draft
        };
        db.Reports.Add(report);

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

        var engines = new ITranscriptionEngine[]
        {
            new MedAsrTranscriptionEngine(NullLogger<MedAsrTranscriptionEngine>.Instance),
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
        Assert.False(string.IsNullOrWhiteSpace(dto.TranscribedText));
        Assert.NotNull(dto.TranscribedAt);
        Assert.NotNull(notifier.LastNotifiedDto);
        Assert.Equal(dto.Id, notifier.LastNotifiedDto.Id);
    }

    [Fact]
    public async Task TranscriptionOrchestrator_ProcessDictationAsync_WithUbagOverride_UsesUbagEngine()
    {
        using var db = CreateInMemoryDb();
        var report = new Report
        {
            Id = Guid.NewGuid(),
            RadiologyId = "RAD-TEST-002",
            PatientName = "Jane Smith",
            PatientAge = 38,
            PatientGender = "Female",
            Status = ReportStatus.Draft
        };
        db.Reports.Add(report);

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
            new MedAsrTranscriptionEngine(NullLogger<MedAsrTranscriptionEngine>.Instance),
            new UbagTranscriptionEngine(null, NullLogger<UbagTranscriptionEngine>.Instance)
        };

        var notifier = new DummyNotifier();
        var orchestrator = new TranscriptionOrchestrator(
            engines,
            db,
            NullLogger<TranscriptionOrchestrator>.Instance,
            notifier
        );

        var dto = await orchestrator.ProcessDictationAsync(dictation.Id, "ubag");

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("ubag", dto.TranscriptionEngine);
        Assert.Contains("UBAG", dto.TranscribedText);
    }
}
