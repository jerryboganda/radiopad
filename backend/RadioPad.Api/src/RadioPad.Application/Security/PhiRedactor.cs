using System.Text.RegularExpressions;

namespace RadioPad.Application.Security;

/// <summary>
/// Iter-31 SEC-010 — best-effort PHI scrubbing for log lines, exception
/// messages, and any free-text breadcrumb that may leak patient-identifying
/// strings. Patterns are intentionally permissive (false positives → masked
/// log line; never false negatives that leak PHI). Order matters: the
/// <c>Patient: Name</c> pattern runs before the digit/date scrubbers so the
/// trailing whitespace is not consumed early.
/// </summary>
public static class PhiRedactor
{
    private const string Replacement = "<redacted:phi>";

    private static readonly Regex PatientNamePattern = new(
        @"Patient\s*[:=]\s*[A-Z][A-Za-z\-']+(?:\s+[A-Z][A-Za-z\-']+)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IsoDateOfBirthPattern = new(
        @"\b\d{4}-\d{2}-\d{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex SlashDateOfBirthPattern = new(
        @"\b\d{2}/\d{2}/\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-?\d{2}-?\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex MrnPattern = new(
        @"\b\d{6,12}\b",
        RegexOptions.Compiled);

    /// <summary>Redact PHI-shaped substrings from a single string.</summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var s = PatientNamePattern.Replace(input, Replacement);
        s = IsoDateOfBirthPattern.Replace(s, Replacement);
        s = SlashDateOfBirthPattern.Replace(s, Replacement);
        s = SsnPattern.Replace(s, Replacement);
        s = MrnPattern.Replace(s, Replacement);
        return s;
    }
}
