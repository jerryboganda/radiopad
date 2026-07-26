namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (REPORT-TEMPLATES) — the strongly-typed, already-validated form of a
/// <see cref="RadioPad.Domain.Entities.ReportLayout.LayoutJson"/> row. Produced only
/// by <see cref="ReportLayoutParser.TryParse"/>; never constructed by hand outside
/// tests, so every instance in the renderer pipeline is known-valid.
///
/// Mirrors the frontend type <c>frontend/lib/reportLayouts/schema.ts</c> —
/// <c>ReportLayoutJson</c> — field for field. Keep the two in sync.
/// </summary>
public sealed class ReportLayoutModel
{
    public int SchemaVersion { get; init; } = 1;
    public required PageSetupModel Page { get; init; }
    public required IReadOnlyList<LayoutBlockModel> Blocks { get; init; }
    public required LayoutFooterModel Footer { get; init; }
}

public enum LayoutPageSize { Letter, A4 }

public enum LayoutFont { Sans, Serif, Mono }

public enum LayoutAccent { Graphite, Navy, Teal, Burgundy, Forest, Slate }

public enum LayoutAlign { Left, Center, Right }

public enum LayoutHeadingStyle { Uppercase, AccentBar, Underline, Plain }

public enum LayoutSectionKey { Indication, Technique, Comparison, Findings, Impression, Recommendations }

public enum LayoutStudyField
{
    PatientReference, AccessionNumber, Modality, BodyPart, Contrast, Age, Gender,
    Comparison, PriorReportSummary, DepartmentTag, ReportDate, Status,
}

public enum LayoutDividerStyle { Line, Accent, Space }

public enum LayoutLogoPosition { Left, Right, Above }

public enum LayoutLogoFormat { Png, Jpeg }

public sealed class PageSetupModel
{
    public LayoutPageSize Size { get; init; } = LayoutPageSize.Letter;
    public double MarginPt { get; init; } = 36;
    public LayoutFont Font { get; init; } = LayoutFont.Sans;
    public double BaseFontSizePt { get; init; } = 11;
    public LayoutAccent Accent { get; init; } = LayoutAccent.Graphite;
    public bool ShowPageNumbers { get; init; } = true;
}

public sealed class LayoutFooterModel
{
    public bool ShowStatusLine { get; init; } = true;
    /// <summary>≤200 chars. Never carries the "Powered by RadioPad" branding — see <see cref="ReportLayoutBranding"/>.</summary>
    public string? CustomText { get; init; }
}

/// <summary>
/// A decoded, size-checked logo image ready to embed in either renderer.
/// <see cref="Format"/> selects the MIME/content type at embed time; the raw
/// data-URL text is never carried past the parser.
/// </summary>
public sealed class LayoutLogoModel
{
    public required byte[] Bytes { get; init; }
    public required LayoutLogoFormat Format { get; init; }
    public double WidthPt { get; init; } = 120;
}

/// <summary>Base type for every block in <see cref="ReportLayoutModel.Blocks"/>. One sealed record per block type below.</summary>
public abstract record LayoutBlockModel(string Id);

public sealed record LetterheadBlockModel(
    string Id,
    string? ClinicName,
    IReadOnlyList<string> Lines,
    LayoutLogoModel? Logo,
    LayoutLogoPosition LogoPosition,
    LayoutAlign Align,
    bool ShowAccentRule) : LayoutBlockModel(Id);

public sealed record StudyFieldEntry(LayoutStudyField Key, string? Label);

public sealed record StudyInfoBlockModel(
    string Id,
    int Columns,
    bool ShowBox,
    IReadOnlyList<StudyFieldEntry> Fields) : LayoutBlockModel(Id);

public sealed record SectionBlockModel(
    string Id,
    LayoutSectionKey Section,
    string? Label,
    LayoutHeadingStyle HeadingStyle,
    bool HideIfEmpty) : LayoutBlockModel(Id);

public sealed record SignaturesBlockModel(
    string Id,
    bool ShowDate,
    bool ShowNote,
    bool ShowHash,
    bool ShowSignatureLine) : LayoutBlockModel(Id);

public sealed record TextBlockModel(
    string Id,
    string Content,
    LayoutAlign Align,
    bool Italic,
    int FontSizeDelta) : LayoutBlockModel(Id);

public sealed record DividerBlockModel(
    string Id,
    LayoutDividerStyle Style,
    double SpacePt) : LayoutBlockModel(Id);
