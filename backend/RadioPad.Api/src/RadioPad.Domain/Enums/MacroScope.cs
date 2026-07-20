namespace RadioPad.Domain.Enums;

/// <summary>
/// PRD RPT-021 — reach of a shared autotext macro. Personal (device-local)
/// snippets are not represented here: they never round-trip to the server.
/// </summary>
public enum MacroScope
{
    /// <summary>Available to every user in the tenant.</summary>
    Tenant = 0,

    /// <summary>Available to users working in a named subspecialty.</summary>
    Subspecialty = 1,
}
