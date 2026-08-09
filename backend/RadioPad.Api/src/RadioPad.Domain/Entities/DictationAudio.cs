using System;

namespace RadioPad.Domain.Entities;

public class DictationAudio : Entity
{
    public Guid ReportId { get; set; }
    public Report? Report { get; set; }
    public string StoragePath { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string TranscriptionEngine { get; set; } = "medASR-6gram";
    public string Status { get; set; } = "Pending";
    public string? TranscribedText { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TranscribedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
