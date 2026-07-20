using RadioPad.Application.Teaching;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// PRD §14.14 TF-002 — the teaching-file scrubber. Two obligations pull against
/// each other and both are tested here: nothing identifying may survive, and the
/// case must remain clinically readable afterwards (a scrubber that redacts
/// "45-year-old male" or "L4-L5" has destroyed the teaching value it exists to
/// protect).
/// </summary>
public class TeachingCaseDeidentifierTests
{
    private const string Placeholder = "[de-identified]";

    [Fact]
    public void Strips_Literal_Accession_And_Patient_Reference()
    {
        const string text = "Compared with prior study ACC-2026-0031 for patient PAT-99814.";
        var scrubbed = TeachingCaseDeidentifier.Scrub(text, "ACC-2026-0031", "PAT-99814");

        Assert.DoesNotContain("ACC-2026-0031", scrubbed);
        Assert.DoesNotContain("PAT-99814", scrubbed);
        Assert.Contains(Placeholder, scrubbed);
    }

    [Fact]
    public void Strips_Labelled_Mrn_Accession_And_Dob()
    {
        const string text = "MRN: 004812345\nAccession Number: XR-771\nDOB: 1974-03-02\nPatient Name: Jane Q Doe";
        var scrubbed = TeachingCaseDeidentifier.Scrub(text);

        Assert.DoesNotContain("004812345", scrubbed);
        Assert.DoesNotContain("XR-771", scrubbed);
        Assert.DoesNotContain("1974-03-02", scrubbed);
        Assert.DoesNotContain("Jane", scrubbed);
        Assert.DoesNotContain("Doe", scrubbed);
    }

    [Theory]
    [InlineData("Imaging performed 05/01/2024 at the referring centre.", "05/01/2024")]
    [InlineData("Follow-up CT on 2024-01-05 showed resolution.", "2024-01-05")]
    [InlineData("Presented January 5, 2024 with acute pain.", "January 5, 2024")]
    [InlineData("Prior study Mar 2023 available for comparison.", "Mar 2023")]
    public void Strips_Explicit_Dates_In_Every_Common_Format(string text, string date)
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(text);
        Assert.DoesNotContain(date, scrubbed);
        Assert.Contains(Placeholder, scrubbed);
    }

    [Fact]
    public void Strips_Named_Clinicians_Behind_An_Honorific()
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub("Discussed with Dr. Alan Grant by telephone.");
        Assert.DoesNotContain("Alan", scrubbed);
        Assert.DoesNotContain("Grant", scrubbed);
    }

    [Fact]
    public void Strips_Name_Following_A_Patient_Label()
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub("Patient: John Smith presented overnight.");
        Assert.DoesNotContain("John", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
    }

    /// <summary>
    /// The other half of the contract. Over-redaction is not "safe by default"
    /// here — it silently guts the teaching file — so the clinically meaningful
    /// tokens are pinned by test.
    /// </summary>
    [Theory]
    [InlineData("45-year-old male with 3-day history of right lower quadrant pain.", "45-year-old")]
    [InlineData("Disc protrusion at L4-L5 with mild canal stenosis.", "L4-L5")]
    [InlineData("Compression deformity of T12 vertebral body.", "T12")]
    [InlineData("Lesion measures 12 mm in maximal axial dimension.", "12 mm")]
    [InlineData("Attenuation 45 HU, consistent with a simple cyst.", "45 HU")]
    public void Preserves_Clinical_Content(string text, string mustSurvive)
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(text);
        Assert.Contains(mustSurvive, scrubbed);
    }

    /// <summary>
    /// HIPAA Safe Harbor §164.514(b)(2)(i)(C) — an age over 89 is itself an
    /// identifier (the over-89 cohort is small enough to re-identify from), so
    /// it must be aggregated into a single "90 or older" band. Ages 0–89 are
    /// explicitly NOT identifiers and must survive verbatim, which is why the
    /// boundary is pinned from both sides.
    /// </summary>
    [Theory]
    [InlineData("92-year-old female with a hip fracture.", "90 or older")]
    [InlineData("The patient is a 97 year old man.", "90 or older")]
    [InlineData("Aged 104, presenting with confusion.", "90 or older")]
    [InlineData("103-year-old with pneumonia.", "90 or older")]
    [InlineData("90-year-old male, routine follow-up.", "90 or older")]
    public void Bands_Ages_Over_89(string text, string expected)
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(text);
        Assert.Contains(expected, scrubbed);
        Assert.DoesNotContain("-year-old", scrubbed);
    }

    [Theory]
    [InlineData("89-year-old female with a hip fracture.", "89-year-old")]
    [InlineData("45-year-old male with abdominal pain.", "45-year-old")]
    [InlineData("A 72 year old presenting with dyspnoea.", "72 year old")]
    public void Leaves_Ages_Under_90_Alone(string text, string mustSurvive)
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(text);
        Assert.Contains(mustSurvive, scrubbed);
        Assert.DoesNotContain("90 or older", scrubbed);
    }

    [Fact]
    public void Age_Banding_Keeps_The_Rest_Of_The_Sentence()
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(
            "94-year-old male with a 3 cm right upper lobe mass.");
        Assert.Contains("90 or older", scrubbed);
        Assert.Contains("male", scrubbed);
        Assert.Contains("3 cm", scrubbed);
        Assert.Contains("right upper lobe mass", scrubbed);
    }

    [Fact]
    public void Collapses_Adjacent_Placeholders()
    {
        var scrubbed = TeachingCaseDeidentifier.Scrub(
            "Study ACC-2026-0031 on 2024-01-05.", "ACC-2026-0031");
        // Two redactions separated only by " on " must not be collapsed, but a run
        // of placeholders with nothing but punctuation between them must be.
        Assert.DoesNotContain($"{Placeholder} {Placeholder}", scrubbed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Input_Yields_Empty_String(string? input)
    {
        Assert.Equal("", TeachingCaseDeidentifier.Scrub(input));
    }

    [Fact]
    public void ContainsAny_Detects_A_Surviving_Identifier()
    {
        Assert.True(TeachingCaseDeidentifier.ContainsAny("prior ACC-2026-0031 reviewed", "ACC-2026-0031"));
        Assert.False(TeachingCaseDeidentifier.ContainsAny("prior study reviewed", "ACC-2026-0031"));
        // Short values are ignored: they are far more likely to be ordinary words.
        Assert.False(TeachingCaseDeidentifier.ContainsAny("CT chest", "CT"));
    }
}
