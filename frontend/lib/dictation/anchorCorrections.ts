// Re-anchor cross-check corrections (offsets computed server-side against the
// dictated transcript) onto a section's CURRENT text by locating originalText.
// This makes inline highlighting robust to formatting differences and edits the
// radiologist made after dictating. Pure + unit tested.

import type { CrossCheckCorrection } from '@/lib/api';

/**
 * Return the corrections that can be located in <paramref name="sectionText"/>,
 * with section-relative offsets. Substitutions only (insertions have no findable
 * anchor); matched in document order, non-overlapping where possible.
 */
export function anchorCorrections(
  sectionText: string,
  corrections: CrossCheckCorrection[],
): CrossCheckCorrection[] {
  const text = sectionText ?? '';
  const out: CrossCheckCorrection[] = [];
  let cursor = 0;
  for (const c of corrections) {
    if (!c.originalText) continue; // insertions can't be anchored for inline highlight
    let idx = text.indexOf(c.originalText, cursor);
    if (idx < 0) idx = text.indexOf(c.originalText); // fall back to anywhere
    if (idx < 0) continue;
    out.push({ ...c, startOffset: idx, endOffset: idx + c.originalText.length });
    cursor = idx + c.originalText.length;
  }
  return out;
}

/**
 * Apply an accepted correction to the section text, re-finding originalText at
 * apply time (robust to prior accepts). Returns the new text, or null if the
 * original can no longer be found.
 */
export function applyCorrection(sectionText: string, c: CrossCheckCorrection): string | null {
  const text = sectionText ?? '';
  if (!c.originalText) return null;
  const idx = text.indexOf(c.originalText);
  if (idx < 0) return null;
  return text.slice(0, idx) + c.correctedText + text.slice(idx + c.originalText.length);
}
