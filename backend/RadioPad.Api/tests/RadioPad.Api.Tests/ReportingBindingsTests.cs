using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Iter-36 — unit coverage for the modality+body-part auto-resolution that picks the
/// Approved report template (scaffolding) and rulebook (prompts) for a study.
/// </summary>
public class ReportingBindingsTests
{
    private static ReportTemplate Tmpl(string modality, string bodyPart, TemplateStatus status,
        TemplateVariant variant = TemplateVariant.Normal, int ageMinutes = 0, string contrast = "") =>
        new()
        {
            Modality = modality,
            BodyPart = bodyPart,
            Contrast = contrast,
            Status = status,
            Variant = variant,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(ageMinutes),
        };

    private static Rulebook Rb(string modalities, string bodyParts, RulebookStatus status, string version = "1.0.0") =>
        new()
        {
            AppliesToModalities = modalities,
            AppliesToBodyParts = bodyParts,
            Status = status,
            Version = version,
        };

    [Fact]
    public void Resolves_Approved_Template_And_Rulebook_ByExactKey()
    {
        var templates = new[] { Tmpl("CT", "Chest", TemplateStatus.Approved) };
        var rulebooks = new[] { Rb("CT,MR", "Chest,Abdomen", RulebookStatus.Approved) };

        var (template, rulebook) = ReportingService.ResolveBindings(templates, rulebooks, "CT", "Chest");

        Assert.NotNull(template);
        Assert.NotNull(rulebook);
    }

    [Fact]
    public void Matching_Is_CaseInsensitive()
    {
        var templates = new[] { Tmpl("CT", "Chest", TemplateStatus.Approved) };
        var rulebooks = new[] { Rb("ct", "chest", RulebookStatus.Approved) };

        var (template, rulebook) = ReportingService.ResolveBindings(templates, rulebooks, "ct", "CHEST");

        Assert.NotNull(template);
        Assert.NotNull(rulebook);
    }

    [Fact]
    public void Ignores_NonApproved_Candidates()
    {
        var templates = new[] { Tmpl("CT", "Chest", TemplateStatus.Draft) };
        var rulebooks = new[] { Rb("CT", "Chest", RulebookStatus.InReview) };

        var (template, rulebook) = ReportingService.ResolveBindings(templates, rulebooks, "CT", "Chest");

        Assert.Null(template);
        Assert.Null(rulebook);
    }

    [Fact]
    public void Prefers_Normal_Variant_Then_MostRecent()
    {
        var normal = Tmpl("CT", "Chest", TemplateStatus.Approved, TemplateVariant.Normal, ageMinutes: 1);
        var abnormalNewer = Tmpl("CT", "Chest", TemplateStatus.Approved, TemplateVariant.Abnormal, ageMinutes: 99);
        var (template, _) = ReportingService.ResolveBindings(new[] { abnormalNewer, normal }, Array.Empty<Rulebook>(), "CT", "Chest");
        Assert.Same(normal, template);
    }

    [Fact]
    public void Returns_Null_When_Key_Incomplete_Or_NoMatch()
    {
        var templates = new[] { Tmpl("CT", "Chest", TemplateStatus.Approved) };
        var rulebooks = new[] { Rb("CT", "Chest", RulebookStatus.Approved) };

        Assert.Equal((null, null), ReportingService.ResolveBindings(templates, rulebooks, "CT", ""));
        Assert.Equal((null, null), ReportingService.ResolveBindings(templates, rulebooks, "", "Chest"));

        var (t, r) = ReportingService.ResolveBindings(templates, rulebooks, "MR", "Head");
        Assert.Null(t);
        Assert.Null(r);
    }

    // --- Hybrid contrast model: 3-tier template preference -----------------------

    [Fact]
    public void Contrast_Prefers_Exact_Match_Over_Agnostic()
    {
        var withContrast = Tmpl("CT", "Abdomen & Pelvis", TemplateStatus.Approved, contrast: "With");
        var plain = Tmpl("CT", "Abdomen & Pelvis", TemplateStatus.Approved, contrast: "None");
        var agnostic = Tmpl("CT", "Abdomen & Pelvis", TemplateStatus.Approved, contrast: "");
        var templates = new[] { agnostic, plain, withContrast };

        var (t, _) = ReportingService.ResolveBindings(templates, Array.Empty<Rulebook>(), "CT", "Abdomen & Pelvis", "With");
        Assert.Same(withContrast, t);

        var (t2, _) = ReportingService.ResolveBindings(templates, Array.Empty<Rulebook>(), "CT", "Abdomen & Pelvis", "None");
        Assert.Same(plain, t2);
    }

    [Fact]
    public void Contrast_FallsBack_To_Agnostic_Then_Any()
    {
        // No exact contrast match → tier 2 picks the contrast-agnostic template.
        var agnostic = Tmpl("CT", "Chest", TemplateStatus.Approved, contrast: "");
        var withContrast = Tmpl("CT", "Chest", TemplateStatus.Approved, contrast: "With");
        var (t, _) = ReportingService.ResolveBindings(new[] { withContrast, agnostic }, Array.Empty<Rulebook>(), "CT", "Chest", "None");
        Assert.Same(agnostic, t);

        // No exact and no agnostic → tier 3 returns any (modality, body part) match.
        var (t2, _) = ReportingService.ResolveBindings(new[] { withContrast }, Array.Empty<Rulebook>(), "CT", "Chest", "None");
        Assert.Same(withContrast, t2);
    }

    [Fact]
    public void Contrast_Null_Or_Empty_Behaves_As_Before()
    {
        // Backward-compat: omitting contrast prefers the agnostic template, else any.
        var agnostic = Tmpl("MR", "Brain", TemplateStatus.Approved, contrast: "");
        var withContrast = Tmpl("MR", "Brain", TemplateStatus.Approved, contrast: "With");
        var (t, _) = ReportingService.ResolveBindings(new[] { withContrast, agnostic }, Array.Empty<Rulebook>(), "MR", "Brain");
        Assert.Same(agnostic, t);
    }
}
