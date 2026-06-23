using System.Text.RegularExpressions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests.Security;

public class PermissionCatalogTests
{
    [Fact]
    public void UserRoleOrdinals_RemainStable_ForCompatibility()
    {
        Assert.Equal(0, (int)UserRole.Radiologist);
        Assert.Equal(1, (int)UserRole.ReportingAdmin);
        Assert.Equal(2, (int)UserRole.MedicalDirector);
        Assert.Equal(3, (int)UserRole.ComplianceReviewer);
        Assert.Equal(4, (int)UserRole.ItAdmin);
        Assert.Equal(5, (int)UserRole.BillingAdmin);
    }

    [Fact]
    public void Catalog_DefinesEveryPermission_WithStableUniqueKeys()
    {
        var definitions = PermissionCatalog.All;
        var byPermission = definitions.Select(d => d.Permission).ToHashSet();
        var byKey = definitions.Select(d => d.Key).ToArray();

        foreach (var permission in Enum.GetValues<RbacPermission>())
            Assert.Contains(permission, byPermission);

        Assert.Equal(byKey.Length, byKey.Distinct(StringComparer.Ordinal).Count());
        Assert.All(byKey, key => Assert.Matches(new Regex("^[a-z]+(?:_[a-z]+)*(?:\\.[a-z]+(?:_[a-z]+)*)+$"), key));
    }

    [Fact]
    public void RoleMappings_DefineEveryCurrentRole()
    {
        foreach (var role in Enum.GetValues<UserRole>())
            Assert.NotEmpty(RolePermissionMap.ForRole(role));
    }

    [Fact]
    public void RoleMappings_PreserveRulebookApprovalSemantics()
    {
        AssertAllowed(RbacPermission.RulebooksApprove, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        AssertDenied(RbacPermission.RulebooksApprove, UserRole.Radiologist, UserRole.ComplianceReviewer, UserRole.BillingAdmin);
    }

    [Fact]
    public void RoleMappings_PreserveBillingManagementSemantics()
    {
        AssertAllowed(RbacPermission.BillingManage, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        AssertDenied(RbacPermission.BillingManage, UserRole.Radiologist, UserRole.ReportingAdmin, UserRole.ComplianceReviewer);
    }

    [Fact]
    public void RoleMappings_PreserveReportSigningSemantics()
    {
        AssertAllowed(RbacPermission.ReportsSign, UserRole.Radiologist, UserRole.MedicalDirector);
        AssertDenied(RbacPermission.ReportsSign, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.ItAdmin, UserRole.BillingAdmin);
    }

    [Fact]
    public void RoleMappings_PreserveSecurityAndAuditSemantics()
    {
        // 2026-06-23 least-privilege: SecurityManage is ItAdmin/MedicalDirector only
        // (ComplianceReviewer reviews + audits, does not own security infra).
        AssertAllowed(RbacPermission.SecurityManage, UserRole.ItAdmin, UserRole.MedicalDirector);
        AssertDenied(RbacPermission.SecurityManage, UserRole.Radiologist, UserRole.ReportingAdmin, UserRole.BillingAdmin, UserRole.ComplianceReviewer);

        AssertAllowed(RbacPermission.TenantSettingsManage, UserRole.ReportingAdmin, UserRole.MedicalDirector, UserRole.ItAdmin);
        AssertDenied(RbacPermission.TenantSettingsManage, UserRole.Radiologist, UserRole.ComplianceReviewer, UserRole.BillingAdmin);

        AssertAllowed(RbacPermission.AuditVerify, UserRole.ItAdmin, UserRole.MedicalDirector, UserRole.ComplianceReviewer);
        AssertDenied(RbacPermission.AuditVerify, UserRole.Radiologist, UserRole.ReportingAdmin, UserRole.BillingAdmin);
    }

    [Fact]
    public void RoleMappings_PreservePromptOverrideSeparationOfDuties()
    {
        // 2026-06-23 least-privilege: PromptOverridesManage moved off ReportingAdmin to
        // ItAdmin (keeps the manage/approve SoD: ItAdmin manages, MedicalDirector approves).
        AssertAllowed(RbacPermission.PromptOverridesManage, UserRole.ItAdmin, UserRole.MedicalDirector);
        AssertDenied(RbacPermission.PromptOverridesManage, UserRole.Radiologist, UserRole.ComplianceReviewer, UserRole.ReportingAdmin, UserRole.BillingAdmin);

        AssertAllowed(RbacPermission.PromptOverridesApprove, UserRole.MedicalDirector);
        AssertDenied(RbacPermission.PromptOverridesApprove, UserRole.Radiologist, UserRole.ReportingAdmin, UserRole.ComplianceReviewer, UserRole.ItAdmin, UserRole.BillingAdmin);
    }

    [Fact]
    public void EndpointPermissionMatrix_IsCanonicalAndUsesCatalogKeys()
    {
        var entries = EndpointPermissionMatrix.All;
        Assert.NotEmpty(entries);
        Assert.Equal(
            entries.Count,
            entries.Select(e => $"{e.Method} {e.RouteTemplate}").Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var entry in entries)
        {
            Assert.Equal(entry.Method.ToUpperInvariant(), entry.Method);
            Assert.Equal(PermissionCatalog.Get(entry.Permission).Key, entry.PermissionKey);
            Assert.False(string.IsNullOrWhiteSpace(entry.Notes));
        }
    }

    [Fact]
    public void PermissionService_ReturnsDefaultDenyDecisionMetadata()
    {
        var service = new RolePermissionService();
        var user = new User { Role = UserRole.Radiologist };

        var decision = service.Authorize(user, RbacPermission.ProvidersManage);

        Assert.False(decision.Allowed);
        Assert.Equal("providers.manage", decision.PermissionKey);
        Assert.Equal(UserRole.Radiologist, decision.UserRole);
        Assert.Contains(UserRole.ItAdmin, decision.CompatibleRoles);
        // ProvidersManage is ItAdmin/MedicalDirector only (least-privilege 2026-06-23).
        Assert.Contains(UserRole.MedicalDirector, decision.CompatibleRoles);
    }

    private static void AssertAllowed(RbacPermission permission, params UserRole[] roles)
    {
        foreach (var role in roles)
            Assert.Contains(permission, RolePermissionMap.ForRole(role));
    }

    private static void AssertDenied(RbacPermission permission, params UserRole[] roles)
    {
        foreach (var role in roles)
            Assert.DoesNotContain(permission, RolePermissionMap.ForRole(role));
    }
}
