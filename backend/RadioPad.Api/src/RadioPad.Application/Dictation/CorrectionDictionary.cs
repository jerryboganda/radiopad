using RadioPad.Domain.Entities;

namespace RadioPad.Application.Dictation;

/// <summary>
/// Brief §6 / F7 — resolves correction-dictionary rules applied deterministically BEFORE the LLM.
/// Phase 0 maps the org (tenant) lexicon; the per-user personal-override layer is layered on top in
/// a later increment (needs its own store). Only rows with a concrete replacement become rules — a
/// forbidden term with no replacement is a validation concern, not a deterministic correction.
/// </summary>
public static class CorrectionDictionary
{
    public static IReadOnlyList<CorrectionRule> FromLexicon(IEnumerable<TenantLexicon>? lexicon)
    {
        if (lexicon is null)
            return Array.Empty<CorrectionRule>();

        return lexicon
            .Where(l => !string.IsNullOrWhiteSpace(l.Term) && !string.IsNullOrWhiteSpace(l.Replacement))
            .OrderByDescending(l => l.Term.Trim().Length) // longer phrases first so they win
            .Select(l => new CorrectionRule(l.Term.Trim(), l.Replacement.Trim()))
            .ToList();
    }
}
