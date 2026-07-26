namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (REPORT-TEMPLATES) — server-side constants shared by
/// <see cref="RadioPad.Api.Services.PdfLayoutRenderer"/> and
/// <see cref="RadioPad.Api.Services.DocxLayoutRenderer"/>.
///
/// <see cref="FooterText"/> is deliberately not a field on any parsed layout —
/// <see cref="ReportLayoutParser"/> never reads it from client JSON, so there is
/// no path by which a crafted <c>LayoutJson</c> can suppress or alter it. Every
/// layout-driven PDF/DOCX page, and the frontend's <c>LayoutPaper</c> preview
/// (a hand-mirrored copy of this string — see <c>frontend/lib/reportLayouts/accents.ts</c>),
/// carries it unconditionally.
/// </summary>
public static class ReportLayoutBranding
{
    public const string BrandLine = "Powered by RadioPad";
    public const string Website = "radiopadstudio.com";
    public const string FooterText = BrandLine + " — " + Website;

    /// <summary>
    /// Mandatory legal disclaimer — same non-removable contract as
    /// <see cref="FooterText"/>: never read from client JSON, always rendered,
    /// bold, in every layout-driven PDF/DOCX page and the frontend preview.
    /// </summary>
    public const string LegalDisclaimer = "NOT VALID IN COURT OF LAW";

    /// <summary>Pinned hex per <see cref="LayoutAccent"/> — never a client-supplied color string.</summary>
    public static readonly IReadOnlyDictionary<LayoutAccent, string> AccentHex = new Dictionary<LayoutAccent, string>
    {
        [LayoutAccent.Graphite] = "#374151",
        [LayoutAccent.Navy] = "#1e3a8a",
        [LayoutAccent.Teal] = "#0f766e",
        [LayoutAccent.Burgundy] = "#7f1d1d",
        [LayoutAccent.Forest] = "#14532d",
        [LayoutAccent.Slate] = "#475569",
    };

    /// <summary>PDF font family names — QuestPDF resolves these through its Skia-based font
    /// fallback, the same mechanism the legacy renderer already relies on for "Helvetica".</summary>
    public static readonly IReadOnlyDictionary<LayoutFont, string> PdfFontFamily = new Dictionary<LayoutFont, string>
    {
        [LayoutFont.Sans] = "Helvetica",
        [LayoutFont.Serif] = "Times New Roman",
        [LayoutFont.Mono] = "Courier New",
    };

    /// <summary>DOCX <c>RunFonts</c> ascii/hAnsi family names.</summary>
    public static readonly IReadOnlyDictionary<LayoutFont, string> DocxFontFamily = new Dictionary<LayoutFont, string>
    {
        [LayoutFont.Sans] = "Arial",
        [LayoutFont.Serif] = "Times New Roman",
        [LayoutFont.Mono] = "Courier New",
    };

    public static readonly IReadOnlyDictionary<LayoutSectionKey, string> DefaultSectionLabel = new Dictionary<LayoutSectionKey, string>
    {
        [LayoutSectionKey.Indication] = "Indication",
        [LayoutSectionKey.Technique] = "Technique",
        [LayoutSectionKey.Comparison] = "Comparison",
        [LayoutSectionKey.Findings] = "Findings",
        [LayoutSectionKey.Impression] = "Impression",
        [LayoutSectionKey.Recommendations] = "Recommendations",
    };

    public static readonly IReadOnlyDictionary<LayoutStudyField, string> DefaultStudyFieldLabel = new Dictionary<LayoutStudyField, string>
    {
        [LayoutStudyField.PatientReference] = "Patient",
        [LayoutStudyField.AccessionNumber] = "Accession",
        [LayoutStudyField.Modality] = "Modality",
        [LayoutStudyField.BodyPart] = "Body part",
        [LayoutStudyField.Contrast] = "Contrast",
        [LayoutStudyField.Age] = "Age",
        [LayoutStudyField.Gender] = "Gender",
        [LayoutStudyField.Comparison] = "Comparison",
        [LayoutStudyField.PriorReportSummary] = "Prior study",
        [LayoutStudyField.DepartmentTag] = "Department",
        [LayoutStudyField.ReportDate] = "Report date",
        [LayoutStudyField.Status] = "Status",
    };

    /// <summary>Total serialized LayoutJson size cap (UTF-8 bytes), enforced by the parser.</summary>
    public const int MaxLayoutJsonBytes = 256 * 1024;

    /// <summary>Decoded logo byte-size cap, enforced by the parser after base64 decode.</summary>
    public const int MaxLogoBytes = 150 * 1024;

    public const int MaxBlocks = 24;
}
