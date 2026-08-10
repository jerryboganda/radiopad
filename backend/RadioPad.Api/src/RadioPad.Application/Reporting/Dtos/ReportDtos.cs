using System;
using System.Collections.Generic;

namespace RadioPad.Application.Reporting.Dtos;

public record CreateReportRequestDto(
    string RadiologyId,
    string PatientName,
    int PatientAge,
    string PatientGender,
    /// <summary>Imaging modality (e.g. "CT", "MRI", "X-Ray") — written into <c>Report.Study.Modality</c>
    /// so template/rulebook auto-resolution (which already keys off <c>Study.Modality</c>/<c>BodyPart</c>
    /// for every desktop-created report) also works for reports created from the mobile app.</summary>
    string? Modality = null,
    /// <summary>Body region/part scanned (e.g. "Chest", "Abdomen") — written into <c>Report.Study.BodyPart</c>.</summary>
    string? BodyPart = null
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
    IReadOnlyList<DictationAudioDto> Dictations,
    string Modality = "",
    string BodyPart = ""
);

/// <summary>List-engines response item for the mobile/desktop transcription-engine dropdown —
/// dynamically enumerated from the DI-registered <c>ITranscriptionEngine</c>s so a future 3rd
/// engine registration appears automatically, with no hardcoded strings on either client.</summary>
public record TranscriptionEngineDto(
    string EngineId,
    string DisplayName,
    bool IsLocal,
    bool IsAvailable,
    bool IsDefault
);
