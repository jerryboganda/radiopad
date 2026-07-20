'use client';

// PRD WL-004 — reader load and fatigue heuristics, and WL-005 — RVU /
// procedure-mix analytics. Both are INFORMATIONAL by PRD design: nothing here
// blocks a read, reassigns a case, or reports a radiologist to anyone. The
// output is a nudge the reader sees about their own session.
//
// The session log is device-local (localStorage). It records only report ids,
// modality/body-part labels and timestamps — never patient data.

export interface ReadEvent {
  /** Report id, so re-opening the same case is not double-counted. */
  reportId: string;
  modality: string;
  bodyPart: string;
  /** Epoch ms when the reader opened it. */
  at: number;
}

const KEY = 'radiopad:reader-load';
/** Sessions older than this are history, not "today's shift". */
const SESSION_WINDOW_MS = 16 * 60 * 60 * 1000;
/** A gap this long means the reader took a real break — the streak resets. */
export const BREAK_GAP_MS = 20 * 60 * 1000;
/** Continuous reading beyond this earns a break suggestion. */
export const FATIGUE_STREAK_MS = 90 * 60 * 1000;
/** …or this many cases without a break. */
export const FATIGUE_STREAK_CASES = 25;

function read(): ReadEvent[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (e): e is ReadEvent =>
        !!e &&
        typeof (e as ReadEvent).reportId === 'string' &&
        typeof (e as ReadEvent).at === 'number',
    );
  } catch {
    return [];
  }
}

function write(events: ReadEvent[]): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(KEY, JSON.stringify(events));
  } catch {
    /* storage unavailable — heuristics degrade to "no data", never to a crash */
  }
}

/** Events inside the current shift window, oldest first. */
export function getSessionReads(now: number = Date.now()): ReadEvent[] {
  return read()
    .filter((e) => now - e.at <= SESSION_WINDOW_MS)
    .sort((a, b) => a.at - b.at);
}

/** Record that the reader opened a case. Re-opening the same report is a no-op. */
export function recordRead(
  input: { reportId: string; modality: string; bodyPart: string },
  now: number = Date.now(),
): void {
  const events = getSessionReads(now);
  if (events.some((e) => e.reportId === input.reportId)) return;
  write([...events, { ...input, at: now }]);
}

export function clearReaderLoad(): void {
  write([]);
}

export interface FatigueState {
  casesThisSession: number;
  /** Cases read since the last break longer than {@link BREAK_GAP_MS}. */
  casesSinceBreak: number;
  /** Continuous reading time in ms since the last break. */
  streakMs: number;
  /** True when a break is worth suggesting. */
  breakSuggested: boolean;
  /** Plain-English reason, or null when nothing to say. */
  message: string | null;
}

/**
 * Fatigue heuristic over the session log. Deliberately conservative: it fires
 * on a long unbroken streak, never on total volume, because a busy day with
 * proper breaks is not the risk this is about.
 */
export function computeFatigue(now: number = Date.now(), events?: ReadEvent[]): FatigueState {
  const list = events ?? getSessionReads(now);
  if (list.length === 0) {
    return { casesThisSession: 0, casesSinceBreak: 0, streakMs: 0, breakSuggested: false, message: null };
  }

  // Walk back from the newest read until a gap longer than BREAK_GAP_MS.
  let streakStart = list[list.length - 1].at;
  let casesSinceBreak = 1;
  for (let i = list.length - 1; i > 0; i--) {
    if (list[i].at - list[i - 1].at > BREAK_GAP_MS) break;
    streakStart = list[i - 1].at;
    casesSinceBreak++;
  }

  const streakMs = Math.max(0, list[list.length - 1].at - streakStart);
  const byTime = streakMs >= FATIGUE_STREAK_MS;
  const byCount = casesSinceBreak >= FATIGUE_STREAK_CASES;
  const minutes = Math.round(streakMs / 60_000);

  let message: string | null = null;
  if (byTime && byCount) {
    message = `You've read ${casesSinceBreak} cases over ${minutes} minutes without a break.`;
  } else if (byTime) {
    message = `You've been reading for ${minutes} minutes without a break.`;
  } else if (byCount) {
    message = `You've read ${casesSinceBreak} cases without a break.`;
  }

  return {
    casesThisSession: list.length,
    casesSinceBreak,
    streakMs,
    breakSuggested: byTime || byCount,
    message,
  };
}

// ── WL-005 — RVU / procedure mix ────────────────────────────────────────────

/**
 * Work-RVU reference by modality. These are ROUNDED PLANNING FIGURES for a
 * volume/mix view, not billing values: real wRVUs are per-CPT and vary by
 * contrast, laterality and year. The UI labels the output as an estimate for
 * exactly this reason — never feed it to a claim.
 */
export const MODALITY_WRVU: Record<string, number> = {
  CT: 1.2,
  MR: 1.8,
  MRI: 1.8,
  US: 0.7,
  XR: 0.25,
  CR: 0.25,
  DX: 0.25,
  MG: 0.7,
  NM: 1.0,
  PT: 1.9,
  PET: 1.9,
  FL: 0.6,
  DEFAULT: 0.6,
};

export function wRvuFor(modality: string): number {
  const key = (modality || '').trim().toUpperCase();
  return MODALITY_WRVU[key] ?? MODALITY_WRVU.DEFAULT;
}

export interface ProcedureMixRow {
  modality: string;
  count: number;
  estimatedWRvu: number;
  /** Share of the session's cases, 0..1. */
  share: number;
}

export interface ProcedureMix {
  rows: ProcedureMixRow[];
  totalCases: number;
  totalWRvu: number;
}

/** Procedure mix + estimated wRVU for a set of reads (defaults to this session). */
export function computeProcedureMix(events?: ReadEvent[], now: number = Date.now()): ProcedureMix {
  const list = events ?? getSessionReads(now);
  const byModality = new Map<string, number>();
  for (const e of list) {
    const key = (e.modality || 'Unspecified').trim().toUpperCase();
    byModality.set(key, (byModality.get(key) ?? 0) + 1);
  }
  const totalCases = list.length;
  const rows: ProcedureMixRow[] = [...byModality.entries()]
    .map(([modality, count]) => ({
      modality,
      count,
      estimatedWRvu: Math.round(count * wRvuFor(modality) * 100) / 100,
      share: totalCases === 0 ? 0 : count / totalCases,
    }))
    .sort((a, b) => b.count - a.count);
  const totalWRvu = Math.round(rows.reduce((sum, r) => sum + r.estimatedWRvu, 0) * 100) / 100;
  return { rows, totalCases, totalWRvu };
}
