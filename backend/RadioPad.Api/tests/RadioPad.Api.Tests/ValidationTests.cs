using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;
using Xunit;

namespace RadioPad.Api.Tests;

public class ValidationEngineTests
{
    private static RulebookSpec ChestCt() => RulebookSpec.FromYaml("""
        rulebook_id: chest_ct_v1
        name: Chest CT
        version: 1.0.0
        owner: Test
        status: approved
        applies_to:
          modalities: ["CT"]
          body_parts: ["Chest"]
        style:
          tone: concise_clinical
          impression_max_bullets: 5
          avoid_terms:
            - unremarkable
        required_sections:
          - Indication
          - Findings
          - Impression
        rules:
          - id: laterality_consistency
            severity: blocker
            description: Laterality must match between findings and impression.
          - id: measurement_consistency
            severity: warning
            description: Measurements must match between sections.
          - id: negation_conflict
            severity: blocker
            description: Findings denials must not be asserted in impression.
          - id: critical_result_language
            severity: blocker
            description: Critical findings need approved escalation language.
        """);

    [Fact]
    public void RequiredSectionMissingIsBlocker()
    {
        var report = new Report
        {
            Indication = "Cough",
            Findings = "Clear lungs.",
            Impression = "",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.True(v.BlockerPresent);
        Assert.Contains(v.Findings, f => f.RuleId == "required_section:impression");
    }

    [Fact]
    public void LateralityConflictDetected()
    {
        var report = new Report
        {
            Indication = "Pain",
            Findings = "Left lower lobe consolidation.",
            Impression = "Right lower lobe pneumonia.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.Contains(v.Findings, f => f.RuleId == "laterality_consistency");
        Assert.True(v.BlockerPresent);
        Assert.Contains(v.Findings, f => f.RuleId == "laterality_consistency" && f.Severity == "Blocker");
    }

    [Fact]
    public void LungRadsBareFourDoesNotSatisfyCategoryRule()
    {
        var spec = RulebookSpec.FromYaml("""
            rulebook_id: lung_lungrads_v1
            name: Lung-RADS
            version: 1.0.0
            owner: Test
            status: approved
            required_sections: [Impression]
            rules:
              - id: lungrads_category_required
                severity: blocker
                description: Lung-RADS category required.
            """);
        var report = new Report { Impression = "Lung-RADS 4." };
        var v = new ReportValidator().Validate(report, spec);
        Assert.True(v.BlockerPresent);
        Assert.Contains(v.Findings, f => f.RuleId == "lungrads_category_required");
    }

    [Fact]
    public void MeasurementInImpressionNotInFindings()
    {
        var report = new Report
        {
            Indication = "Nodule",
            Findings = "Nodule present in left upper lobe.",
            Impression = "Nodule measuring 12 mm in left upper lobe.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.Contains(v.Findings, f => f.RuleId == "measurement_consistency");
    }

    [Fact]
    public void NegationConflictDetected()
    {
        var report = new Report
        {
            Indication = "Trauma",
            Findings = "No pneumothorax.",
            Impression = "Small pneumothorax noted.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.Contains(v.Findings, f => f.RuleId == "negation_conflict");
    }

    [Fact]
    public void CriticalLanguageRequiresEscalationPhrase()
    {
        var report = new Report
        {
            Indication = "CP",
            Findings = "Aortic dissection noted.",
            Impression = "Critical finding: type A aortic dissection.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.Contains(v.Findings, f => f.RuleId == "critical_result_language");
    }

    [Fact]
    public void AvoidTermFlagged()
    {
        var report = new Report
        {
            Indication = "Routine",
            Findings = "Lungs are unremarkable.",
            Impression = "1. No acute findings.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.Contains(v.Findings, f => f.RuleId == "style:avoid_term");
    }

    [Fact]
    public void CleanReportProducesNoBlockers()
    {
        var report = new Report
        {
            Indication = "Cough",
            Findings = "Lungs are clear. No effusion. No pneumothorax.",
            Impression = "1. No acute cardiopulmonary process.",
        };
        var v = new ReportValidator().Validate(report, ChestCt());
        Assert.False(v.BlockerPresent);
    }
}

public class FhirSerializerTests
{
    [Fact]
    public void SerializesToFhirDiagnosticReport()
    {
        var r = new Report
        {
            Findings = "Lungs clear.",
            Impression = "1. No acute findings.",
            Study = new StudyContext { Modality = "CT", BodyPart = "Chest", AccessionNumber = "ACC123" },
        };
        var json = FhirDiagnosticReportSerializer.Serialize(r, "dev");
        Assert.Contains("\"resourceType\": \"DiagnosticReport\"", json);
        Assert.Contains("\"conclusion\": \"1. No acute findings.\"", json);
        Assert.Contains("ACC123", json);
    }
}

public class PhiDetectorTests
{
    [Fact]
    public void MrnTriggersPhi()
    {
        var r = new Report { Findings = "Patient MRN: 123456 — clear lungs." };
        Assert.True(ReportingService.ContainsPhi(r));
    }

    [Fact]
    public void DateOfBirthTriggersPhi()
    {
        var r = new Report { Findings = "DOB 5/14/1972 — clear lungs." };
        Assert.True(ReportingService.ContainsPhi(r));
    }

    [Fact]
    public void GenericDictationDoesNotTriggerPhi()
    {
        var r = new Report { Findings = "Lungs are clear. No effusion." };
        Assert.False(ReportingService.ContainsPhi(r));
    }
}
