using System.Collections.Generic;
using System.Linq;
using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §5.6 — deterministic sentinel that WARNS (does not silently reject) when left/right,
/// presence/absence (negation), or patient sex appear to have been flipped between the dictation
/// and the formatted output. SAFETY-CRITICAL (brief §8).
/// </summary>
public class LateralityNegationSentinelTests
{
    private static Dictionary<string, string> Sections(string findings = "", string impression = "") =>
        new() { ["findings"] = findings, ["impression"] = impression };

    // ── Laterality ───────────────────────────────────────────────────────

    [Fact]
    public void Warns_On_Laterality_Flip()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("mass in the right kidney", Sections(findings: "Mass in the left kidney."));

        Assert.Contains(result.Warnings, w => w.Kind == SentinelKind.Laterality);
    }

    [Fact]
    public void No_Warning_When_Laterality_Consistent()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("mass in the right kidney", Sections(findings: "Mass in the right kidney."));

        Assert.DoesNotContain(result.Warnings, w => w.Kind == SentinelKind.Laterality);
    }

    [Fact]
    public void Warns_When_Laterality_Introduced_Not_Dictated()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("renal mass", Sections(findings: "Left renal mass."));

        Assert.Contains(result.Warnings, w => w.Kind == SentinelKind.Laterality);
    }

    // ── Negation ─────────────────────────────────────────────────────────

    [Fact]
    public void Warns_On_Dropped_Negation()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("no pneumothorax", Sections(findings: "Pneumothorax."));

        Assert.Contains(result.Warnings, w => w.Kind == SentinelKind.Negation);
    }

    [Fact]
    public void No_Warning_When_Negation_Preserved_Through_Rephrasing()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("no effusion", Sections(findings: "No pleural effusion is seen."));

        Assert.DoesNotContain(result.Warnings, w => w.Kind == SentinelKind.Negation);
    }

    // ── Gender / sex ─────────────────────────────────────────────────────

    [Fact]
    public void Warns_On_Male_Anatomy_In_Female_Study()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("pelvic study", Sections(findings: "The prostate is enlarged."), patientSex: "female");

        Assert.Contains(result.Warnings, w => w.Kind == SentinelKind.Gender);
    }

    [Fact]
    public void No_Gender_Warning_When_Consistent()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("pelvic study", Sections(findings: "The uterus is normal."), patientSex: "female");

        Assert.DoesNotContain(result.Warnings, w => w.Kind == SentinelKind.Gender);
    }

    [Fact]
    public void No_Gender_Check_When_Sex_Unknown()
    {
        var sentinel = new LateralityNegationSentinel();
        var result = sentinel.Check("pelvic study", Sections(findings: "The prostate is enlarged."), patientSex: null);

        Assert.DoesNotContain(result.Warnings, w => w.Kind == SentinelKind.Gender);
    }
}
