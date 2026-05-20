using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public sealed class RolePermissionService : IPermissionService
{
    public bool HasPermission(User user, RbacPermission permission) =>
        RolePermissionMap.ForRole(user.Role).Contains(permission);

    public IReadOnlySet<RbacPermission> GetPermissions(User user) =>
        RolePermissionMap.ForRole(user.Role);

    public PermissionDecision Authorize(User user, RbacPermission permission)
    {
        var definition = PermissionCatalog.Get(permission);
        var allowed = HasPermission(user, permission);
        return new PermissionDecision(
            allowed,
            permission,
            definition.Key,
            user.Role,
            RolePermissionMap.RolesFor(permission));
    }
}
