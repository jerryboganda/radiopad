using RadioPad.Domain.Enums;

namespace RadioPad.Application.Security;

public sealed record PermissionDefinition(
    RbacPermission Permission,
    string Key,
    string Description,
    bool ClinicalSafetySensitive = false);
