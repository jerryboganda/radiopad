// Smart-spacing rule for inserting dictated text, shared by the plain-textarea
// path (insertText.ts) and the rich SectionEditor. Kept pure so it is unit
// tested without mounting an editor.
//
// Rule: when appending mid-prose, add a single separating space — but not if
// the text before the caret already ends in whitespace, or the inserted text
// begins with whitespace or closing punctuation.

/**
 * Returns `text` with a leading space prepended when smart spacing applies.
 * @param before the text immediately before the caret (may be the whole prefix)
 * @param text the text to insert
 */
export function withSmartSpacing(before: string, text: string): string {
  if (!text) return text;
  if (before.length > 0 && !/\s$/.test(before) && !/^[\s.,;:?!)]/.test(text)) {
    return ` ${text}`;
  }
  return text;
}
