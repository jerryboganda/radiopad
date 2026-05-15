/**
 * PRD Beta #5 — Voice Dictation Command Mode.
 *
 * Detects command phrases in speech-recognition transcripts and maps them
 * to AI actions.  When a radiologist says "generate impression" (or a
 * natural variant) during dictation, the phrase is stripped from the
 * transcript and the corresponding backend action is triggered instead.
 */

export type VoiceCommand =
  | 'generate_impression'
  | 'make_concise'
  | 'make_formal'
  | 'patient_friendly'
  | 'validate_report'
  | 'cleanup_dictation';

export interface CommandMatch {
  command: VoiceCommand;
  matchedPhrase: string;
}

const COMMAND_PATTERNS: Array<{ pattern: RegExp; command: VoiceCommand }> = [
  { pattern: /\b(?:generate|create)\s+impression\b/i, command: 'generate_impression' },
  { pattern: /\b(?:make|rewrite)\s+(?:it\s+)?concise\b/i, command: 'make_concise' },
  { pattern: /\b(?:make|rewrite)\s+(?:it\s+)?formal\b/i, command: 'make_formal' },
  { pattern: /\bpatient[\s-]?friendly\b/i, command: 'patient_friendly' },
  { pattern: /\bvalidate\s+(?:the\s+)?report\b/i, command: 'validate_report' },
  { pattern: /\bclean\s*up\s+(?:the\s+)?dictation\b/i, command: 'cleanup_dictation' },
];

/**
 * Extract the last sentence from a transcript (delimited by `.`, `!`, `?`,
 * or beginning of string) and check whether it matches a known command.
 */
export function detectCommand(transcript: string): CommandMatch | null {
  const trimmed = transcript.trim();
  if (!trimmed) return null;

  // Grab the last sentence — everything after the final sentence-ending
  // punctuation, or the entire string if there is none.
  const lastSentence = trimmed.replace(/^.*[.!?]\s*/s, '');

  for (const { pattern, command } of COMMAND_PATTERNS) {
    const m = lastSentence.match(pattern);
    if (m) {
      return { command, matchedPhrase: m[0] };
    }
  }
  return null;
}

/**
 * Remove the matched command phrase from the transcript (first occurrence
 * from the end) and clean up surrounding whitespace / trailing punctuation.
 */
export function stripCommand(transcript: string, match: CommandMatch): string {
  const idx = transcript.lastIndexOf(match.matchedPhrase);
  if (idx === -1) return transcript;
  const before = transcript.slice(0, idx);
  const after = transcript.slice(idx + match.matchedPhrase.length);
  return (before + after).replace(/\s{2,}/g, ' ').trim();
}
