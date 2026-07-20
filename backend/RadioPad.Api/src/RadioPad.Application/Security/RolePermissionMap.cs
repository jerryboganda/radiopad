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
                RbacPermission.PromptOverridesApprove),

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
                RbacPermission.ValidationPacksRead),

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
                RbacPermission.PromptOverridesManage),

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
                RbacPermission.ProvidersRead), // see Radiologist -- trainees dictate too

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
                RbacPermission.ProvidersRead), // see Radiologist -- trainees dictate too

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
                RbacPermission.ValidationPacksRead),

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
                RbacPermission.AuditExport),
        };

    public static IReadOnlySet<RbacPermission> ForRole(UserRole role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : Empty;

    public static IReadOnlyCollection<UserRole> RolesFor(RbacPermission permission) =>
        Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToArray();

    private static readonly IReadOnlySet<RbacPermission> Empty = new HashSet<RbacPermission>();

    private static IReadOnlySet<RbacPermission> Set(params RbacPermission[] permissions) =>
        new HashSet<RbacPermission>(permissions);
}
