using System.Text.Json;
using System.Text.Json.Serialization;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// Serializes a <see cref="Report"/> to a HL7 FHIR R4 DiagnosticReport JSON
/// resource (<c>resourceType: "DiagnosticReport"</c>). The output is a
/// minimal, valid representation: identifier, status, code, subject,
/// effectiveDateTime, conclusion, and presentedForm with the formatted
/// narrative. References to Patient/Practitioner are emitted as logical
/// references (no Bundle), suitable for transport into an EHR that will
/// resolve them locally.
/// </summary>
public static class FhirDiagnosticReportSerializer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(Report report, string? tenantSlug = null)
    {
        var resource = new
        {
            resourceType = "DiagnosticReport",
            id = report.Id.ToString(),
            meta = new
            {
                profile = new[] { "http://hl7.org/fhir/StructureDefinition/DiagnosticReport" },
                source = tenantSlug is null ? null : $"urn:radiopad:tenant:{tenantSlug}",
            },
            identifier = new[]
            {
                new
                {
                    system = "urn:radiopad:report",
                    value = report.Id.ToString(),
                },
                new
                {
                    system = "urn:radiopad:accession",
                    value = report.Study.AccessionNumber,
                },
            },
            status = report.Status switch
            {
                Domain.Enums.ReportStatus.Draft => "preliminary",
                Domain.Enums.ReportStatus.Validated => "preliminary",
                Domain.Enums.ReportStatus.Acknowledged => "final",
                Domain.Enums.ReportStatus.Exported => "final",
                _ => "preliminary",
            },
            category = new[]
            {
                new
                {
                    coding = new[]
                    {
                        new { system = "http://terminology.hl7.org/CodeSystem/v2-0074", code = "RAD", display = "Radiology" },
                    },
                },
            },
            code = new
            {
                coding = new[]
                {
                    new
                    {
                        system = "urn:radiopad:modality-bodypart",
                        code = $"{report.Study.Modality}:{report.Study.BodyPart}".ToLowerInvariant(),
                        display = $"{report.Study.Modality} {report.Study.BodyPart}".Trim(),
                    },
                },
                text = $"{report.Study.Modality} {report.Study.BodyPart}".Trim(),
            },
            subject = new { reference = string.IsNullOrEmpty(report.Study.PatientReference)
                ? null
                : report.Study.PatientReference },
            effectiveDateTime = report.UpdatedAt.UtcDateTime.ToString("o"),
            issued = DateTimeOffset.UtcNow.UtcDateTime.ToString("o"),
            conclusion = report.Impression,
            presentedForm = new[]
            {
                new
                {
                    contentType = "text/plain",
                    data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(BuildNarrative(report))),
                    title = "RadioPad narrative",
                },
            },
        };

        return JsonSerializer.Serialize(resource, Json);
    }

    public static string BuildNarrative(Report r)
    {
        var sb = new System.Text.StringBuilder();
        Section(sb, "Indication", r.Indication);
        Section(sb, "Technique", r.Technique);
        Section(sb, "Comparison", r.Comparison);
        Section(sb, "Findings", r.Findings);
        Section(sb, "Impression", r.Impression);
        Section(sb, "Recommendations", r.Recommendations);
        return sb.ToString().TrimEnd();
    }

    private static void Section(System.Text.StringBuilder sb, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        sb.Append(title.ToUpperInvariant()).Append(':').Append('\n');
        sb.Append(content.Trim()).Append("\n\n");
    }
}
