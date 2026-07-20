using RadioPad.Domain.Enums;

namespace RadioPad.Domain.Entities;

/// <summary>
/// PRD RPT-021 — a shared autotext macro (trigger + body with <c>${field}</c>
/// fill-in placeholders), owned by the tenant rather than by one workstation.
///
/// RadioPad already ships device-local per-user snippets (frontend
/// <c>lib/snippets.ts</c>); the PRD additionally requires tenant- and
/// subspecialty-scoped macros so a department can publish an agreed normal
/// template once. Personal snippets always win on a trigger collision — a
/// radiologist can override a departmental macro without an admin.
///
/// Macro bodies are clinical boilerplate authored by staff and must never
/// contain PHI; they are tenant-scoped like every other authored artifact.
/// </summary>
public class SharedMacro : Entity
{
    public Guid TenantId { get; set; }

    /// <summary>Who published it (audit/provenance; not an access control).</summary>
    public Guid CreatedByUserId { get; set; }

    public MacroScope Scope { get; set; } = MacroScope.Tenant;

    /// <summary>
    /// Subspecialty this macro applies to when <see cref="Scope"/> is
    /// <see cref="MacroScope.Subspecialty"/> (e.g. "Neuro", "MSK"). Empty for
    /// tenant-wide macros. Matched case-insensitively against the template
    /// subspecialty vocabulary already used by <c>ReportTemplate</c>.
    /// </summary>
    public string Subspecialty { get; set; } = "";

    /// <summary>Short abbreviation the radiologist types, e.g. "nlchest".</summary>
    public string Trigger { get; set; } = "";

    /// <summary>Expansion body; may contain <c>${field}</c> placeholders.</summary>
    public string Body { get; set; } = "";

    /// <summary>Optional one-line note shown in the macro picker.</summary>
    public string Description { get; set; } = "";
}
