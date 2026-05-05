using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using PdfDocument = QuestPDF.Fluent.Document;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace RadioPad.Api.Services;

/// <summary>
/// PRD RPT-011 — PDF + DOCX renderers. Both consume the same plain-text
/// narrative produced by <see cref="FhirDiagnosticReportSerializer.BuildNarrative"/>
/// so all four export formats stay byte-for-byte aligned on content.
/// </summary>
public static class ReportDocumentRenderer
{
    static ReportDocumentRenderer()
    {
        // QuestPDF Community licence (free, no telemetry). Required by the SDK.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] RenderPdf(Report report, Tenant tenant)
    {
        var narrative = FhirDiagnosticReportSerializer.BuildNarrative(report);
        return PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.Letter);
                page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(11));
                page.Header().Column(col =>
                {
                    col.Item().Text(tenant.DisplayName).SemiBold().FontSize(14);
                    col.Item().Text($"Accession: {report.Study.AccessionNumber}    Modality: {report.Study.Modality}    Body part: {report.Study.BodyPart}").FontSize(9);
                });
                page.Content().PaddingVertical(12).Text(narrative);
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("RadioPad — ");
                    t.Span($"{report.Status} — {report.UpdatedAt:u}").FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    public static byte[] RenderDocx(Report report, Tenant tenant)
    {
        var narrative = FhirDiagnosticReportSerializer.BuildNarrative(report);
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new WordDocument(new Body(
                Heading(tenant.DisplayName, level: 1),
                Para($"Accession: {report.Study.AccessionNumber}    Modality: {report.Study.Modality}    Body part: {report.Study.BodyPart}"),
                Para(""),
                Para(narrative)));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Paragraph Heading(string text, int level) => new(
        new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level}" }),
        new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph Para(string text)
    {
        // OpenXML uses CR-only line breaks inside a paragraph; split on \n
        // and emit a Break for each new line so the dictation reads correctly.
        var run = new Run();
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            if (i < lines.Length - 1) run.Append(new Break());
        }
        return new Paragraph(run);
    }
}
