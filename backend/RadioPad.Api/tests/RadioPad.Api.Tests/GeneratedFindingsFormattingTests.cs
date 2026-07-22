using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests;

public class GeneratedFindingsFormattingTests
{
    [Fact]
    public void Separates_Run_On_Headings_And_Adds_Bullets()
    {
        const string raw = "CRANIAL VAULT / EXTRA-AXIAL SPACES: No hemorrhage. BRAIN PARENCHYMA: Chronic right frontal encephalomalacia.";

        var formatted = ReportingService.FormatGeneratedFindings(raw);

        Assert.Equal(
            "CRANIAL VAULT / EXTRA-AXIAL SPACES:\n• No hemorrhage.\n\nBRAIN PARENCHYMA:\n• Chronic right frontal encephalomalacia.",
            formatted);
    }

    [Fact]
    public void Preserves_Already_Structured_Findings()
    {
        const string raw = "BONES / SINUSES:\n• No acute fracture.\n\nSOFT TISSUES:\n• No focal swelling.";

        Assert.Equal(raw, ReportingService.FormatGeneratedFindings(raw));
    }

    [Fact]
    public void Leaves_Unlabelled_Prose_Unchanged()
    {
        const string raw = "No acute intracranial abnormality.";

        Assert.Equal(raw, ReportingService.FormatGeneratedFindings(raw));
    }

    /// <summary>
    /// MedGemma (on-device) sometimes writes "*" instead of the requested "•" bullet. Without
    /// normalization this would be treated as unformatted and double-bulleted as "• * ...".
    /// </summary>
    [Fact]
    public void Normalizes_Asterisk_Bullets_To_The_Standard_Marker()
    {
        const string raw = "KIDNEYS:\n* Normal in size and cortical thickness bilaterally.\n* No hydronephrosis.";

        Assert.Equal(
            "KIDNEYS:\n• Normal in size and cortical thickness bilaterally.\n• No hydronephrosis.",
            ReportingService.FormatGeneratedFindings(raw));
    }
}
