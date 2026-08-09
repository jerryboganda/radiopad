using System;
using System.Collections.Generic;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Domain.Tests;

public class ReportingDomainTests
{
    [Fact]
    public void Report_ShouldInitializeWithDefaultValues()
    {
        var report = new Report();

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal("", report.RadiologyId);
        Assert.Equal("", report.PatientName);
        Assert.Equal(0, report.PatientAge);
        Assert.Equal("", report.PatientGender);
        Assert.NotNull(report.Dictations);
        Assert.Empty(report.Dictations);
    }

    [Fact]
    public void Report_ShouldSetAndRetrieveProperties()
    {
        var report = new Report
        {
            RadiologyId = "RAD-12345",
            PatientName = "John Doe",
            PatientAge = 45,
            PatientGender = "Male"
        };

        Assert.Equal("RAD-12345", report.RadiologyId);
        Assert.Equal("John Doe", report.PatientName);
        Assert.Equal(45, report.PatientAge);
        Assert.Equal("Male", report.PatientGender);
    }

    [Fact]
    public void DictationAudio_ShouldInitializeWithDefaultValues()
    {
        var dictation = new DictationAudio();

        Assert.NotEqual(Guid.Empty, dictation.Id);
        Assert.Equal(Guid.Empty, dictation.ReportId);
        Assert.Null(dictation.Report);
        Assert.Equal("", dictation.StoragePath);
        Assert.Equal(0.0, dictation.DurationSeconds);
        Assert.Equal("medASR-6gram", dictation.TranscriptionEngine);
        Assert.Equal("Pending", dictation.Status);
        Assert.Null(dictation.TranscribedText);
        Assert.Null(dictation.TranscribedAt);
        Assert.Null(dictation.ErrorMessage);
    }

    [Fact]
    public void DictationAudio_ShouldAssociateWithReport()
    {
        var report = new Report
        {
            RadiologyId = "RAD-999",
            PatientName = "Jane Smith",
            PatientAge = 30,
            PatientGender = "Female"
        };

        var dictation = new DictationAudio
        {
            ReportId = report.Id,
            Report = report,
            StoragePath = "/audio/test.wav",
            DurationSeconds = 42.5,
            TranscriptionEngine = "medASR-6gram",
            Status = "Completed",
            TranscribedText = "Normal chest xray"
        };

        report.Dictations.Add(dictation);

        Assert.Single(report.Dictations);
        Assert.Contains(dictation, report.Dictations);
        Assert.Equal(report.Id, dictation.ReportId);
        Assert.Equal(report, dictation.Report);
    }

    [Fact]
    public void Dtos_ShouldCreateAndInstantiateCorrectly()
    {
        var createDto = new CreateReportRequestDto("RAD-101", "Bob Smith", 60, "Male");
        Assert.Equal("RAD-101", createDto.RadiologyId);
        Assert.Equal("Bob Smith", createDto.PatientName);
        Assert.Equal(60, createDto.PatientAge);
        Assert.Equal("Male", createDto.PatientGender);

        var audioDto = new DictationAudioDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "/path/audio.wav",
            12.5,
            "medASR-6gram",
            "Pending",
            null,
            DateTimeOffset.UtcNow,
            null,
            null
        );
        Assert.Equal("/path/audio.wav", audioDto.StoragePath);

        var reportDto = new ReportDto(
            Guid.NewGuid(),
            "RAD-101",
            "Bob Smith",
            60,
            "Male",
            DateTimeOffset.UtcNow,
            "Draft",
            new List<DictationAudioDto> { audioDto }
        );
        Assert.Equal("RAD-101", reportDto.RadiologyId);
        Assert.Single(reportDto.Dictations);
    }
}
