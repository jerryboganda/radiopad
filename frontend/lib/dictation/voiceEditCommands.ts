// F1 (dictation brief) — spoken editing commands. Between the transcript and insertion, a
// radiologist can say "scratch that" to undo the last dictation instead of having it typed out.
//
// SAFETY: a command is only recognised when it is the ENTIRE final utterance (anchored match on
// the trimmed chunk). This is deliberate — "delete the prior comparison" or "there is no scratch
// on the cortex" are dictation content, not commands, and must never trigger an edit. STT emits a
// short command like "scratch that" as its own final result, so full-chunk matching is safe and
// avoids false positives in prose. Pure + deterministic; unit-tested.

export type VoiceEditCommand =
  /** Undo the last dictation ("scratch that" / "strike that" / "undo that"). */
  | { kind: 'undo' };

// Each pattern anchors to the whole (trimmed) chunk, tolerating trailing punctuation the STT or
// the punctuation formatter may attach.
const PATTERNS: ReadonlyArray<{ re: RegExp; command: VoiceEditCommand }> = [
  { re: /^scratch(?:\s+that)?[.!]?$/i, command: { kind: 'undo' } },
  { re: /^strike\s+that[.!]?$/i, command: { kind: 'undo' } },
  { re: /^undo(?:\s+that)?[.!]?$/i, command: { kind: 'undo' } },
  { re: /^delete\s+that[.!]?$/i, command: { kind: 'undo' } },
];

/**
 * Return the editing command a final transcript chunk represents, or null if the chunk is
 * ordinary dictation to be inserted. Only whole-utterance commands match.
 */
export function parseVoiceEditCommand(text: string): VoiceEditCommand | null {
  const trimmed = text.trim();
  if (!trimmed) return null;
  for (const { re, command } of PATTERNS) {
    if (re.test(trimmed)) return command;
  }
  return null;
}
