using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public interface IPermissionService
{
    bool HasPermission(User user, RbacPermission permission);
    IReadOnlySet<RbacPermission> GetPermissions(User user);
    PermissionDecision Authorize(User user, RbacPermission permission);
}
