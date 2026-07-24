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
