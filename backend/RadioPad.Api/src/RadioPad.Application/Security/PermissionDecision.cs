using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public sealed record PermissionDecision(
    bool Allowed,
    RbacPermission Permission,
    string PermissionKey,
    UserRole UserRole,
    IReadOnlyCollection<UserRole> CompatibleRoles);
