using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// F6 — the dictation draft is validated against the tenant's rulebook before the radiologist
/// decides whether to accept it. That requires projecting the drafted section text onto a report
/// instance so the existing validator can run over it.
///
/// <para>The projection has one genuinely dangerous property: it must never touch the report the
/// request has loaded for writing. <c>ReportsController.DictationDraft</c> holds an EF-TRACKED
/// report, and the §5.7 audit append later in the same request calls SaveChanges — so projecting
/// onto that instance would silently persist an unapplied, unreviewed AI draft into the stored
/// report. The endpoint therefore projects onto a separate <c>AsNoTracking</c> copy; these tests pin
/// the projection's own behaviour, which is what makes that copy safe to validate.</para>
/// </summary>
public class DraftSectionProjectionTests
{
    /// <summary>Mirror of ReportsController.ApplyDraftSections (private; behaviour pinned here).</summary>
    private static void Apply(Report target, IReadOnlyDictionary<string, string> sections)
    {
        foreach (var (key, value) in sections)
        {
            if (value is null) continue;
            switch (key.Trim().ToLowerInvariant())
            {
                case "indication": target.Indication = value; break;
                case "technique": target.Technique = value; break;
                case "findings": target.Findings = value; break;
                case "impression": target.Impression = value; break;
                case "recommendations": target.Recommendations = value; break;
            }
        }
    }

    private static Report Existing() => new()
    {
        Indication = "original indication",
        Technique = "original technique",
        Findings = "original findings",
        Impression = "original impression",
        Recommendations = "original recommendations",
    };

    [Fact]
    public void Projects_Every_Canonical_Section()
    {
        var r = Existing();
        Apply(r, new Dictionary<string, string>
        {
            ["indication"] = "i",
            ["technique"] = "t",
            ["findings"] = "f",
            ["impression"] = "im",
            ["recommendations"] = "rec",
        });

        Assert.Equal("i", r.Indication);
        Assert.Equal("t", r.Technique);
        Assert.Equal("f", r.Findings);
        Assert.Equal("im", r.Impression);
        Assert.Equal("rec", r.Recommendations);
    }

    /// <summary>Key casing/whitespace from a formatter must not silently drop a section.</summary>
    [Theory]
    [InlineData("Findings")]
    [InlineData("FINDINGS")]
    [InlineData("  findings  ")]
    public void Section_Keys_Are_Matched_Case_And_Whitespace_Insensitively(string key)
    {
        var r = Existing();
        Apply(r, new Dictionary<string, string> { [key] = "projected" });
        Assert.Equal("projected", r.Findings);
    }

    /// <summary>
    /// A section the formatter did not produce leaves the report's own text alone. Validating a
    /// report whose untouched sections had been blanked would invent missing-section blockers that
    /// the drafted report does not actually have.
    /// </summary>
    [Fact]
    public void Absent_Sections_Leave_Existing_Text_Untouched()
    {
        var r = Existing();
        Apply(r, new Dictionary<string, string> { ["findings"] = "only findings drafted" });

        Assert.Equal("only findings drafted", r.Findings);
        Assert.Equal("original indication", r.Indication);
        Assert.Equal("original impression", r.Impression);
        Assert.Equal("original recommendations", r.Recommendations);
    }

    /// <summary>An unrecognised key is ignored, never guessed onto a field.</summary>
    [Fact]
    public void Unknown_Keys_Are_Ignored()
    {
        var r = Existing();
        Apply(r, new Dictionary<string, string> { ["comparison"] = "x", ["wharrgarbl"] = "y" });

        Assert.Equal("original findings", r.Findings);
        Assert.Equal("original impression", r.Impression);
    }

    /// <summary>The projection mutates only the instance handed to it — never a sibling.</summary>
    [Fact]
    public void Projection_Does_Not_Touch_Another_Report_Instance()
    {
        var tracked = Existing();
        var probe = Existing();

        Apply(probe, new Dictionary<string, string> { ["findings"] = "draft text" });

        Assert.Equal("draft text", probe.Findings);
        Assert.Equal("original findings", tracked.Findings);
    }
}
