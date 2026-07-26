using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (REPORT-TEMPLATES) — an in-memory, non-persisted CT-chest report used
/// by <c>POST /api/report-layouts/preview/{pdf,docx}</c> so a radiologist can see
/// true rendering fidelity for a design before it ever touches a real patient
/// report. No database row is read or written; nothing here carries PHI —
/// every value is fictional and clearly so (accession "SAMPLE-2041").
/// </summary>
public static class ReportLayoutSampleData
{
    public static Report CreateSampleReport()
    {
        var reportId = Guid.NewGuid();
        var signedAt = DateTimeOffset.UtcNow.AddHours(-2);
        return new Report
        {
            Id = reportId,
            TenantId = Guid.Empty,
            CreatedByUserId = Guid.Empty,
            Status = ReportStatus.Acknowledged,
            Priority = ReportPriority.Routine,
            Study = new StudyContext
            {
                AccessionNumber = "SAMPLE-2041",
                Modality = "CT",
                BodyPart = "Chest",
                Contrast = "With",
                Age = 58,
                Gender = "Female",
                Comparison = "CT chest, 14 months prior",
                PriorReportSummary = "Stable 4 mm right upper lobe nodule.",
                PatientReference = "SAMPLE-PATIENT",
            },
            Indication = "Persistent cough and 6 kg unintentional weight loss over three months; further evaluation.",
            Technique = "Contiguous axial CT images of the chest were obtained following intravenous "
                + "administration of iodinated contrast, with coronal and sagittal reformats. Radiation "
                + "dose-reduction techniques were applied.",
            Comparison = "CT chest dated 14 months prior.",
            Findings = "Lungs: A stable 4 mm noncalcified nodule is again seen in the right upper lobe, "
                + "unchanged from prior. No new nodule, consolidation, or ground-glass opacity. "
                + "Airways: Trachea and central airways are patent.\n"
                + "Pleura: No pleural effusion or pneumothorax.\n"
                + "Mediastinum/Hila: No mediastinal or hilar lymphadenopathy. Heart size is normal; no "
                + "pericardial effusion.\n"
                + "Chest wall/Bones: No aggressive osseous lesion. Mild multilevel degenerative changes "
                + "of the thoracic spine.\n"
                + "Upper abdomen (limited): Visualized upper abdominal organs are unremarkable.",
            Impression = "1. Stable 4 mm right upper lobe pulmonary nodule, unchanged over 14 months — "
                + "consistent with a benign etiology; no further dedicated follow-up required.\n"
                + "2. No CT evidence of a mass or consolidation to account for the reported weight loss; "
                + "clinical correlation recommended.",
            Recommendations = "Correlate clinically; consider outpatient work-up for the reported weight "
                + "loss if symptoms persist. Routine surveillance only for the pulmonary nodule per "
                + "Fleischner Society guidance.",
            Signatures = new List<ReportSignature>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ReportId = reportId,
                    TenantId = Guid.Empty,
                    UserId = Guid.Empty,
                    SignedAt = signedAt,
                    Role = SignatureRole.Primary,
                    Note = "No discrepancy from preliminary read.",
                    Hash = "sample-hash-not-cryptographic",
                },
            },
        };
    }
}
