using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public static class RolePermissionMap
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<RbacPermission>> Map =
        new Dictionary<UserRole, IReadOnlySet<RbacPermission>>
        {
            [UserRole.Radiologist] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsSign,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                // Operator decision (2026-07-20): choosing the AI engine belongs to the
                // radiologist, not only to an administrator. Without ProvidersRead the
                // desktop AI panel 403s and tells them "provider details are managed by
                // your workspace admin" -- they could not see, let alone choose between,
                // cloud / UBAG / on-device models. This is READ only: it does not grant
                // ProvidersManage, so a clinician cannot rewrite the tenant-wide config
                // that every other user in the org depends on.
                RbacPermission.ProvidersRead,
                // PRD §14.15 — logging and closing the loop on a critical finding is
                // the reading radiologist's own clinical duty, so they both see and
                // act on critical results.
                RbacPermission.CriticalResultsRead,
                RbacPermission.CriticalResultsManage,
                // PRD §14.13 — an attending both serves as a peer reviewer and sees the
                // reviews recorded against their own reports. Running the programme
                // (assigning, sampling, reading the concordance dashboard) is NOT theirs.
                RbacPermission.PeerReviewRead,
                RbacPermission.PeerReviewSubmit,
                // PRD §14.14 — teaching file: an attending browses the library and
                // authors de-identified cases from their own reports.
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage,
                RbacPermission.BillingRead,
                RbacPermission.UsersRead,
                RbacPermission.AuditRead),

            [UserRole.ReportingAdmin] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.RulebooksManage,
                RbacPermission.RulebooksApprove,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.TemplatesManage,
                RbacPermission.ModalitiesManage,
                RbacPermission.BodyPartsManage,
                RbacPermission.TemplatesApprove,
                RbacPermission.ProvidersRead,
                // Least-privilege (2026-06-23): system-level integration perms
                // (ProvidersManage / McpToolsManage / PromptOverridesManage) are
                // reserved for ItAdmin + MedicalDirector. ReportingAdmin keeps the
                // reporting-content perms (rulebooks/templates/prompts read, validate,
                // tenant settings) but no longer manages AI provider configs or MCP
                // tool integrations or drafts prompt overrides.
                RbacPermission.TenantSettingsManage,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                // Oversight only: a ReportingAdmin sees the critical-results compliance
                // list but does not perform the clinical communication itself.
                RbacPermission.CriticalResultsRead,
                // PRD §14.13 — the reporting admin runs the peer-review programme
                // (roster, assignment, sampling cadence, dashboard) but never scores a
                // case: scoring is a clinical judgement reserved for attendings.
                RbacPermission.PeerReviewRead,
                RbacPermission.PeerReviewManage,
                // PRD §14.14 — curates the tenant teaching library alongside the rest
                // of the reporting content estate.
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage,
                RbacPermission.BillingRead,
                RbacPermission.UsersRead,
                RbacPermission.AuditRead),

            [UserRole.MedicalDirector] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsSign,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.RulebooksManage,
                RbacPermission.RulebooksApprove,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.TemplatesManage,
                RbacPermission.ModalitiesManage,
                RbacPermission.BodyPartsManage,
                RbacPermission.TemplatesApprove,
                RbacPermission.ProvidersRead,
                // The bundled desktop runs as a single MedicalDirector operator who is
                // also the local admin; ProvidersManage lets them add/edit/enable AI
                // providers (incl. UBAG targets) from the AI-models page without a
                // developer. MedicalDirector already holds UsersManage/SecurityManage/
                // TenantSettingsManage, so this is consistent with the role's admin scope.
                RbacPermission.ProvidersManage,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                RbacPermission.UsersRead,
                RbacPermission.UsersManage,
                RbacPermission.UsersRevokeSessions,
                RbacPermission.BillingRead,
                RbacPermission.BillingManage,
                RbacPermission.SecurityManage,
                RbacPermission.TenantSettingsManage,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksManage,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.McpToolsManage,
                RbacPermission.PromptOverridesManage,
                RbacPermission.PromptOverridesApprove,
                // PRD §14.15 — the medical director both reads the compliance list and
                // may act on any critical result (escalation owner of last resort).
                RbacPermission.CriticalResultsRead,
                RbacPermission.CriticalResultsManage,
                // PRD §14.13 (PR-005) — owns the peer-review programme end to end:
                // assigns and samples cases, scores them personally, and is the only
                // clinical role that reads the per-reader concordance dashboard.
                RbacPermission.PeerReviewRead,
                RbacPermission.PeerReviewSubmit,
                RbacPermission.PeerReviewManage,
                // PRD §14.14 — owns the education programme: authors, publishes, and
                // moderates (deletes) any case in the tenant library.
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage),

            [UserRole.ComplianceReviewer] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                RbacPermission.UsersRead,
                RbacPermission.UsersRevokeSessions,
                RbacPermission.BillingRead,
                // Least-privilege (2026-06-23): SecurityManage (KMS / webhooks /
                // observability / SIEM config) is an IT/MedicalDirector responsibility;
                // a ComplianceReviewer reviews + audits but does not own security infra.
                RbacPermission.ValidationPacksRead,
                // Compliance oversight of the closed-loop communication record (read-only).
                RbacPermission.CriticalResultsRead,
                // PRD §14.13 — reads the peer-review record as part of quality oversight;
                // does not assign, score, or see the per-reader dashboard.
                RbacPermission.PeerReviewRead,
                // PRD §14.14 — read-only sight of the de-identified library so
                // compliance can confirm what was published; never authors a case.
                RbacPermission.TeachingCasesRead),

            [UserRole.ItAdmin] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.RulebooksManage,
                RbacPermission.RulebooksApprove,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.TemplatesManage,
                RbacPermission.ModalitiesManage,
                RbacPermission.BodyPartsManage,
                RbacPermission.TemplatesApprove,
                RbacPermission.ProvidersRead,
                RbacPermission.ProvidersManage,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                RbacPermission.UsersRead,
                RbacPermission.UsersManage,
                RbacPermission.UsersRevokeSessions,
                RbacPermission.BillingRead,
                RbacPermission.BillingManage,
                RbacPermission.SecurityManage,
                RbacPermission.TenantSettingsManage,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksManage,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.McpToolsManage,
                // Gains PromptOverridesManage (moved off ReportingAdmin) so the
                // manage/approve separation-of-duties survives: ItAdmin manages prompt
                // overrides, MedicalDirector approves them.
                RbacPermission.PromptOverridesManage,
                // Compliance oversight of the closed-loop communication record (read-only).
                RbacPermission.CriticalResultsRead,
                // PRD §14.14 — Manage here is moderation authority, not authorship:
                // it is what lets an administrator delete a case they do not own.
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage),

            [UserRole.BillingAdmin] = Set(
                RbacPermission.ProvidersRead,
                RbacPermission.AuditRead,
                RbacPermission.UsersRead,
                RbacPermission.BillingRead,
                RbacPermission.BillingManage),

            // Iter-0c (AUTH-002) — trainee: draft/edit/validate, but NEVER sign
            // or export a final report (attending signs). Aligns with the
            // never-auto-sign safety boundary applied to human roles too.
            [UserRole.Resident] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.ProvidersRead, // see Radiologist -- trainees dictate too
                // PRD §14.15 — calling a critical finding through is core trainee duty
                // on-call; it is a communication record, NOT a report signature.
                RbacPermission.CriticalResultsRead,
                RbacPermission.CriticalResultsManage,
                // PRD §14.13 (PR-008) — a resident READS the attending feedback recorded
                // against their own drafts, but does not peer-review a colleague.
                RbacPermission.PeerReviewRead,
                // PRD §14.14 — residents are the teaching file's primary audience AND a
                // primary author; the library is de-identified, so this grants no PHI.
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage),

            // Iter-0c (AUTH-002) — senior trainee: as Resident plus export of
            // preliminary reports; final sign still reserved for attendings.
            [UserRole.Fellow] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.ProvidersRead, // see Radiologist -- trainees dictate too
                RbacPermission.CriticalResultsRead,
                RbacPermission.CriticalResultsManage,
                // PRD §14.13 (PR-008) — a senior trainee may score a resident draft in
                // educational mode, so unlike a Resident they hold Submit.
                RbacPermission.PeerReviewRead,
                RbacPermission.PeerReviewSubmit,
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage),

            // Iter-0c (AUTH-002) — attending subspecialist: full reporting
            // authority, identical to a general Radiologist.
            [UserRole.Subspecialist] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.ReportsDraft,
                RbacPermission.ReportsEdit,
                RbacPermission.ReportsValidate,
                RbacPermission.ReportsSign,
                RbacPermission.ReportsExport,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.ProvidersRead, // see Radiologist -- engine choice is the clinician's
                RbacPermission.CriticalResultsRead,
                RbacPermission.CriticalResultsManage,
                // PRD §14.13 — attending reporting authority carries peer-review duty.
                RbacPermission.PeerReviewRead,
                RbacPermission.PeerReviewSubmit,
                RbacPermission.TeachingCasesRead,
                RbacPermission.TeachingCasesManage,
                RbacPermission.BillingRead,
                RbacPermission.UsersRead,
                RbacPermission.AuditRead),

            // Iter-0c (AUTH-002) — research user: read-only access to
            // de-identified reporting artifacts. No sign/edit/export of PHI.
            [UserRole.Researcher] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ValidationPacksRead,
                // PRD §14.14 — the teaching library IS the de-identified corpus this
                // role exists to read. Read only: a researcher never authors or edits.
                RbacPermission.TeachingCasesRead),

            // Iter-0c (AUTH-002) — read-only auditor: read everything + audit
            // verify/export; no mutations anywhere.
            [UserRole.Auditor] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.ModalitiesRead,
                RbacPermission.BodyPartsRead,
                RbacPermission.ProvidersRead,
                RbacPermission.ValidationPacksRead,
                RbacPermission.UsersRead,
                RbacPermission.BillingRead,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                // Read-only auditor: sees the closed-loop record, never mutates it.
                RbacPermission.CriticalResultsRead,
                // Same posture for the peer-review record: readable evidence, never scored.
                RbacPermission.PeerReviewRead,
                RbacPermission.TeachingCasesRead),
        };

    public static IReadOnlySet<RbacPermission> ForRole(UserRole role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : Empty;

    public static IReadOnlyCollection<UserRole> RolesFor(RbacPermission permission) =>
        Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToArray();

    private static readonly IReadOnlySet<RbacPermission> Empty = new HashSet<RbacPermission>();

    private static IReadOnlySet<RbacPermission> Set(params RbacPermission[] permissions) =>
        new HashSet<RbacPermission>(permissions);
}
