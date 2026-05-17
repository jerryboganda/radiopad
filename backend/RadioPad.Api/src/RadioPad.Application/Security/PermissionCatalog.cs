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
            [RbacPermission.ValidationPacksRead] = Define(RbacPermission.ValidationPacksRead, "validation_packs.read", "Read clinical validation packs."),
            [RbacPermission.ValidationPacksManage] = Define(RbacPermission.ValidationPacksManage, "validation_packs.manage", "Create and approve validation packs.", clinical: true),
            [RbacPermission.ValidationPacksRun] = Define(RbacPermission.ValidationPacksRun, "validation_packs.run", "Run validation packs.", clinical: true),
            [RbacPermission.McpToolsInvoke] = Define(RbacPermission.McpToolsInvoke, "mcp_tools.invoke", "Invoke approved MCP tools."),
            [RbacPermission.McpToolsManage] = Define(RbacPermission.McpToolsManage, "mcp_tools.manage", "Register, approve, test, or revoke MCP tools."),
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
