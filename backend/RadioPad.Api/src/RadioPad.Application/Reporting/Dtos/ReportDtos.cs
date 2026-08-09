using System;
using System.Collections.Generic;

namespace RadioPad.Application.Reporting.Dtos;

public record CreateReportRequestDto(
    string RadiologyId,
    string PatientName,
    int PatientAge,
    string PatientGender
);

public record DictationAudioDto(
    Guid Id,
    Guid ReportId,
    string StoragePath,
    double DurationSeconds,
    string TranscriptionEngine,
    string Status,
    string? TranscribedText,
    DateTimeOffset UploadedAt,
    DateTimeOffset? TranscribedAt,
    string? ErrorMessage
);

public record ReportDto(
    Guid Id,
    string RadiologyId,
    string PatientName,
    int PatientAge,
    string PatientGender,
    DateTimeOffset CreatedAt,
    string Status,
    IReadOnlyList<DictationAudioDto> Dictations
);
