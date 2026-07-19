using System.Text.RegularExpressions;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Translates MedASR's own output markup into plain report text.
///
/// <para>MedASR does not emit spoken punctuation as words (the way Parakeet/SAPI do) nor always as
/// real punctuation — when the speaker dictates "period" it emits a literal <c>{period}</c> marker,
/// and it segments the dictation with <c>[FINDINGS]</c>-style section tags. Verified against the
/// bundle's own test_wavs (see <c>MedAsrTranscriptNormalizerTests</c>). Left untranslated those
/// markers reach the report verbatim; worse, the frontend's spoken-punctuation pass rewrites the
/// word *inside* the braces, turning <c>{period}</c> into <c>{.}</c>. Normalising at the engine
/// boundary means every downstream consumer — the §5.2 pass-through, the formatter, the ROVER
/// ensemble token alignment, and the raw-transcript fallback — sees ordinary prose.</para>
///
/// <para><b>Deterministic and content-preserving.</b> This only rewrites the model's own markup into
/// the punctuation it denotes; it never adds, drops, or reorders a clinical word, so it is safe to
/// run before the §5.2 token lock. An UNRECOGNISED marker is deliberately left verbatim rather than
/// guessed at — a visible <c>{foo}</c> in the draft is a fail-visible anomaly the radiologist
/// catches, whereas a guess would be a silent fabrication.</para>
/// </summary>
public static class MedAsrTranscriptNormalizer
{
    /// <summary>
    /// Marker vocabulary → the punctuation it denotes. Keyed case-insensitively on the marker's
    /// inner text. Anything absent here is left verbatim (see the class remarks).
    /// </summary>
    private static readonly Dictionary<string, string> Markers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["period"] = ".",
        ["full stop"] = ".",
        ["comma"] = ",",
        ["colon"] = ":",
        ["semicolon"] = ";",
        ["semi colon"] = ";",
        ["question mark"] = "?",
        ["exclamation mark"] = "!",
        ["exclamation point"] = "!",
        ["hyphen"] = "-",
        ["dash"] = "-",
        ["slash"] = "/",
        ["new paragraph"] = "\n\n",
        ["paragraph"] = "\n\n",
        ["new line"] = "\n",
        ["newline"] = "\n",
    };

    private static readonly Regex MarkerRe = new(@"\{\s*([A-Za-z][A-Za-z ]{0,24})\s*\}", RegexOptions.Compiled);

    // Section tags are ALL-CAPS words in square brackets, e.g. [FINDINGS] / [EXAM TYPE]. Bounded
    // length so a bracketed citation or measurement range is never mistaken for a heading.
    private static readonly Regex SectionTagRe = new(@"\[\s*([A-Z][A-Z ]{1,28}[A-Z])\s*\]", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctRe = new(@"[ \t]+([.,;:?!])", RegexOptions.Compiled);
    // Spans newlines: the section-tag substitution inserts one, so "[FINDINGS] {colon}" lands as
    // "FINDINGS:\n :" and a same-line-only pattern would miss it.
    private static readonly Regex DuplicateColonRe = new(@":[ \t\r\n]*:", RegexOptions.Compiled);
    private static readonly Regex ManySpacesRe = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex ManyBlankLinesRe = new(@"(\r?\n){3,}", RegexOptions.Compiled);
    private static readonly Regex SpaceAroundNewlineRe = new(@"[ \t]*\r?\n[ \t]*", RegexOptions.Compiled);

    /// <summary>
    /// Rewrite <paramref name="raw"/> from MedASR markup into plain text. Returns the input
    /// unchanged when it carries no markers (MedASR emits ordinary prose for undictated
    /// punctuation), so this is safe and idempotent to apply to every result.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // 1) Punctuation markers → the punctuation they denote. Unknown markers survive verbatim.
        var text = MarkerRe.Replace(raw, m =>
            Markers.TryGetValue(m.Groups[1].Value.Trim(), out var punct) ? punct : m.Value);

        // 2) Section tags → a plain "HEADING:" line. We deliberately do NOT hand these to the
        //    formatter as authoritative structure: MedASR can emit a tag mid-sentence (observed:
        //    "The [DIAGNOSES] Includes ..."), so treating one as a hard section boundary would
        //    mangle the sentence. As a heading line the legitimate cases read correctly and a
        //    spurious one is obvious to the reviewing radiologist — and no words are lost either way.
        text = SectionTagRe.Replace(text, m => "\n" + m.Groups[1].Value.Trim() + ":\n");

        // 3) Tidy the seams the substitutions leave behind ("[FINDINGS] {colon}" → "FINDINGS::").
        text = DuplicateColonRe.Replace(text, ":");
        text = SpaceAroundNewlineRe.Replace(text, "\n");
        text = SpaceBeforePunctRe.Replace(text, "$1");
        text = ManySpacesRe.Replace(text, " ");
        text = ManyBlankLinesRe.Replace(text, "\n\n");

        return text.Trim();
    }
}
