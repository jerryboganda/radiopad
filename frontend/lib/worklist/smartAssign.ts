'use client';

// PRD WL-003 — "smart assign" suggestion. INFORMATIONAL ONLY, by requirement:
// this ranks the queue and points at one case. It never assigns work, never
// locks a case to a reader, and never hides anything — the radiologist picks
// whatever they want and the suggestion is a shortcut, not a gate.
//
// Ranking inputs, in the order they matter:
//   1. Clinical priority — STAT before Urgent before Routine, always.
//   2. Wait time — within a priority band, the case that has waited longest.
//   3. Subspecialty familiarity — a modality/body-part pairing this reader has
//      been reading today, so they stay in one head-space rather than
//      thrashing between CT chest and MSK MR.
//   4. Fatigue — when a break is due, a long/complex study is a worse next
//      pick than a quick one, so heavy modalities are nudged down.
//
// Pure functions over data the caller already has; no network, no storage.

import { computeFatigue, wRvuFor, type ReadEvent } from '@/lib/worklist/readerLoad';

export interface AssignCandidate {
  reportId: string;
  priority: 'STAT' | 'Urgent' | 'Routine';
  modality: string;
  bodyPart: string;
  /** Epoch ms the case has been waiting since (last activity). */
  waitingSince: number;
}

export interface AssignSuggestion {
  reportId: string;
  score: number;
  /** Plain-English "why this one" for the UI — never a bare number. */
  reasons: string[];
}

const PRIORITY_WEIGHT: Record<AssignCandidate['priority'], number> = {
  STAT: 10_000,
  Urgent: 5_000,
  Routine: 0,
};

/** Familiarity: how much of the reader's recent work matches this pairing. */
function familiarity(candidate: AssignCandidate, history: ReadEvent[]): number {
  if (history.length === 0) return 0;
  const mod = candidate.modality.trim().toUpperCase();
  const part = candidate.bodyPart.trim().toUpperCase();
  let hits = 0;
  for (const e of history) {
    const m = (e.modality || '').trim().toUpperCase() === mod;
    const b = (e.bodyPart || '').trim().toUpperCase() === part;
    if (m && b) hits += 1;
    else if (m || b) hits += 0.4;
  }
  return hits / history.length; // 0..1
}

/**
 * Rank candidates, best first. `now` and `history` are injectable so the
 * ranking is deterministic under test.
 */
export function rankCandidates(
  candidates: AssignCandidate[],
  opts: { now?: number; history?: ReadEvent[] } = {},
): AssignSuggestion[] {
  const now = opts.now ?? Date.now();
  const history = opts.history ?? [];
  const fatigue = computeFatigue(now, history);

  return candidates
    .map((c) => {
      const reasons: string[] = [];
      let score = PRIORITY_WEIGHT[c.priority];
      if (c.priority !== 'Routine') reasons.push(`${c.priority} priority`);

      // Wait time: one point per minute, capped so a stale routine case can
      // never outrank a fresh STAT.
      const waitedMin = Math.max(0, (now - c.waitingSince) / 60_000);
      const waitScore = Math.min(waitedMin, 4_000);
      score += waitScore;
      if (waitedMin >= 60) reasons.push(`waiting ${Math.round(waitedMin / 60)}h`);
      else if (waitedMin >= 10) reasons.push(`waiting ${Math.round(waitedMin)}m`);

      const fam = familiarity(c, history);
      if (fam > 0.2) {
        score += fam * 300;
        reasons.push('matches what you have been reading');
      }

      // When a break is due, prefer a lighter study as the next read.
      if (fatigue.breakSuggested) {
        const weight = wRvuFor(c.modality);
        score -= weight * 100;
        if (weight <= 0.4) reasons.push('shorter study');
      }

      return { reportId: c.reportId, score: Math.round(score), reasons };
    })
    .sort((a, b) => b.score - a.score);
}

/** The single suggested next read, or null when the queue is empty. */
export function suggestNext(
  candidates: AssignCandidate[],
  opts: { now?: number; history?: ReadEvent[] } = {},
): AssignSuggestion | null {
  return rankCandidates(candidates, opts)[0] ?? null;
}
