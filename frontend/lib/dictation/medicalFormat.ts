// Dictation auto-formatting. Turns spoken punctuation words into real
// punctuation, normalises whitespace, and capitalises sentence starts so a
// radiologist can dictate prose hands-free. Pure + deterministic — every rule
// is unit-tested in `__tests__/dictation/medicalFormat.test.ts`.
//
// Scope is intentionally small (the high-value spoken tokens). Anything not
// recognised is passed through verbatim, so dictation never loses words.

const SPOKEN_PUNCTUATION: ReadonlyArray<readonly [RegExp, string]> = [
  [/\bnew paragraph\b/gi, '\n\n'],
  [/\b(?:new line|next line)\b/gi, '\n'],
  [/\b(?:full stop|period)\b/gi, '.'],
  [/\bcomma\b/gi, ','],
  [/\bsemicolon\b/gi, ';'],
  [/\bcolon\b/gi, ':'],
  [/\bquestion mark\b/gi, '?'],
  [/\b(?:exclamation mark|exclamation point)\b/gi, '!'],
  [/\b(?:open paren|open parenthesis)\b/gi, '('],
  [/\b(?:close paren|close parenthesis)\b/gi, ')'],
  [/\b(?:hyphen|dash)\b/gi, '-'],
];

/** Capitalise the first letter at the start, after sentence enders, and after newlines. */
function capitaliseSentences(text: string): string {
  return text.replace(/(^|[.!?]\s+|\n+)([a-z])/g, (_m, prefix: string, ch: string) => prefix + ch.toUpperCase());
}

export function formatDictation(input: string): string {
  let s = input;
  for (const [pattern, replacement] of SPOKEN_PUNCTUATION) {
    s = s.replace(pattern, replacement);
  }
  // Drop any whitespace before punctuation ("clear ." -> "clear.").
  s = s.replace(/\s+([.,;:?!)])/g, '$1');
  // Ensure a single space *after* punctuation when followed by a letter (never
  // between digits, so decimals like "2.5" survive).
  s = s.replace(/([.,;:?!])(?=[A-Za-z])/g, '$1 ');
  // Collapse repeated spaces/tabs but leave newlines intact.
  s = s.replace(/[ \t]{2,}/g, ' ');
  // Trim incidental spaces hugging a newline.
  s = s.replace(/[ \t]*\n[ \t]*/g, '\n');
  s = capitaliseSentences(s);
  return s.trim();
}
