using System;
using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Option 1 (consultant-everywhere): a rewrite must read at senior-consultant grade, not
/// as the shallow editorial prose it used to produce. These capture the composed request
/// via a fake gateway and assert the consultant doctrine now leads the rewrite system
/// prompt — while proving the no-fabrication contract and the patient-facing modes'
/// deliberately-different format are preserved.
/// </summary>
public class ReportRewriteConsultantDoctrineTests
{
    [Theory]
    [InlineData(ReportRewriteMode.Concise)]
    [InlineData(ReportRewriteMode.Formal)]
    [InlineData(ReportRewriteMode.Custom)]
    public async Task Clinician_Facing_Rewrite_Leads_With_The_Consultant_Doctrine(ReportRewriteMode mode)
    {
        var gw = new CapturingGateway();
        var svc = new ReportRewriteService(gw);

        await svc.RewriteAsync(Tenant(), Report(), Provider(), mode, sections: null,
            instruction: mode == ReportRewriteMode.Custom ? "Tighten the wording." : null, ct: default);

        var sys = Squish(gw.Captured!.SystemPrompt);

        // Consultant persona + layout + impression synthesis now lead the prompt.
        Assert.Contains("senior consultant radiologist", sys);
        Assert.Contains("UPPERCASE", sys);
        Assert.Contains("• ", gw.Captured!.SystemPrompt); // the literal bullet, un-squished
        Assert.Contains("diagnosis-level", sys);
        // Fidelity / no-fabrication is asserted right in the system role.
        Assert.Contains("never add, remove, alter, or invent", sys);
        // The mode-specific editing instruction is appended, not lost.
        Assert.Contains("Editing task", sys);
    }

    [Theory]
    [InlineData(ReportRewriteMode.PatientFriendly)]
    [InlineData(ReportRewriteMode.ReferringSummary)]
    public async Task Patient_Facing_Rewrite_Keeps_Its_Format_But_Reinforces_Fidelity(ReportRewriteMode mode)
    {
        var gw = new CapturingGateway();
        var svc = new ReportRewriteService(gw);

        await svc.RewriteAsync(Tenant(), Report(), Provider(), mode, sections: null, instruction: null, ct: default);

        var sys = Squish(gw.Captured!.SystemPrompt);

        // Fidelity core is present...
        Assert.Contains("never add, remove, alter, or invent", sys);
        // ...but the clinician heading+bullet layout is NOT imposed on these modes,
        // whose plain-language / one-paragraph purpose would otherwise be defeated.
        Assert.DoesNotContain("UPPERCASE", sys);
    }

    private static string Squish(string? s) =>
        System.Text.RegularExpressions.Regex.Replace(s ?? "", "\\s+", " ").Trim();

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1" };
    private static ProviderConfig Provider() =>
        new() { Id = Guid.NewGuid(), Name = "P", Adapter = "mock", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true };

    private static Report Report() => new()
    {
        Id = Guid.NewGuid(),
        Indication = "Suspected urolithiasis.",
        Findings = "Right lower pole renal calculus 5.5 x 7.1 mm, non-obstructive.",
        Impression = "1. Renal calculus.",
        Study = new StudyContext { Modality = "CT", BodyPart = "KUB", Contrast = "Without contrast", Age = 59, Gender = "Male" },
    };

    private sealed class CapturingGateway : IAiGateway
    {
        public AiCompletionRequest? Captured { get; private set; }
        public Task<AiResult> RouteAsync(Tenant tenant, AiCompletionRequest request, CancellationToken ct)
        {
            Captured ??= request;
            // Echo the source so the Custom-mode fabrication guard finds nothing new.
            return Task.FromResult(new AiResult(request.UserPrompt, tenant.Slug, "m", 1, 10, 5, request.PromptVersion));
        }
    }
}
