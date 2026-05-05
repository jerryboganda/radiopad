using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-30 BILL-003 — exercises <see cref="BillingInvoiceExporter"/> at the
/// service layer (Stripe is not required). Verifies CSV header, ZIP shape,
/// and that <c>manifest.txt</c> contains correct SHA-256 hashes for every
/// file in the archive.
/// </summary>
public class BulkInvoiceExportTests
{
    [Fact]
    public void BuildCsv_Has_Header_And_All_Rows()
    {
        var rows = new[]
        {
            new BulkInvoiceRow("in_1", "RP-0001", "2026-01-01/2026-02-01", 5000, "usd", "paid", "https://stripe/inv/1"),
            new BulkInvoiceRow("in_2", "RP-0002", "2026-02-01/2026-03-01", 7500, "usd", "open", "https://stripe/inv/2"),
        };
        var bytes = BillingInvoiceExporter.BuildCsv(rows);
        var text = Encoding.UTF8.GetString(bytes).Replace("\r", "");
        var lines = text.Trim('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("id", lines[0]);
        Assert.Contains("amountCents", lines[0]);
        Assert.Contains("in_1", lines[1]);
        Assert.Contains("in_2", lines[2]);
    }

    [Fact]
    public void BuildZip_Contains_Manifest_With_Valid_Hashes()
    {
        var rows = new[]
        {
            new BulkInvoiceRow("in_a", "RP-A", "2026-01-01/2026-02-01", 1000, "usd", "paid", "https://stripe/a"),
            new BulkInvoiceRow("in_b", "RP-B", "2026-02-01/2026-03-01", 2000, "usd", "paid", "https://stripe/b"),
        };
        var result = BillingInvoiceExporter.BuildZip(rows, "it");
        Assert.Equal(2, result.InvoiceCount);
        Assert.True(result.ZipBytes.Length > 0);

        using var ms = new MemoryStream(result.ZipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var manifestEntry = zip.GetEntry("manifest.txt");
        Assert.NotNull(manifestEntry);
        using var mr = new StreamReader(manifestEntry!.Open());
        var manifest = mr.ReadToEnd();
        var manifestLines = manifest.Replace("\r", "").Trim('\n').Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        Assert.True(manifestLines.Count >= 3);

        // CSV must be present
        Assert.NotNull(zip.GetEntry("invoices.csv"));
        // Per-invoice PDFs (named after invoice Number when present)
        Assert.NotNull(zip.GetEntry("invoices/RP-A.pdf"));
        Assert.NotNull(zip.GetEntry("invoices/RP-B.pdf"));

        // Verify each manifest hash matches the corresponding entry.
        // Manifest format: "<path>|<sha256>" per line.
        foreach (var line in manifestLines)
        {
            var parts = line.Split('|', 2);
            Assert.Equal(2, parts.Length);
            var path = parts[0].Trim();
            var hash = parts[1].Trim().ToLowerInvariant();
            if (path == "manifest.txt") continue;
            var entry = zip.GetEntry(path);
            Assert.NotNull(entry);
            using var es = entry!.Open();
            using var copy = new MemoryStream();
            es.CopyTo(copy);
            var actual = Convert.ToHexString(SHA256.HashData(copy.ToArray())).ToLowerInvariant();
            Assert.Equal(hash, actual);
        }
    }
}
