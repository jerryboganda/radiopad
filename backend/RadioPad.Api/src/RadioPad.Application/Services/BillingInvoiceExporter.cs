using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-30 (BILL-003) — Enterprise invoice bulk export. Builds an in-memory
/// ZIP archive containing <c>invoices.csv</c>, one synthetic PDF per invoice,
/// and a SHA-256 <c>manifest.txt</c> listing every payload by name + hash.
///
/// PDF rendering intentionally avoids pulling Stripe content directly into
/// the archive — only fields we already retain as a thin DTO are emitted.
/// QuestPDF is the canonical PDF renderer in this codebase, but to keep the
/// exporter usable from inside test fixtures (where QuestPDF licensing
/// initialisation is fragile) we emit a minimal valid PDF that any reader
/// can open. Operators wanting branded PDF output can swap this for the
/// QuestPDF-backed renderer in <c>ReportDocumentRenderer</c>.
/// </summary>
public sealed record BulkInvoiceRow(
    string Id,
    string Number,
    string Period,
    long AmountCents,
    string Currency,
    string Status,
    string HostedInvoiceUrl);

public sealed record BulkInvoiceExportResult(byte[] ZipBytes, int InvoiceCount);

public static class BillingInvoiceExporter
{
    public static BulkInvoiceExportResult BuildZip(IReadOnlyList<BulkInvoiceRow> invoices, string tenantSlug)
    {
        var manifest = new StringBuilder();
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. invoices.csv — primary index.
            var csvBytes = WriteEntry(archive, "invoices.csv", BuildCsv(invoices));
            manifest.Append("invoices.csv|").Append(Sha256Hex(csvBytes)).Append('\n');

            // 2. one minimal PDF per invoice.
            foreach (var inv in invoices)
            {
                var safeName = string.IsNullOrWhiteSpace(inv.Number)
                    ? inv.Id
                    : inv.Number.Replace('/', '_').Replace('\\', '_');
                var pdfName = $"invoices/{safeName}.pdf";
                var pdfBytes = WriteEntry(archive, pdfName, BuildMinimalPdf(inv, tenantSlug));
                manifest.Append(pdfName).Append('|').Append(Sha256Hex(pdfBytes)).Append('\n');
            }

            // 3. manifest.txt — added LAST so it's always present.
            WriteEntry(archive, "manifest.txt", Encoding.UTF8.GetBytes(manifest.ToString()));
        }
        return new BulkInvoiceExportResult(ms.ToArray(), invoices.Count);
    }

    public static byte[] BuildCsv(IReadOnlyList<BulkInvoiceRow> invoices)
    {
        var sb = new StringBuilder();
        sb.Append("id,number,period,amountCents,currency,status,hostedInvoiceUrl\n");
        foreach (var i in invoices)
        {
            sb.Append(Csv(i.Id)).Append(',');
            sb.Append(Csv(i.Number)).Append(',');
            sb.Append(Csv(i.Period)).Append(',');
            sb.Append(i.AmountCents).Append(',');
            sb.Append(Csv(i.Currency)).Append(',');
            sb.Append(Csv(i.Status)).Append(',');
            sb.Append(Csv(i.HostedInvoiceUrl)).Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] WriteEntry(ZipArchive archive, string name, byte[] payload)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var s = entry.Open();
        s.Write(payload, 0, payload.Length);
        return payload;
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var needsQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Emits a minimal-but-valid single-page PDF (no compression, no
    /// embedded fonts) that summarises one invoice. Sufficient for archival
    /// + offline retention without taking a runtime dependency on QuestPDF.
    /// </summary>
    private static byte[] BuildMinimalPdf(BulkInvoiceRow inv, string tenantSlug)
    {
        var amount = (inv.AmountCents / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var currency = (inv.Currency ?? "").ToUpperInvariant();
        var lines = new[]
        {
            $"RadioPad Invoice — {tenantSlug}",
            $"Invoice number: {inv.Number}",
            $"Invoice id:     {inv.Id}",
            $"Period:         {inv.Period}",
            $"Amount:         {amount} {currency}",
            $"Status:         {inv.Status}",
            $"Hosted URL:     {inv.HostedInvoiceUrl}",
        };

        // Build the content stream (Helvetica, 12pt, hand-laid).
        var content = new StringBuilder();
        content.Append("BT\n/F1 12 Tf\n72 760 Td\n");
        for (int i = 0; i < lines.Length; i++)
        {
            content.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
            if (i < lines.Length - 1) content.Append("0 -16 Td\n");
        }
        content.Append("ET");
        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        var pdf = new MemoryStream();
        using var w = new BinaryWriter(pdf, Encoding.ASCII, leaveOpen: true);
        var offsets = new long[6];
        WriteAscii(w, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        offsets[1] = pdf.Position;
        WriteAscii(w, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = pdf.Position;
        WriteAscii(w, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets[3] = pdf.Position;
        WriteAscii(w, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        offsets[4] = pdf.Position;
        WriteAscii(w, $"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        w.Write(contentBytes);
        WriteAscii(w, "\nendstream\nendobj\n");
        offsets[5] = pdf.Position;
        WriteAscii(w, "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        var xref = pdf.Position;
        WriteAscii(w, "xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
        {
            WriteAscii(w, offsets[i].ToString("D10") + " 00000 n \n");
        }
        WriteAscii(w, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static void WriteAscii(BinaryWriter w, string s) => w.Write(Encoding.ASCII.GetBytes(s));

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
