// PRD WL-003/004/005 — the reader-load, fatigue, procedure-mix and
// smart-assign heuristics. All informational: they must never reorder clinical
// priority, and a STAT case must outrank every comfort factor.
import { describe, it, expect, beforeEach } from 'vitest';
import {
  computeFatigue,
  computeProcedureMix,
  recordRead,
  getSessionReads,
  clearReaderLoad,
  BREAK_GAP_MS,
  type ReadEvent,
} from '@/lib/worklist/readerLoad';
import { rankCandidates, suggestNext, type AssignCandidate } from '@/lib/worklist/smartAssign';

const MIN = 60_000;
const T0 = 1_700_000_000_000;

function reads(spec: Array<[offsetMin: number, modality: string, bodyPart?: string]>): ReadEvent[] {
  return spec.map(([m, modality, bodyPart], i) => ({
    reportId: `r${i}`,
    modality,
    bodyPart: bodyPart ?? 'Chest',
    at: T0 + m * MIN,
  }));
}

describe('reader load (WL-004)', () => {
  beforeEach(() => clearReaderLoad());

  it('records a read once per report', () => {
    recordRead({ reportId: 'a', modality: 'CT', bodyPart: 'Chest' }, T0);
    recordRead({ reportId: 'a', modality: 'CT', bodyPart: 'Chest' }, T0 + MIN);
    recordRead({ reportId: 'b', modality: 'MR', bodyPart: 'Brain' }, T0 + 2 * MIN);
    expect(getSessionReads(T0 + 3 * MIN).map((e) => e.reportId)).toEqual(['a', 'b']);
  });

  it('says nothing when the session is empty or short', () => {
    expect(computeFatigue(T0).breakSuggested).toBe(false);
    const short = reads([[0, 'CT'], [5, 'CT'], [10, 'CT']]);
    expect(computeFatigue(T0 + 10 * MIN, short).breakSuggested).toBe(false);
  });

  it('suggests a break after a long unbroken streak', () => {
    // 20 reads, 5 minutes apart → 95 minutes of continuous reading.
    const long = reads(Array.from({ length: 20 }, (_, i) => [i * 5, 'CT'] as [number, string]));
    const f = computeFatigue(T0 + 95 * MIN, long);
    expect(f.breakSuggested).toBe(true);
    expect(f.streakMs).toBeGreaterThanOrEqual(90 * MIN);
    expect(f.message).toContain('without a break');
  });

  it('a real break resets the streak', () => {
    const before = Array.from({ length: 20 }, (_, i) => [i * 5, 'CT'] as [number, string]);
    // …then a gap longer than BREAK_GAP_MS, then two more reads.
    const afterStart = 95 + BREAK_GAP_MS / MIN + 5;
    const list = reads([...before, [afterStart, 'CT'], [afterStart + 3, 'CT']]);
    const f = computeFatigue(T0 + (afterStart + 3) * MIN, list);
    expect(f.casesSinceBreak).toBe(2);
    expect(f.breakSuggested).toBe(false);
    expect(f.casesThisSession).toBe(22);
  });
});

describe('procedure mix (WL-005)', () => {
  it('counts by modality with shares and an estimated wRVU total', () => {
    const mix = computeProcedureMix(reads([[0, 'CT'], [5, 'CT'], [10, 'XR'], [15, 'MR']]), T0 + 20 * MIN);
    expect(mix.totalCases).toBe(4);
    expect(mix.rows[0]).toMatchObject({ modality: 'CT', count: 2 });
    expect(mix.rows[0].share).toBeCloseTo(0.5);
    // 2×1.2 + 1×0.25 + 1×1.8 = 4.45
    expect(mix.totalWRvu).toBeCloseTo(4.45, 2);
  });

  it('is empty for an empty session', () => {
    expect(computeProcedureMix([], T0)).toMatchObject({ totalCases: 0, totalWRvu: 0, rows: [] });
  });
});

describe('smart assign (WL-003)', () => {
  const now = T0 + 200 * MIN;

  function candidate(id: string, priority: AssignCandidate['priority'], waitedMin: number, modality = 'CT'): AssignCandidate {
    return { reportId: id, priority, modality, bodyPart: 'Chest', waitingSince: now - waitedMin * MIN };
  }

  it('always puts clinical priority first, however long a routine case has waited', () => {
    const ranked = rankCandidates(
      [candidate('routine-ancient', 'Routine', 5000), candidate('stat-fresh', 'STAT', 1)],
      { now },
    );
    expect(ranked[0].reportId).toBe('stat-fresh');
    expect(ranked[0].reasons).toContain('STAT priority');
  });

  it('within a priority band, the longest wait wins', () => {
    const ranked = rankCandidates(
      [candidate('new', 'Routine', 5), candidate('old', 'Routine', 300)],
      { now },
    );
    expect(ranked[0].reportId).toBe('old');
  });

  it('prefers a case matching what the reader has been reading', () => {
    const history = reads([[0, 'MR', 'Brain'], [10, 'MR', 'Brain'], [20, 'MR', 'Brain']]);
    const ranked = rankCandidates(
      [candidate('ct', 'Routine', 30, 'CT'), { ...candidate('mr', 'Routine', 30, 'MR'), bodyPart: 'Brain' }],
      { now, history },
    );
    expect(ranked[0].reportId).toBe('mr');
    expect(ranked[0].reasons).toContain('matches what you have been reading');
  });

  it('returns null for an empty queue', () => {
    expect(suggestNext([], { now })).toBeNull();
  });
});
