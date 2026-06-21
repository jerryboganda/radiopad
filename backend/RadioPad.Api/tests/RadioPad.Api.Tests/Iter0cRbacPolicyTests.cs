using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Iter-0c (PRD AUTH-002 / §0.6) — the 5 added RBAC roles carry sane,
/// least-privilege permission sets, and the TenantSettings policy scaffold has
/// safe (non-enforcing) defaults.
/// </summary>
public class Iter0cRbacPolicyTests
{
    [Theory]
    [InlineData(UserRole.Resident)]
    [InlineData(UserRole.Fellow)]
    [InlineData(UserRole.Subspecialist)]
    [InlineData(UserRole.Researcher)]
    [InlineData(UserRole.Auditor)]
    public void New_Roles_Have_A_Permission_Set(UserRole role)
    {
        Assert.NotEmpty(RolePermissionMap.ForRole(role));
    }

    [Fact]
    public void Resident_Can_Draft_But_Never_Sign_Or_Export()
    {
        var p = RolePermissionMap.ForRole(UserRole.Resident);
        Assert.Contains(RbacPermission.ReportsDraft, p);
        Assert.Contains(RbacPermission.ReportsEdit, p);
        Assert.Contains(RbacPermission.ReportsValidate, p);
        Assert.DoesNotContain(RbacPermission.ReportsSign, p);
        Assert.DoesNotContain(RbacPermission.ReportsExport, p);
    }

    [Fact]
    public void Fellow_Can_Export_But_Not_Sign()
    {
        var p = RolePermissionMap.ForRole(UserRole.Fellow);
        Assert.Contains(RbacPermission.ReportsExport, p);
        Assert.DoesNotContain(RbacPermission.ReportsSign, p);
    }

    [Fact]
    public void Subspecialist_Has_Full_Attending_Authority_Including_Sign()
    {
        var sub = RolePermissionMap.ForRole(UserRole.Subspecialist);
        var rad = RolePermissionMap.ForRole(UserRole.Radiologist);
        Assert.Contains(RbacPermission.ReportsSign, sub);
        // Subspecialist mirrors a general radiologist's clinical authority.
        Assert.Contains(RbacPermission.ReportsSign, rad);
        Assert.Contains(RbacPermission.ReportsExport, sub);
    }

    [Fact]
    public void Researcher_Is_Read_Only()
    {
        var p = RolePermissionMap.ForRole(UserRole.Researcher);
        Assert.Contains(RbacPermission.ReportsRead, p);
        Assert.DoesNotContain(RbacPermission.ReportsDraft, p);
        Assert.DoesNotContain(RbacPermission.ReportsEdit, p);
        Assert.DoesNotContain(RbacPermission.ReportsSign, p);
    }

    [Fact]
    public void Auditor_Can_Verify_And_Export_Audit_But_Not_Mutate()
    {
        var p = RolePermissionMap.ForRole(UserRole.Auditor);
        Assert.Contains(RbacPermission.AuditRead, p);
        Assert.Contains(RbacPermission.AuditVerify, p);
        Assert.Contains(RbacPermission.AuditExport, p);
        Assert.DoesNotContain(RbacPermission.ReportsEdit, p);
        Assert.DoesNotContain(RbacPermission.UsersManage, p);
    }

    [Fact]
    public void TenantSettings_Policy_Scaffold_Defaults_Are_Non_Enforcing()
    {
        var s = new TenantSettings();
        Assert.False(s.RequireMfa);
        Assert.Equal(0, s.IdleTimeoutMinutes);
        Assert.Equal(0, s.MaxConcurrentSessions);
        Assert.Equal("{}", s.RetentionByClassJson);
        Assert.Equal("[]", s.CriticalityClassesJson);
    }
}
