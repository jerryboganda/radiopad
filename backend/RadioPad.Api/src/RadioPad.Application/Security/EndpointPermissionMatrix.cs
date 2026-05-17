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
        };

    public static IReadOnlyCollection<EndpointPermissionDefinition> All => Entries;

    private static EndpointPermissionDefinition Define(
        string method,
        string routeTemplate,
        RbacPermission permission,
        string notes) =>
        new(method, routeTemplate, permission, PermissionCatalog.Get(permission).Key, notes);
}
