// Client-side resolution of the effective correction dictionary (§6 / F7).
//
// WHY THIS EXISTS. The report-scoped CLOUD draft endpoint resolves corrections server-side from the
// org lexicon + the user's personal corrections. The on-device draft endpoint is deliberately
// STATELESS — it never touches the database, so the transcript (PHI) stays on the workstation — and
// therefore cannot look them up. Corrections must be passed in by the caller.
//
// Without this, switching a draft to on-device formatting silently drops every correction the
// radiologist has configured: their "MRI" capitalisation, their org's mandated phrasing, all of it,
// with nothing on screen to say so. Quietly changing what a report says based on where it was
// formatted is precisely the kind of invisible difference this product cannot afford.
//
// This is a faithful port of `CorrectionDictionary.Resolve` (RadioPad.Application.Dictation) —
// the same precedent as `spokenNumbers.ts` porting the §5.2 number normalizer. The two must agree,
// so the rules are restated explicitly and tested:
//   1. Only entries with BOTH sides non-empty are used.
//   2. The org lexicon is layered UNDER the user's personal corrections — for the same source term
//      (compared case-INSENSITIVELY) the user's entry wins.
//   3. Longest source phrase first, so a long phrase is not pre-empted by a shorter rule.

export interface CorrectionRule {
  from: string;
  to: string;
}

/** Org lexicon row as returned by `GET /api/lexicon`. */
export interface LexiconEntry {
  term: string;
  replacement: string;
}

/** Personal correction as returned by `GET /api/user-corrections`. */
export interface UserCorrectionEntry {
  from: string;
  to: string;
}

const usable = (a: string | null | undefined, b: string | null | undefined): boolean =>
  !!a && !!b && a.trim().length > 0 && b.trim().length > 0;

/**
 * Resolve the effective rules for a user, mirroring the backend precedence exactly.
 *
 * Returns rules ordered longest-source-first, ready to hand to the on-device draft endpoint.
 */
export function resolveCorrections(
  orgLexicon: readonly LexiconEntry[] | null | undefined,
  userCorrections: readonly UserCorrectionEntry[] | null | undefined,
): CorrectionRule[] {
  // Keyed case-insensitively so a personal "mri -> MRI" overrides an org "MRI -> ..." the same way
  // the backend's OrdinalIgnoreCase dictionary does.
  const byFrom = new Map<string, CorrectionRule>();
  const put = (rule: CorrectionRule) => byFrom.set(rule.from.toLowerCase(), rule);

  for (const l of orgLexicon ?? []) {
    if (!usable(l?.term, l?.replacement)) continue;
    put({ from: l.term.trim(), to: l.replacement.trim() });
  }

  // Applied second: the user's own correction wins for the same source term.
  for (const u of userCorrections ?? []) {
    if (!usable(u?.from, u?.to)) continue;
    put({ from: u.from.trim(), to: u.to.trim() });
  }

  return [...byFrom.values()].sort((a, b) => b.from.length - a.from.length);
}
