using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (REPORT-TEMPLATES) — pure content-resolution helpers shared by
/// <see cref="PdfLayoutRenderer"/> and <see cref="DocxLayoutRenderer"/>, so the
/// two formats can never drift on what a given field or section resolves to.
/// Section bodies are always the verbatim <see cref="Report"/> strings — the
/// same source <c>FhirDiagnosticReportSerializer.BuildNarrative</c> reads.
/// </summary>
public static class ReportLayoutContent
{
    public static string ResolveStudyFieldValue(Report report, LayoutStudyField field) => field switch
    {
        LayoutStudyField.PatientReference => Dash(report.Study.PatientReference),
        LayoutStudyField.AccessionNumber => Dash(report.Study.AccessionNumber),
        LayoutStudyField.Modality => Dash(report.Study.Modality),
        LayoutStudyField.BodyPart => Dash(report.Study.BodyPart),
        LayoutStudyField.Contrast => Dash(report.Study.Contrast),
        LayoutStudyField.Age => report.Study.Age is int a
            ? report.Study.AgeUnit == StudyAgeUnit.Months ? $"{a} mo" : a.ToString()
            : "—",
        LayoutStudyField.Gender => Dash(report.Study.Gender),
        LayoutStudyField.Comparison => Dash(report.Study.Comparison),
        LayoutStudyField.PriorReportSummary => Dash(report.Study.PriorReportSummary),
        LayoutStudyField.DepartmentTag => Dash(report.DepartmentTag),
        LayoutStudyField.ReportDate => report.UpdatedAt.ToString("d"),
        LayoutStudyField.Status => report.Status.ToString(),
        _ => "—",
    };

    public static string ResolveSectionBody(Report report, LayoutSectionKey key) => key switch
    {
        LayoutSectionKey.Indication => report.Indication,
        LayoutSectionKey.Technique => report.Technique,
        LayoutSectionKey.Comparison => report.Comparison,
        LayoutSectionKey.Findings => report.Findings,
        LayoutSectionKey.Impression => report.Impression,
        LayoutSectionKey.Recommendations => report.Recommendations,
        _ => "",
    };

    public static string RoleLabel(SignatureRole role) => role switch
    {
        SignatureRole.Primary => "Primary radiologist",
        SignatureRole.CoSigner => "Co-signer",
        SignatureRole.Addendum => "Addendum",
        _ => role.ToString(),
    };

    public static string ShortHash(string hash) =>
        string.IsNullOrEmpty(hash) ? "—" : hash.Length <= 12 ? hash : hash[..12] + "…";

    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
