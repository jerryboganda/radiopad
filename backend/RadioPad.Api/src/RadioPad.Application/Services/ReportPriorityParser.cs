using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services;

/// <summary>
/// F8 — maps an inbound order's priority string onto <see cref="ReportPriority"/>.
///
/// <para><c>Report.Priority</c> is documented as "RIS-driven" and drives worklist queue ordering,
/// but no ingest path ever wrote it: the JSON order webhook, the FHIR ServiceRequest import and the
/// FHIR DiagnosticReport import all constructed a <c>Report</c> without it, so every report in every
/// deployment carried the <c>Routine</c> default. A STAT order placed upstream arrived
/// indistinguishable from routine work, and the worklist was left inferring urgency by pattern
/// matching the clinical indication text — a fallback that only ever ran because the real field was
/// empty.</para>
///
/// <para>Accepts the vocabularies the two channels actually speak: FHIR request-priority
/// (<c>routine | urgent | asap | stat</c>) and the HL7 v2 priority codes carried in ORC-7 / TQ1-9
/// (<c>S</c> stat, <c>A</c> ASAP, <c>P</c> preop, <c>C</c> callback, <c>T</c> timing critical,
/// <c>R</c> routine). Anything unrecognised — including null and empty — is
/// <see cref="ReportPriority.Routine"/>: an unreadable priority must never silently escalate a case,
/// and must never mask that the sender said nothing.</para>
/// </summary>
public static class ReportPriorityParser
{
    public static ReportPriority Parse(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return ReportPriority.Routine;

        return priority.Trim().ToLowerInvariant() switch
        {
            // Highest urgency: act now.
            "stat" or "s" => ReportPriority.Stat,
            // Elevated but not immediate. ASAP, preop, callback and timing-critical all mean the
            // study cannot simply join the back of the routine queue.
            "urgent" or "asap" or "a" or "preop" or "p" or "callback" or "c" or "timing" or "t"
                => ReportPriority.Urgent,
            "routine" or "r" or "normal" => ReportPriority.Routine,
            _ => ReportPriority.Routine,
        };
    }
}
