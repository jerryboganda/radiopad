using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Hardening for UBAG's browser-automation Gemini web session: a deterministic, code-side check
/// that the model's response actually matches the mandated FINDINGS heading+bullet layout and
/// numbered IMPRESSION — the same structural contract the consultant doctrine's prompts specify.
/// </summary>
public class ConsultantOutputComplianceTests
{
    [Fact]
    public void Compliant_Findings_And_Impression_Have_No_Violations()
    {
        var findings = "KIDNEYS:\n• Right lower pole calculus 5.5 x 7.1 mm, non-obstructive.\n\n" +
                        "LIVER, SPLEEN, PANCREAS:\n• Normal in morphology.";
        var impression = "1. Non-obstructive right renal calculus.\n2. No acute intra-abdominal process.";

        Assert.Empty(ConsultantOutputCompliance.Check(findings, impression));
    }

    [Fact]
    public void Flowing_Prose_Findings_With_No_Heading_Or_Bullet_Is_A_Violation()
    {
        var findings = "The kidneys are normal in size. The right kidney contains multiple non-obstructive " +
                        "calculi, the largest measuring 5.5 x 7.1 mm.";

        var violations = ConsultantOutputCompliance.CheckFindings(findings);

        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("heading"));
        Assert.Contains(violations, v => v.Contains("bullet"));
    }

    [Fact]
    public void Findings_With_Heading_But_No_Bullet_Flags_Only_The_Bullet_Violation()
    {
        var findings = "KIDNEYS:\nRight lower pole calculus present, non-obstructive.";

        var violations = ConsultantOutputCompliance.CheckFindings(findings);

        Assert.Single(violations);
        Assert.Contains("bullet", violations[0]);
    }

    [Fact]
    public void Telegraphic_Unnumbered_Impression_Is_A_Violation()
    {
        var violations = ConsultantOutputCompliance.CheckImpression("Multiple renal calculi. Gallstone.");

        Assert.Single(violations);
        Assert.Contains("numbered", violations[0]);
    }

    [Fact]
    public void Numbered_Impression_Has_No_Violations()
    {
        var impression = "1. Non-obstructive right renal calculus.\n2. Cholelithiasis without cholecystitis.";

        Assert.Empty(ConsultantOutputCompliance.CheckImpression(impression));
    }

    [Fact]
    public void Empty_Sections_Are_Not_Flagged_As_Layout_Violations()
    {
        // Missing content is a content problem the recommendations backfill / radiologist review
        // handles — not a layout violation this checker should retry over.
        Assert.Empty(ConsultantOutputCompliance.CheckFindings(""));
        Assert.Empty(ConsultantOutputCompliance.CheckFindings(null));
        Assert.Empty(ConsultantOutputCompliance.CheckImpression(""));
        Assert.Empty(ConsultantOutputCompliance.CheckImpression(null));
    }

    // ---- depth/style dimension — the "shallow but well-formatted" failure ------------------

    [Fact]
    public void Banned_Hedging_Terms_Are_Style_Violations_In_Both_Sections()
    {
        var violations = ConsultantOutputCompliance.CheckStyle(
            findings: "KIDNEYS:\n• The kidneys are unremarkable.",
            impression: "1. Cannot rule out early obstruction.");

        Assert.Equal(2, violations.Count);
        Assert.Contains("\"unremarkable\"", violations[0]);
        Assert.Contains("\"cannot rule out\"", violations[1]);
    }

    [Fact]
    public void Telegraphic_Numbered_Impression_Statement_Is_A_Style_Violation()
    {
        var violations = ConsultantOutputCompliance.CheckStyle(
            findings: null,
            impression: "1. Gallstone.\n2. Non-obstructive left renal calculus.");

        Assert.Single(violations);
        Assert.Contains("\"Gallstone.\"", violations[0]);
        Assert.Contains("telegraphic", violations[0]);
    }

    [Fact]
    public void Diagnosis_Level_Statements_With_Qualifiers_Pass_The_Style_Check()
    {
        Assert.Empty(ConsultantOutputCompliance.CheckStyle(
            findings: "KIDNEYS:\n• Normal in size, contour, and attenuation.",
            impression: "1. Non-obstructive right renal calculus.\n2. Cholelithiasis without cholecystitis."));
    }

    [Fact]
    public void Single_Anatomy_Group_Fails_Generation_Coverage_But_Not_Rewrite()
    {
        // Perfectly formatted, hedge-free, numbered — but one anatomy group. Generation must
        // flag it (systematic review mandated); rewrite must NOT (a rewrite may never add
        // structures the source lacked — the no-fabrication contract).
        var findings = "KIDNEYS:\n• Normal in size and contour.";
        var impression = "1. No acute abnormality.";

        Assert.Contains(ConsultantOutputCompliance.CheckForGeneration(findings, impression),
            v => v.Contains("anatomy/system group"));
        Assert.Empty(ConsultantOutputCompliance.CheckForRewrite(findings, impression));
    }

    [Fact]
    public void Fully_Compliant_Systematic_Review_Passes_The_Complete_Generation_Check()
    {
        var findings = "KIDNEYS:\n• Right lower pole calculus 5.5 x 7.1 mm, non-obstructive.\n" +
                        "• No hydronephrosis, hydroureter, or perinephric stranding.\n\n" +
                        "LIVER, SPLEEN, PANCREAS:\n• Normal in morphology within the limits of a non-contrast examination.";
        var impression = "1. Non-obstructive right renal calculus.\n2. No acute intra-abdominal process.";

        Assert.Empty(ConsultantOutputCompliance.CheckForGeneration(findings, impression));
    }

    [Fact]
    public void BuildReinforcement_Lists_Every_Violation_And_The_Output_Contract()
    {
        var violations = new[] { "FINDINGS reads as prose.", "IMPRESSION is not numbered." };

        var reinforcement = ConsultantOutputCompliance.BuildReinforcement(violations, "Return JSON only.");

        Assert.Contains("FINDINGS reads as prose.", reinforcement);
        Assert.Contains("IMPRESSION is not numbered.", reinforcement);
        Assert.Contains("Return JSON only.", reinforcement);
        Assert.Contains("FORMAT CORRECTION REQUIRED", reinforcement);
    }
}
