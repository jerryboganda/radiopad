using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RadioPad.Domain.Entities;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (RPT-030) — renders a <see cref="ReportLayoutModel"/> to DOCX via
/// the OpenXml SDK. Mirrors <see cref="PdfLayoutRenderer"/> block-for-block; section
/// body text is always the verbatim <see cref="Report"/> strings via
/// <see cref="ReportLayoutContent"/>, and the "Powered by RadioPad" branding line in
/// the footer is emitted unconditionally, never from <paramref name="layout"/>.
///
/// Simplification vs. the PDF renderer: a letterhead logo is always placed above the
/// clinic name/lines in DOCX regardless of <c>LogoPosition</c> (left/right side-by-side
/// placement needs a floating anchor or a borderless table; not worth the added schema
/// risk for a cosmetic difference). The PDF renderer honors left/right/above exactly.
/// </summary>
public static class DocxLayoutRenderer
{
    public static byte[] Render(Report report, Tenant tenant, ReportLayoutModel layout)
    {
        var fontFamily = ReportLayoutBranding.DocxFontFamily[layout.Page.Font];
        var accentHex = ReportLayoutBranding.AccentHex[layout.Page.Accent].TrimStart('#');
        var baseSize = layout.Page.BaseFontSizePt;

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();

            foreach (var block in layout.Blocks)
            {
                AppendBlock(body, main, block, report, tenant, fontFamily, accentHex, baseSize);
            }

            var footerPart = main.AddNewPart<FooterPart>();
            footerPart.Footer = BuildFooter(report, layout, fontFamily);
            footerPart.Footer.Save();
            var footerRelId = main.GetIdOfPart(footerPart);

            var (pageWidth, pageHeight) = layout.Page.Size == LayoutPageSize.A4 ? (11906, 16838) : (12240, 15840);
            var marginTwips = (int)Math.Round(layout.Page.MarginPt * 20);

            var sectPr = new SectionProperties(
                new FooterReference { Type = HeaderFooterValues.Default, Id = footerRelId },
                new PageSize { Width = (UInt32Value)(uint)pageWidth, Height = (UInt32Value)(uint)pageHeight },
                new PageMargin
                {
                    Top = marginTwips,
                    Right = (UInt32Value)(uint)marginTwips,
                    Bottom = marginTwips,
                    Left = (UInt32Value)(uint)marginTwips,
                });
            body.Append(sectPr);

            main.Document = new WordDocument(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    // ---- footer --------------------------------------------------------------

    /// <summary>
    /// Single line — lead text (custom/status), then the mandatory branding, then
    /// the page number, joined with a bullet separator. Never stacked paragraphs.
    /// </summary>
    private static Footer BuildFooter(Report report, ReportLayoutModel layout, string fontFamily)
    {
        var footer = new Footer();
        var paragraph = new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));

        RunProperties RPr(bool bold = false)
        {
            var rPr = new RunProperties();
            rPr.Append(new RunFonts { Ascii = fontFamily, HighAnsi = fontFamily });
            if (bold) rPr.Append(new Bold());
            rPr.Append(new Color { Val = "6b7280" });
            rPr.Append(new FontSize { Val = "15" }); // 7.5pt
            return rPr;
        }
        Run TextRun(string text, bool bold = false) => new(RPr(bold), new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var leadParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(layout.Footer.CustomText)) leadParts.Add(layout.Footer.CustomText!);
        if (layout.Footer.ShowStatusLine) leadParts.Add($"{report.Status} — {report.UpdatedAt:u}");
        if (leadParts.Count > 0)
        {
            paragraph.Append(TextRun(string.Join("   •   ", leadParts) + "   •   "));
        }

        // Mandatory, non-configurable — see ReportLayoutBranding. Bold: legal
        // disclaimer, then the branding line, separated by a bullet.
        paragraph.Append(TextRun(ReportLayoutBranding.LegalDisclaimer, bold: true));
        paragraph.Append(TextRun("   •   "));
        paragraph.Append(TextRun(ReportLayoutBranding.FooterText, bold: true));

        if (layout.Page.ShowPageNumbers)
        {
            paragraph.Append(TextRun("   •   Page "));
            paragraph.Append(new SimpleField(new Run(RPr(), new Text("1"))) { Instruction = "PAGE" });
            paragraph.Append(TextRun(" of "));
            paragraph.Append(new SimpleField(new Run(RPr(), new Text("1"))) { Instruction = "NUMPAGES" });
        }

        footer.Append(paragraph);
        return footer;
    }

    // ---- block dispatch --------------------------------------------------------------

    private static void AppendBlock(Body body, MainDocumentPart main, LayoutBlockModel block, Report report, Tenant tenant, string fontFamily, string accentHex, double baseSize)
    {
        switch (block)
        {
            case LetterheadBlockModel lh: AppendLetterhead(body, main, lh, tenant, fontFamily, accentHex); break;
            case StudyInfoBlockModel si: AppendStudyInfo(body, si, report, fontFamily); break;
            case SectionBlockModel sec: AppendSection(body, sec, report, fontFamily, accentHex, baseSize); break;
            case SignaturesBlockModel sig: AppendSignatures(body, sig, report, fontFamily); break;
            case TextBlockModel txt: AppendText(body, txt, fontFamily, baseSize); break;
            case DividerBlockModel div: AppendDivider(body, div, fontFamily, accentHex); break;
        }
    }

    private static void AppendLetterhead(Body body, MainDocumentPart main, LetterheadBlockModel b, Tenant tenant, string fontFamily, string accentHex)
    {
        var clinicName = string.IsNullOrWhiteSpace(b.ClinicName) ? tenant.DisplayName : b.ClinicName!;
        var align = b.Align switch
        {
            LayoutAlign.Center => JustificationValues.Center,
            LayoutAlign.Right => JustificationValues.Right,
            _ => JustificationValues.Left,
        };

        if (b.Logo is not null)
        {
            var relId = EmbedImage(main, b.Logo);
            var drawingParagraph = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(ImageDrawing(relId, b.Logo)));
            body.Append(drawingParagraph);
        }

        body.Append(Para(clinicName, fontFamily, 16, bold: true, align: align));
        foreach (var line in b.Lines)
        {
            body.Append(Para(line, fontFamily, 9, colorHex: "595959", align: align));
        }

        if (b.ShowAccentRule)
        {
            body.Append(Para("", fontFamily, 1, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 12, Color = accentHex }));
        }
    }

    private static string EmbedImage(MainDocumentPart main, LayoutLogoModel logo)
    {
        var part = main.AddImagePart(logo.Format == LayoutLogoFormat.Png ? ImagePartType.Png : ImagePartType.Jpeg);
        using var stream = new MemoryStream(logo.Bytes);
        part.FeedData(stream);
        return main.GetIdOfPart(part);
    }

    private static Drawing ImageDrawing(string relId, LayoutLogoModel logo)
    {
        var aspect = EstimateAspectRatio(logo);
        var widthEmu = (long)(logo.WidthPt * 12700);
        var heightEmu = (long)Math.Max(1, logo.WidthPt * aspect * 12700);

        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = 1U, Name = "Letterhead logo" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Letterhead logo" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
    }

    /// <summary>
    /// PNG stores its pixel dimensions at a fixed IHDR offset, so we read them
    /// directly; a JPEG's SOF marker is variable-offset and not worth a parser for
    /// a cosmetic sizing detail, so JPEGs fall back to a neutral wide-logo ratio.
    /// The PDF renderer (QuestPDF <c>FitWidth</c>) is unaffected by this fallback.
    /// </summary>
    private static double EstimateAspectRatio(LayoutLogoModel logo)
    {
        if (logo.Format == LayoutLogoFormat.Png && logo.Bytes.Length >= 24)
        {
            var width = ReadUInt32BigEndian(logo.Bytes, 16);
            var height = ReadUInt32BigEndian(logo.Bytes, 20);
            if (width > 0 && height > 0) return (double)height / width;
        }
        return 0.4;
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void AppendStudyInfo(Body body, StudyInfoBlockModel b, Report report, string fontFamily)
    {
        if (b.Fields.Count == 0) return;
        var cols = Math.Max(1, b.Columns);
        var borderVal = b.ShowBox ? BorderValues.Single : BorderValues.None;

        var table = new Table();
        table.Append(new TableProperties(
            new TableBorders(
                new TopBorder { Val = borderVal, Size = 4, Color = "D9D9D9" },
                new BottomBorder { Val = borderVal, Size = 4, Color = "D9D9D9" },
                new LeftBorder { Val = borderVal, Size = 4, Color = "D9D9D9" },
                new RightBorder { Val = borderVal, Size = 4, Color = "D9D9D9" },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));

        var grid = new TableGrid();
        for (var i = 0; i < cols; i++) grid.Append(new GridColumn());
        table.Append(grid);

        foreach (var chunk in b.Fields.Chunk(cols))
        {
            var tr = new TableRow();
            foreach (var f in chunk)
            {
                var label = f.Label ?? ReportLayoutBranding.DefaultStudyFieldLabel[f.Key];
                var value = ReportLayoutContent.ResolveStudyFieldValue(report, f.Key);
                var tc = new TableCell();
                tc.Append(new TableCellProperties());
                tc.Append(Para(label.ToUpperInvariant(), fontFamily, 8, colorHex: "595959"));
                tc.Append(Para(value, fontFamily, 10));
                tr.Append(tc);
            }
            for (var pad = chunk.Length; pad < cols; pad++)
            {
                tr.Append(new TableCell(new TableCellProperties(), new Paragraph()));
            }
            table.Append(tr);
        }

        body.Append(table);
        body.Append(new Paragraph());
    }

    private static void AppendSection(Body body, SectionBlockModel b, Report report, string fontFamily, string accentHex, double baseSize)
    {
        var text = ReportLayoutContent.ResolveSectionBody(report, b.Section);
        if (string.IsNullOrWhiteSpace(text) && b.HideIfEmpty) return;

        var label = b.Label ?? ReportLayoutBranding.DefaultSectionLabel[b.Section];

        switch (b.HeadingStyle)
        {
            case LayoutHeadingStyle.AccentBar:
                body.Append(Para(label, fontFamily, 11, bold: true));
                body.Append(Para("", fontFamily, 1, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 18, Color = accentHex }));
                break;
            case LayoutHeadingStyle.Underline:
                body.Append(Para(label, fontFamily, 11, bold: true, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 8, Color = accentHex }));
                break;
            case LayoutHeadingStyle.Plain:
                body.Append(Para(label, fontFamily, 11, bold: true));
                break;
            default: // Uppercase
                body.Append(Para(label.ToUpperInvariant(), fontFamily, 10, bold: true));
                break;
        }

        body.Append(Para(string.IsNullOrWhiteSpace(text) ? "—" : text, fontFamily, baseSize, multiline: true));
    }

    private static void AppendSignatures(Body body, SignaturesBlockModel b, Report report, string fontFamily)
    {
        if (report.Signatures.Count == 0) return;

        body.Append(Para("SIGNATURES", fontFamily, 9, bold: true, colorHex: "595959"));
        foreach (var sig in report.Signatures.OrderBy(s => s.SignedAt))
        {
            body.Append(new Paragraph()); // spacer
            if (b.ShowSignatureLine)
            {
                body.Append(Para("", fontFamily, 1, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "595959" }));
            }
            body.Append(Para(ReportLayoutContent.RoleLabel(sig.Role), fontFamily, 10, bold: true));
            if (b.ShowDate)
            {
                body.Append(Para($"Signed {sig.SignedAt:f}", fontFamily, 8.5, colorHex: "595959"));
            }
            if (b.ShowNote && !string.IsNullOrWhiteSpace(sig.Note))
            {
                body.Append(Para(sig.Note!, fontFamily, 9, italic: true));
            }
            if (b.ShowHash)
            {
                body.Append(Para($"Verification: {ReportLayoutContent.ShortHash(sig.Hash)}", fontFamily, 7.5, colorHex: "808080"));
            }
        }
    }

    private static void AppendText(Body body, TextBlockModel b, string fontFamily, double baseSize)
    {
        var align = b.Align switch
        {
            LayoutAlign.Center => JustificationValues.Center,
            LayoutAlign.Right => JustificationValues.Right,
            _ => JustificationValues.Left,
        };
        body.Append(Para(b.Content, fontFamily, baseSize + b.FontSizeDelta, italic: b.Italic, align: align, multiline: true));
    }

    private static void AppendDivider(Body body, DividerBlockModel b, string fontFamily, string accentHex)
    {
        switch (b.Style)
        {
            case LayoutDividerStyle.Line:
                body.Append(Para("", fontFamily, 1, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "D9D9D9" }));
                break;
            case LayoutDividerStyle.Accent:
                body.Append(Para("", fontFamily, 1, bottomBorder: new BottomBorder { Val = BorderValues.Single, Size = 8, Color = accentHex }));
                break;
            default: // Space
                var afterTwips = (int)Math.Round(Math.Max(4, b.SpacePt) * 20);
                body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = afterTwips.ToString() })));
                break;
        }
    }

    // ---- paragraph/run builder -------------------------------------------------------

    /// <summary>
    /// The single paragraph builder used throughout this renderer, so element
    /// order (RunFonts, Bold, Italic, Color, FontSize inside rPr; ParagraphBorders
    /// then Justification inside pPr) is correct in exactly one place.
    /// </summary>
    private static Paragraph Para(
        string text, string fontFamily, double sizePt,
        bool bold = false, bool italic = false, string? colorHex = null,
        JustificationValues? align = null, bool multiline = false,
        BottomBorder? bottomBorder = null)
    {
        var rPr = new RunProperties();
        rPr.Append(new RunFonts { Ascii = fontFamily, HighAnsi = fontFamily });
        if (bold) rPr.Append(new Bold());
        if (italic) rPr.Append(new Italic());
        if (colorHex != null) rPr.Append(new Color { Val = colorHex });
        rPr.Append(new FontSize { Val = ((int)Math.Round(sizePt * 2)).ToString() });

        var run = new Run(rPr);
        if (multiline)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
                if (i < lines.Length - 1) run.Append(new Break());
            }
        }
        else
        {
            run.Append(new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve });
        }

        var pPrChildren = new List<OpenXmlElement>();
        if (bottomBorder != null) pPrChildren.Add(new ParagraphBorders(bottomBorder));
        if (align != null) pPrChildren.Add(new Justification { Val = align.Value });

        var paragraph = new Paragraph();
        if (pPrChildren.Count > 0)
        {
            paragraph.Append(new ParagraphProperties(pPrChildren.ToArray()));
        }
        paragraph.Append(run);
        return paragraph;
    }
}
