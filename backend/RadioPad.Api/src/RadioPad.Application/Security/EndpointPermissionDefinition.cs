using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public sealed record EndpointPermissionDefinition(
    string Method,
    string RouteTemplate,
    RbacPermission Permission,
    string PermissionKey,
    string Notes);
