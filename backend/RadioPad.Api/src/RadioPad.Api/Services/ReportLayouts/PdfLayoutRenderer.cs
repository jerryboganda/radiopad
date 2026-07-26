using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RadioPad.Domain.Entities;
using PdfDocument = QuestPDF.Fluent.Document;

namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (RPT-030) — renders a <see cref="ReportLayoutModel"/> to PDF via
/// QuestPDF. Section BODY TEXT always comes verbatim from <see cref="Report"/>'s six
/// section strings (the same source <c>FhirDiagnosticReportSerializer.BuildNarrative</c>
/// concatenates for text/FHIR export) — this renderer only ever changes presentation:
/// page setup, ordering, labels, and typography. The "Powered by RadioPad" branding
/// line in the footer is emitted unconditionally from <see cref="ReportLayoutBranding"/>
/// and is never influenced by <paramref name="layout"/>.
/// </summary>
public static class PdfLayoutRenderer
{
    public static byte[] Render(Report report, Tenant tenant, ReportLayoutModel layout)
    {
        var pageSize = layout.Page.Size == LayoutPageSize.A4 ? PageSizes.A4 : PageSizes.Letter;
        var fontFamily = ReportLayoutBranding.PdfFontFamily[layout.Page.Font];
        var accentHex = ReportLayoutBranding.AccentHex[layout.Page.Accent];
        var baseFontSize = (float)layout.Page.BaseFontSizePt;

        return PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin((float)layout.Page.MarginPt);
                page.DefaultTextStyle(t => t.FontFamily(fontFamily).FontSize(baseFontSize).FontColor("#111827"));

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    foreach (var block in layout.Blocks)
                    {
                        RenderBlock(col, block, report, tenant, accentHex);
                    }
                });

                page.Footer().Column(col =>
                {
                    col.Spacing(2);
                    if (layout.Footer.ShowStatusLine || !string.IsNullOrWhiteSpace(layout.Footer.CustomText))
                    {
                        col.Item().AlignCenter().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                            if (!string.IsNullOrWhiteSpace(layout.Footer.CustomText))
                            {
                                t.Span(layout.Footer.CustomText);
                                if (layout.Footer.ShowStatusLine) t.Span("   •   ");
                            }
                            if (layout.Footer.ShowStatusLine)
                            {
                                t.Span($"{report.Status} — {report.UpdatedAt:u}");
                            }
                        });
                    }

                    // Mandatory, non-configurable branding — see ReportLayoutBranding.FooterText.
                    col.Item().AlignCenter().Text(ReportLayoutBranding.FooterText)
                        .FontSize(7.5f).FontColor(Colors.Grey.Medium);

                    if (layout.Page.ShowPageNumbers)
                    {
                        col.Item().AlignCenter().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Colors.Grey.Medium));
                            t.Span("Page ");
                            t.CurrentPageNumber();
                            t.Span(" of ");
                            t.TotalPages();
                        });
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void RenderBlock(ColumnDescriptor col, LayoutBlockModel block, Report report, Tenant tenant, string accentHex)
    {
        switch (block)
        {
            case LetterheadBlockModel lh: RenderLetterhead(col, lh, tenant, accentHex); break;
            case StudyInfoBlockModel si: RenderStudyInfo(col, si, report); break;
            case SectionBlockModel sec: RenderSection(col, sec, report, accentHex); break;
            case SignaturesBlockModel sig: RenderSignatures(col, sig, report); break;
            case TextBlockModel txt: RenderText(col, txt); break;
            case DividerBlockModel div: RenderDivider(col, div, accentHex); break;
        }
    }

    private static void RenderLetterhead(ColumnDescriptor col, LetterheadBlockModel b, Tenant tenant, string accentHex)
    {
        var clinicName = string.IsNullOrWhiteSpace(b.ClinicName) ? tenant.DisplayName : b.ClinicName!;

        void TextColumn(ColumnDescriptor inner)
        {
            inner.Item().Text(clinicName).SemiBold().FontSize(16);
            foreach (var line in b.Lines)
            {
                inner.Item().Text(line).FontSize(9).FontColor(Colors.Grey.Darken2);
            }
        }

        col.Item().Element(c =>
        {
            if (b.Logo is null)
            {
                var aligned = b.Align switch
                {
                    LayoutAlign.Center => c.AlignCenter(),
                    LayoutAlign.Right => c.AlignRight(),
                    _ => c.AlignLeft(),
                };
                aligned.Column(TextColumn);
                return;
            }

            if (b.LogoPosition == LayoutLogoPosition.Above)
            {
                c.Column(outer =>
                {
                    outer.Item().AlignCenter().Width((float)b.Logo.WidthPt).Image(b.Logo.Bytes).FitWidth();
                    outer.Item().AlignCenter().Column(TextColumn);
                });
                return;
            }

            c.Row(row =>
            {
                if (b.LogoPosition == LayoutLogoPosition.Left)
                {
                    row.ConstantItem((float)b.Logo.WidthPt).Image(b.Logo.Bytes).FitWidth();
                    row.RelativeItem().PaddingLeft(10).Column(TextColumn);
                }
                else
                {
                    row.RelativeItem().Column(TextColumn);
                    row.ConstantItem((float)b.Logo.WidthPt).PaddingLeft(10).Image(b.Logo.Bytes).FitWidth();
                }
            });
        });

        if (b.ShowAccentRule)
        {
            col.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(accentHex);
        }
    }

    private static void RenderStudyInfo(ColumnDescriptor col, StudyInfoBlockModel b, Report report)
    {
        if (b.Fields.Count == 0) return;

        void Grid(ColumnDescriptor inner)
        {
            foreach (var chunk in b.Fields.Chunk(Math.Max(1, b.Columns)))
            {
                inner.Item().PaddingBottom(4).Row(row =>
                {
                    foreach (var f in chunk)
                    {
                        var label = f.Label ?? ReportLayoutBranding.DefaultStudyFieldLabel[f.Key];
                        var value = ReportLayoutContent.ResolveStudyFieldValue(report, f.Key);
                        row.RelativeItem().Column(cell =>
                        {
                            cell.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor(Colors.Grey.Darken1);
                            cell.Item().Text(value).FontSize(10);
                        });
                    }
                });
            }
        }

        if (b.ShowBox)
        {
            col.Item().Border(0.75f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(Grid);
        }
        else
        {
            col.Item().Column(Grid);
        }
    }

    private static void RenderSection(ColumnDescriptor col, SectionBlockModel b, Report report, string accentHex)
    {
        var body = ReportLayoutContent.ResolveSectionBody(report, b.Section);
        if (string.IsNullOrWhiteSpace(body) && b.HideIfEmpty) return;

        var label = b.Label ?? ReportLayoutBranding.DefaultSectionLabel[b.Section];

        col.Item().Column(inner =>
        {
            switch (b.HeadingStyle)
            {
                case LayoutHeadingStyle.AccentBar:
                    inner.Item().Row(row =>
                    {
                        row.ConstantItem(3).Height(12).Background(accentHex);
                        row.RelativeItem().PaddingLeft(6).Text(label).SemiBold().FontSize(11);
                    });
                    break;
                case LayoutHeadingStyle.Underline:
                    inner.Item().Text(label).SemiBold().FontSize(11);
                    inner.Item().PaddingTop(2).LineHorizontal(1f).LineColor(accentHex);
                    break;
                case LayoutHeadingStyle.Plain:
                    inner.Item().Text(label).SemiBold().FontSize(11);
                    break;
                default: // Uppercase
                    inner.Item().Text(label.ToUpperInvariant()).SemiBold().FontSize(10);
                    break;
            }
            inner.Item().PaddingTop(4).Text(string.IsNullOrWhiteSpace(body) ? "—" : body).FontSize(10.5f).LineHeight(1.35f);
        });
    }

    private static void RenderSignatures(ColumnDescriptor col, SignaturesBlockModel b, Report report)
    {
        if (report.Signatures.Count == 0) return;

        col.Item().PaddingTop(6).Column(inner =>
        {
            inner.Item().Text("SIGNATURES").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
            foreach (var sig in report.Signatures.OrderBy(s => s.SignedAt))
            {
                inner.Item().PaddingTop(10).Column(row =>
                {
                    if (b.ShowSignatureLine)
                    {
                        row.Item().Width(180).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);
                    }
                    row.Item().Text(ReportLayoutContent.RoleLabel(sig.Role)).SemiBold().FontSize(10);
                    if (b.ShowDate)
                    {
                        row.Item().Text($"Signed {sig.SignedAt:f}").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                    }
                    if (b.ShowNote && !string.IsNullOrWhiteSpace(sig.Note))
                    {
                        row.Item().Text(sig.Note!).FontSize(9).Italic();
                    }
                    if (b.ShowHash)
                    {
                        row.Item().Text($"Verification: {ReportLayoutContent.ShortHash(sig.Hash)}").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                    }
                });
            }
        });
    }

    private static void RenderText(ColumnDescriptor col, TextBlockModel b)
    {
        var size = 10.5f + b.FontSizeDelta;
        var el = col.Item();
        var aligned = b.Align switch
        {
            LayoutAlign.Center => el.AlignCenter(),
            LayoutAlign.Right => el.AlignRight(),
            _ => el.AlignLeft(),
        };
        var span = aligned.Text(b.Content).FontSize(size);
        if (b.Italic) span.Italic();
    }

    private static void RenderDivider(ColumnDescriptor col, DividerBlockModel b, string accentHex)
    {
        var spacePt = (float)Math.Max(4, b.SpacePt);
        switch (b.Style)
        {
            case LayoutDividerStyle.Line:
                col.Item().PaddingVertical(spacePt / 2).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                break;
            case LayoutDividerStyle.Accent:
                col.Item().PaddingVertical(spacePt / 2).LineHorizontal(1.5f).LineColor(accentHex);
                break;
            default: // Space
                col.Item().Height(spacePt);
                break;
        }
    }
}
