namespace RadioPad.Domain.ValueObjects;

/// <summary>
/// A single measurement extracted from free-text radiology findings.
/// Supports single, biaxial ("5 x 3 mm"), and triaxial ("12 × 8 × 6 mm") forms.
/// </summary>
public sealed record ExtractedMeasurement(
    double Value,
    string Unit,
    string? SecondValue,
    string? ThirdValue,
    string? AnatomicalLocation,
    string? Finding,
    string? Laterality,
    string Section,
    int StartIndex,
    int EndIndex);
