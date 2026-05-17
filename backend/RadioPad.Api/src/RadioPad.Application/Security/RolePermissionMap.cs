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
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
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
                RbacPermission.TemplatesManage,
                RbacPermission.TemplatesApprove,
                RbacPermission.ProvidersRead,
                RbacPermission.ProvidersManage,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.McpToolsManage,
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
                RbacPermission.TemplatesManage,
                RbacPermission.TemplatesApprove,
                RbacPermission.ProvidersRead,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                RbacPermission.UsersRead,
                RbacPermission.UsersManage,
                RbacPermission.UsersRevokeSessions,
                RbacPermission.BillingRead,
                RbacPermission.BillingManage,
                RbacPermission.SecurityManage,
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksManage,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.McpToolsManage),

            [UserRole.ComplianceReviewer] = Set(
                RbacPermission.ReportsRead,
                RbacPermission.RulebooksRead,
                RbacPermission.TemplatesRead,
                RbacPermission.AuditRead,
                RbacPermission.AuditVerify,
                RbacPermission.AuditExport,
                RbacPermission.UsersRead,
                RbacPermission.UsersRevokeSessions,
                RbacPermission.BillingRead,
                RbacPermission.SecurityManage,
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
                RbacPermission.TemplatesManage,
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
                RbacPermission.ValidationPacksRead,
                RbacPermission.ValidationPacksManage,
                RbacPermission.ValidationPacksRun,
                RbacPermission.McpToolsInvoke,
                RbacPermission.McpToolsManage),

            [UserRole.BillingAdmin] = Set(
                RbacPermission.ProvidersRead,
                RbacPermission.AuditRead,
                RbacPermission.UsersRead,
                RbacPermission.BillingRead,
                RbacPermission.BillingManage),
        };

    public static IReadOnlySet<RbacPermission> ForRole(UserRole role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : Empty;

    public static IReadOnlyCollection<UserRole> RolesFor(RbacPermission permission) =>
        Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToArray();

    private static readonly IReadOnlySet<RbacPermission> Empty = new HashSet<RbacPermission>();

    private static IReadOnlySet<RbacPermission> Set(params RbacPermission[] permissions) =>
        new HashSet<RbacPermission>(permissions);
}
