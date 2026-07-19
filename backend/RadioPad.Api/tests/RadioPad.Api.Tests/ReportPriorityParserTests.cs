using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// F8 — <c>Report.Priority</c> is documented as "RIS-driven" and drives worklist queue ordering, but
/// no ingest path ever wrote it. The JSON order webhook and both FHIR import paths constructed a
/// <c>Report</c> without it, so every report in every deployment carried the <c>Routine</c> default
/// and a STAT order arrived indistinguishable from routine work. The worklist was left inferring
/// urgency by pattern-matching the indication text — a fallback that only ever ran because the real
/// field was empty.
/// </summary>
public class ReportPriorityParserTests
{
    [Theory]
    [InlineData("stat")]
    [InlineData("STAT")]
    [InlineData(" Stat ")]
    [InlineData("S")] // HL7 ORC-7
    public void Stat_Vocabularies_Map_To_Stat(string input)
        => Assert.Equal(ReportPriority.Stat, ReportPriorityParser.Parse(input));

    [Theory]
    [InlineData("urgent")]
    [InlineData("asap")] // FHIR request-priority
    [InlineData("A")] // HL7 ASAP
    [InlineData("P")] // HL7 preop
    [InlineData("C")] // HL7 callback
    [InlineData("T")] // HL7 timing critical
    public void Elevated_Vocabularies_Map_To_Urgent(string input)
        => Assert.Equal(ReportPriority.Urgent, ReportPriorityParser.Parse(input));

    [Theory]
    [InlineData("routine")]
    [InlineData("R")]
    [InlineData("normal")]
    public void Routine_Vocabularies_Map_To_Routine(string input)
        => Assert.Equal(ReportPriority.Routine, ReportPriorityParser.Parse(input));

    /// <summary>
    /// An unreadable priority must never silently escalate a case — a queue where everything is
    /// STAT ranks nothing. Absent and unknown both mean "the sender said nothing", which is Routine.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wharrgarbl")]
    [InlineData("9")]
    public void Missing_Or_Unrecognised_Never_Escalates(string? input)
        => Assert.Equal(ReportPriority.Routine, ReportPriorityParser.Parse(input));
}
