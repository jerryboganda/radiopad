using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public static class PermissionCatalog
{
    private static readonly IReadOnlyDictionary<RbacPermission, PermissionDefinition> Definitions =
        new Dictionary<RbacPermission, PermissionDefinition>
        {
            [RbacPermission.ReportsRead] = Define(RbacPermission.ReportsRead, "reports.read", "Read tenant reports."),
            [RbacPermission.ReportsDraft] = Define(RbacPermission.ReportsDraft, "reports.draft", "Create draft reports."),
            [RbacPermission.ReportsEdit] = Define(RbacPermission.ReportsEdit, "reports.edit", "Edit draft report content.", clinical: true),
            [RbacPermission.ReportsValidate] = Define(RbacPermission.ReportsValidate, "reports.validate", "Run report validation.", clinical: true),
            [RbacPermission.ReportsSign] = Define(RbacPermission.ReportsSign, "reports.sign", "Sign reports.", clinical: true),
            [RbacPermission.ReportsExport] = Define(RbacPermission.ReportsExport, "reports.export", "Export acknowledged reports.", clinical: true),
            [RbacPermission.RulebooksRead] = Define(RbacPermission.RulebooksRead, "rulebooks.read", "Read tenant rulebooks."),
            [RbacPermission.RulebooksManage] = Define(RbacPermission.RulebooksManage, "rulebooks.manage", "Create and update rulebooks.", clinical: true),
            [RbacPermission.RulebooksApprove] = Define(RbacPermission.RulebooksApprove, "rulebooks.approve", "Approve or deprecate rulebooks.", clinical: true),
            [RbacPermission.TemplatesRead] = Define(RbacPermission.TemplatesRead, "templates.read", "Read report templates."),
            [RbacPermission.TemplatesManage] = Define(RbacPermission.TemplatesManage, "templates.manage", "Create and update report templates.", clinical: true),
            [RbacPermission.TemplatesApprove] = Define(RbacPermission.TemplatesApprove, "templates.approve", "Approve or deprecate report templates.", clinical: true),
            [RbacPermission.ProvidersRead] = Define(RbacPermission.ProvidersRead, "providers.read", "Read provider configuration metadata."),
            [RbacPermission.ProvidersManage] = Define(RbacPermission.ProvidersManage, "providers.manage", "Manage AI provider configuration."),
            [RbacPermission.AuditRead] = Define(RbacPermission.AuditRead, "audit.read", "Read tenant audit events."),
            [RbacPermission.AuditVerify] = Define(RbacPermission.AuditVerify, "audit.verify", "Verify audit integrity."),
            [RbacPermission.AuditExport] = Define(RbacPermission.AuditExport, "audit.export", "Export audit/security evidence."),
            [RbacPermission.UsersRead] = Define(RbacPermission.UsersRead, "users.read", "Read tenant users."),
            [RbacPermission.UsersManage] = Define(RbacPermission.UsersManage, "users.manage", "Lock, unlock, or update tenant users."),
            [RbacPermission.UsersRevokeSessions] = Define(RbacPermission.UsersRevokeSessions, "users.revoke_sessions", "Revoke a user's active sessions."),
            [RbacPermission.BillingRead] = Define(RbacPermission.BillingRead, "billing.read", "Read tenant billing status."),
            [RbacPermission.BillingManage] = Define(RbacPermission.BillingManage, "billing.manage", "Manage billing, invoices, refunds, and marketplace payments."),
            [RbacPermission.SecurityManage] = Define(RbacPermission.SecurityManage, "security.manage", "Manage tenant security settings and alert delivery."),
            [RbacPermission.TenantSettingsManage] = Define(RbacPermission.TenantSettingsManage, "tenant_settings.manage", "Manage tenant operational settings."),
            [RbacPermission.ValidationPacksRead] = Define(RbacPermission.ValidationPacksRead, "validation_packs.read", "Read clinical validation packs."),
            [RbacPermission.ValidationPacksManage] = Define(RbacPermission.ValidationPacksManage, "validation_packs.manage", "Create and approve validation packs.", clinical: true),
            [RbacPermission.ValidationPacksRun] = Define(RbacPermission.ValidationPacksRun, "validation_packs.run", "Run validation packs.", clinical: true),
            [RbacPermission.McpToolsInvoke] = Define(RbacPermission.McpToolsInvoke, "mcp_tools.invoke", "Invoke approved MCP tools."),
            [RbacPermission.McpToolsManage] = Define(RbacPermission.McpToolsManage, "mcp_tools.manage", "Register, approve, test, or revoke MCP tools."),
            [RbacPermission.PromptOverridesManage] = Define(RbacPermission.PromptOverridesManage, "prompt_overrides.manage", "Create, update, or delete prompt overrides.", clinical: true),
            [RbacPermission.PromptOverridesApprove] = Define(RbacPermission.PromptOverridesApprove, "prompt_overrides.approve", "Approve prompt overrides for runtime use.", clinical: true),
            [RbacPermission.ModalitiesRead] = Define(RbacPermission.ModalitiesRead, "modalities.read", "Read the imaging-modality catalog."),
            [RbacPermission.ModalitiesManage] = Define(RbacPermission.ModalitiesManage, "modalities.manage", "Create, update, or delete imaging modalities.", clinical: true),
            [RbacPermission.BodyPartsRead] = Define(RbacPermission.BodyPartsRead, "body_parts.read", "Read the body-part catalog."),
            [RbacPermission.BodyPartsManage] = Define(RbacPermission.BodyPartsManage, "body_parts.manage", "Create, update, or delete body parts.", clinical: true),
            // PRD §14.15 (CR-001..010) — critical-results communication tracking.
            // Read covers the radiologist queue AND the compliance/medical-director list;
            // Manage is the clinical act of logging, communicating, acknowledging, and closing.
            [RbacPermission.CriticalResultsRead] = Define(RbacPermission.CriticalResultsRead, "critical_results.read", "Read critical results and the overdue queue."),
            [RbacPermission.CriticalResultsManage] = Define(RbacPermission.CriticalResultsManage, "critical_results.manage", "Log, communicate, acknowledge, escalate, or close critical results.", clinical: true),
            // PRD §14.14 (TF-001..008) — teaching file. Read is the library browse;
            // Manage is authoring, publishing, and deleting a de-identified case.
            [RbacPermission.TeachingCasesRead] = Define(RbacPermission.TeachingCasesRead, "teaching_cases.read", "Browse the de-identified teaching library."),
            [RbacPermission.TeachingCasesManage] = Define(RbacPermission.TeachingCasesManage, "teaching_cases.manage", "Create, edit, publish, or delete teaching cases.", clinical: true),
            // PRD §14.13 (PR-001..010) — RADPEER-aligned peer review.
            // Read = my review queue + the reviews recorded against a report.
            // Submit = the clinical act of scoring a peer's report (attendings/fellows).
            // Manage = run the quality program: assign, random-sample, and read the
            // per-reader concordance dashboard (PR-005) — director/quality-admin only.
            [RbacPermission.PeerReviewRead] = Define(RbacPermission.PeerReviewRead, "peer_review.read", "Read peer-review assignments and the reviews recorded against a report."),
            [RbacPermission.PeerReviewSubmit] = Define(RbacPermission.PeerReviewSubmit, "peer_review.submit", "Submit a RADPEER score for an assigned peer review, or dispute one.", clinical: true),
            [RbacPermission.PeerReviewManage] = Define(RbacPermission.PeerReviewManage, "peer_review.manage", "Assign peer reviews, run random sampling, and read the quality/concordance dashboard."),
        };

    public static IReadOnlyCollection<PermissionDefinition> All => Definitions.Values.ToArray();

    public static PermissionDefinition Get(RbacPermission permission) =>
        Definitions.TryGetValue(permission, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Permission '{permission}' is not defined in the catalog.");

    public static bool TryGet(RbacPermission permission, out PermissionDefinition definition) =>
        Definitions.TryGetValue(permission, out definition!);

    private static PermissionDefinition Define(
        RbacPermission permission,
        string key,
        string description,
        bool clinical = false) =>
        new(permission, key, description, clinical);
}
