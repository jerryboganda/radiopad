/**
 * PRD Beta #5 — Voice Dictation Command Mode.
 *
 * Detects command phrases in speech-recognition transcripts and maps them
 * to actions. When a radiologist says "generate impression" (or a natural
 * variant) during dictation, the phrase is stripped from the transcript and
 * the corresponding action is triggered instead.
 *
 * Two command families:
 *  - AI actions (generate impression, rewrite modes, validate, cleanup) —
 *    routed to the backend.
 *  - PowerScribe/Dragon-style navigation & editing ("next field",
 *    "go to impression", "new paragraph", "scratch that", "open sign and
 *    send") — executed locally against the section-editor registry. Signing
 *    is never automated: the sign command only OPENS the sign panel.
 */

export type VoiceCommand =
  | 'generate_impression'
  | 'make_concise'
  | 'make_formal'
  | 'patient_friendly'
  | 'validate_report'
  | 'cleanup_dictation'
  // Navigation / editing (local, no backend round-trip)
  | 'next_field'
  | 'previous_field'
  | 'go_to_section'
  | 'new_line'
  | 'new_paragraph'
  | 'undo_that'
  | 'open_sign';

export interface CommandMatch {
  command: VoiceCommand;
  matchedPhrase: string;
  /** Target section key for `go_to_section` (findings, impression, …). */
  sectionKey?: string;
}

/** Spoken section names → report section keys (SECTIONS in the editor). */
const SPOKEN_SECTION_MAP: Record<string, string> = {
  findings: 'findings',
  finding: 'findings',
  impression: 'impression',
  impressions: 'impression',
  technique: 'technique',
  comparison: 'comparison',
  indication: 'indication',
  history: 'indication', // "clinical history" lives in the indication section
  recommendation: 'recommendations',
  recommendations: 'recommendations',
};

const SECTION_ALTERNATION = Object.keys(SPOKEN_SECTION_MAP).join('|');

const COMMAND_PATTERNS: Array<{ pattern: RegExp; command: VoiceCommand }> = [
  { pattern: /\b(?:generate|create)\s+impression\b/i, command: 'generate_impression' },
  { pattern: /\b(?:make|rewrite)\s+(?:it\s+)?concise\b/i, command: 'make_concise' },
  { pattern: /\b(?:make|rewrite)\s+(?:it\s+)?formal\b/i, command: 'make_formal' },
  { pattern: /\bpatient[\s-]?friendly\b/i, command: 'patient_friendly' },
  { pattern: /\bvalidate\s+(?:the\s+)?report\b/i, command: 'validate_report' },
  { pattern: /\bclean\s*up\s+(?:the\s+)?dictation\b/i, command: 'cleanup_dictation' },
  // ---- navigation & editing --------------------------------------------
  { pattern: /\bnext\s+(?:field|section)\b/i, command: 'next_field' },
  { pattern: /\b(?:previous|prior|last)\s+(?:field|section)\b/i, command: 'previous_field' },
  {
    pattern: new RegExp(`\\b(?:go|jump)\\s+to\\s+(?:the\\s+)?(${SECTION_ALTERNATION})\\b`, 'i'),
    command: 'go_to_section',
  },
  { pattern: /\bnew\s+paragraph\b/i, command: 'new_paragraph' },
  { pattern: /\bnew\s+line\b/i, command: 'new_line' },
  { pattern: /\b(?:scratch|undo)\s+that\b/i, command: 'undo_that' },
  { pattern: /\b(?:open\s+)?sign\s+and\s+send\b/i, command: 'open_sign' },
  { pattern: /\bsign\s+(?:the\s+)?report\b/i, command: 'open_sign' },
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
      const match: CommandMatch = { command, matchedPhrase: m[0] };
      if (command === 'go_to_section' && m[1]) {
        match.sectionKey = SPOKEN_SECTION_MAP[m[1].toLowerCase()];
      }
      return match;
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
