using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

/// <summary>
/// Canonical documentation-in-code for high-risk tenant API endpoints that
/// must be gated by permission checks at the controller boundary. This matrix
/// intentionally tracks the current fixed
/// <see cref="UserRole"/> to <see cref="RbacPermission"/> mapping; DB-backed
/// custom roles can consume the same permission keys later.
/// </summary>
public static class EndpointPermissionMatrix
{
    private static readonly IReadOnlyCollection<EndpointPermissionDefinition> Entries =
        new[]
        {
            Define("GET", "/api/providers", RbacPermission.ProvidersRead, "Provider config metadata; secret material is never returned."),
            Define("POST", "/api/providers", RbacPermission.ProvidersManage, "Create/update AI provider configuration."),

            Define("GET", "/api/audit", RbacPermission.AuditRead, "Tenant audit query."),
            Define("GET", "/api/audit/search", RbacPermission.AuditRead, "Advanced tenant audit search."),
            Define("GET", "/api/audit/verify", RbacPermission.AuditVerify, "Audit integrity verification."),
            Define("GET", "/api/audit/siem", RbacPermission.AuditExport, "Snapshot audit/SIEM export."),
            Define("GET", "/api/siem/status", RbacPermission.AuditExport, "Read SIEM sink delivery status."),

            Define("POST", "/api/tenant/settings", RbacPermission.TenantSettingsManage, "Update tenant operational/security integration settings."),
            Define("POST", "/api/tenant/settings/kms/verify", RbacPermission.SecurityManage, "Verify configured customer-managed key."),
            Define("POST", "/api/admin/security/test-webhook", RbacPermission.SecurityManage, "Send security webhook test."),
            Define("POST", "/api/admin/observability/slo-alerts", RbacPermission.SecurityManage, "Ingest SLO/security alert."),
            Define("GET", "/api/admin/observability/availability", RbacPermission.SecurityManage, "Read synthetic availability status."),

            Define("GET", "/api/users", RbacPermission.UsersRead, "List tenant users."),
            Define("POST", "/api/users/{id}/lockout", RbacPermission.UsersManage, "Lock a tenant user."),
            Define("POST", "/api/users/{id}/unlock", RbacPermission.UsersManage, "Unlock a tenant user."),
            Define("POST", "/api/users/{id}/revoke-sessions", RbacPermission.UsersRevokeSessions, "Revoke a user's active sessions."),

            Define("POST", "/api/rulebooks", RbacPermission.RulebooksManage, "Create/update rulebooks."),
            Define("POST", "/api/rulebooks/{id}/approve", RbacPermission.RulebooksApprove, "Approve rulebooks."),
            Define("POST", "/api/rulebooks/{id}/deprecate", RbacPermission.RulebooksApprove, "Deprecate rulebooks."),
            Define("POST", "/api/rulebooks/{id}/rollback", RbacPermission.RulebooksApprove, "Rollback rulebooks."),
            Define("POST", "/api/templates", RbacPermission.TemplatesManage, "Create/update templates."),
            Define("POST", "/api/templates/{id}/approve", RbacPermission.TemplatesApprove, "Approve templates."),
            Define("POST", "/api/templates/{id}/deprecate", RbacPermission.TemplatesApprove, "Deprecate templates."),
            Define("POST", "/api/prompts/overrides", RbacPermission.PromptOverridesManage, "Create/update prompt overrides."),
            Define("DELETE", "/api/prompts/overrides/{id}", RbacPermission.PromptOverridesManage, "Delete prompt overrides."),
            Define("POST", "/api/prompts/overrides/{id}/approve", RbacPermission.PromptOverridesApprove, "Approve prompt overrides."),

            Define("POST", "/api/reports/{id}/ai", RbacPermission.ReportsEdit, "Run AI report generation."),
            Define("POST", "/api/reports/{id}/rewrite", RbacPermission.ReportsEdit, "Run AI rewrite."),
            Define("POST", "/api/reports/{id}/sign", RbacPermission.ReportsSign, "Sign reports."),
            Define("POST", "/api/reports/{id}/addendum", RbacPermission.ReportsSign, "Append signed-report addendum."),
            Define("GET", "/api/reports/{id}/export/fhir", RbacPermission.ReportsExport, "Export report as FHIR."),
            Define("GET", "/api/reports/{id}/export/json", RbacPermission.ReportsExport, "Export report as JSON."),
            Define("GET", "/api/reports/{id}/export/text", RbacPermission.ReportsExport, "Export report as text when preview=false."),
            Define("GET", "/api/reports/{id}/export/pdf", RbacPermission.ReportsExport, "Export report as PDF."),
            Define("GET", "/api/reports/{id}/export/docx", RbacPermission.ReportsExport, "Export report as DOCX."),
            Define("GET", "/api/reports/{id}/export/hl7", RbacPermission.ReportsExport, "Export report as HL7."),

            // Core report lifecycle (added 2026-06-23 RBAC hardening — previously ungated).
            Define("GET", "/api/reports", RbacPermission.ReportsRead, "List tenant reports."),
            Define("POST", "/api/reports", RbacPermission.ReportsDraft, "Create a draft report."),
            Define("GET", "/api/reports/{id}", RbacPermission.ReportsRead, "Read a report."),
            Define("PATCH", "/api/reports/{id}", RbacPermission.ReportsEdit, "Edit report fields."),
            Define("POST", "/api/reports/{id}/validate", RbacPermission.ReportsValidate, "Run rulebook validation."),
            Define("POST", "/api/reports/{id}/acknowledge", RbacPermission.ReportsEdit, "Acknowledge AI-generated text."),

            Define("POST", "/api/mcp/tools/{id}/invoke", RbacPermission.McpToolsInvoke, "Invoke a registered MCP tool."),
            Define("POST", "/api/prompts/test-golden", RbacPermission.PromptOverridesManage, "Run a golden-set test of a prompt override."),
            Define("POST", "/api/prompts/validate", RbacPermission.PromptOverridesManage, "Dry-run validate sample findings against a rulebook."),
            Define("GET", "/api/prompts/overrides", RbacPermission.PromptOverridesManage, "List prompt overrides."),
            Define("POST", "/api/templates/{id}/submit-review", RbacPermission.TemplatesManage, "Submit a template for review."),
            Define("POST", "/api/modalities", RbacPermission.ModalitiesManage, "Create/update a modality."),
            Define("DELETE", "/api/modalities/{id}", RbacPermission.ModalitiesManage, "Delete a modality."),
            Define("POST", "/api/body-parts", RbacPermission.BodyPartsManage, "Create/update a body part."),
            Define("DELETE", "/api/body-parts/{id}", RbacPermission.BodyPartsManage, "Delete a body part."),
            Define("GET", "/api/billing/status", RbacPermission.BillingRead, "Read billing status."),
            Define("GET", "/api/billing/features", RbacPermission.BillingRead, "Read plan features/entitlements."),
            Define("GET", "/api/billing/credits", RbacPermission.BillingRead, "Read AI credit usage."),
            Define("GET", "/api/validation-packs", RbacPermission.ValidationPacksRead, "List validation packs."),

            // PRD §14.15 (CR-001..010) — critical-results communication tracking.
            Define("GET", "/api/critical-results", RbacPermission.CriticalResultsRead, "List critical results (status/criticality/report/overdue filters)."),
            Define("GET", "/api/critical-results/overdue", RbacPermission.CriticalResultsRead, "List critical results past their communication deadline."),
            Define("POST", "/api/critical-results", RbacPermission.CriticalResultsManage, "Log a critical result against a report."),
            Define("POST", "/api/critical-results/{id}/communicate", RbacPermission.CriticalResultsManage, "Record communication to the ordering clinician."),
            Define("POST", "/api/critical-results/{id}/acknowledge", RbacPermission.CriticalResultsManage, "Record the receiver's read-back acknowledgement."),
            Define("POST", "/api/critical-results/{id}/escalate", RbacPermission.CriticalResultsManage, "Escalate an un-communicated critical result."),
            Define("POST", "/api/critical-results/{id}/close", RbacPermission.CriticalResultsManage, "Close out a critical result."),

            // PRD §14.13 (PR-001..010) — RADPEER-aligned peer review & quality.
            Define("GET", "/api/peer-reviews/mine", RbacPermission.PeerReviewRead, "List the signed-in reviewer's own peer-review assignments (author blinded until submitted)."),
            Define("GET", "/api/peer-reviews/report/{reportId}", RbacPermission.PeerReviewRead, "List the peer reviews recorded against one report."),
            Define("POST", "/api/peer-reviews", RbacPermission.PeerReviewManage, "Assign one signed report to a peer reviewer."),
            Define("POST", "/api/peer-reviews/sample", RbacPermission.PeerReviewManage, "Randomly sample N signed reports into the peer-review queue."),
            Define("GET", "/api/peer-reviews/stats", RbacPermission.PeerReviewManage, "Per-radiologist concordance rate and discrepancy breakdown over a date range."),
            Define("POST", "/api/peer-reviews/{id}/start", RbacPermission.PeerReviewSubmit, "Mark an assigned peer review as opened."),
            Define("POST", "/api/peer-reviews/{id}/submit", RbacPermission.PeerReviewSubmit, "Submit a RADPEER score with structured rationale."),
            Define("POST", "/api/peer-reviews/{id}/dispute", RbacPermission.PeerReviewSubmit, "Dispute a completed peer review of your own report."),

            // PRD §14.14 (TF-001..008) — teaching file & education module.
            Define("GET", "/api/teaching-cases", RbacPermission.TeachingCasesRead, "Search the de-identified teaching library (modality/body part/difficulty/tag/free text)."),
            Define("GET", "/api/teaching-cases/{id}", RbacPermission.TeachingCasesRead, "Read one teaching case and increment its view counter."),
            Define("POST", "/api/teaching-cases", RbacPermission.TeachingCasesManage, "Create a blank teaching case."),
            Define("POST", "/api/teaching-cases/from-report/{reportId}", RbacPermission.TeachingCasesManage, "Create a teaching case from a report, de-identifying its narrative (TF-001/TF-002)."),
            Define("PATCH", "/api/teaching-cases/{id}", RbacPermission.TeachingCasesManage, "Update a teaching case you authored (or any case, as an administrator)."),
            Define("POST", "/api/teaching-cases/{id}/publish", RbacPermission.TeachingCasesManage, "Publish a teaching case to the tenant library (TF-007)."),
            Define("POST", "/api/teaching-cases/{id}/unpublish", RbacPermission.TeachingCasesManage, "Withdraw a teaching case from the tenant library (TF-007)."),
            Define("DELETE", "/api/teaching-cases/{id}", RbacPermission.TeachingCasesManage, "Delete a teaching case you authored (or any case, as an administrator)."),
        };

    public static IReadOnlyCollection<EndpointPermissionDefinition> All => Entries;

    private static EndpointPermissionDefinition Define(
        string method,
        string routeTemplate,
        RbacPermission permission,
        string notes) =>
        new(method, routeTemplate, permission, PermissionCatalog.Get(permission).Key, notes);
}
