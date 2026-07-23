import { describe, it, expect, beforeEach } from 'vitest';
import type { CrossCheckStatus } from '@/lib/api';
import { jobKindLabel, openReportHref, type Job } from '@/lib/jobs';
import {
  crossCheckPollPatch,
  deriveCrossCheckBadge,
  latestCrossCheckJob,
} from '@/lib/dictation/crossCheckJob';
import { crossCheckSession } from '@/lib/dictation/crossCheckSession';

// FE-PR6 — cross-check migration to a durable async job pair. These tests cover
// the pure core kept out of the heavyweight ReportClient (the reportPage.test.tsx /
// cleanupJob.test.tsx doctrine): the local audio-half poll-patch mapper, the
// two-job badge-state derivation, the job-selection helper, the origin-based
// label/deep-link rules (reconciliation #3), and the cross-mount session store
// (code-review hardening — a per-component ref does not survive an in-app
// navigate-away-and-back, so the audio→review hand-off and the retained ASR
// corrections live here instead). Neither half ever writes the report — that
// discipline lives in ReportClient and is asserted here only indirectly
// (nothing in this module produces a report write).

function job(overrides: Partial<Job> & Pick<Job, 'id' | 'origin' | 'reportId'>): Job {
  return {
    kind: 'crosscheck',
    mode: 'findings',
    status: 'queued',
    createdAt: Date.now(),
    attempt: 1,
    dismissed: false,
    seen: false,
    notified: false,
    ...overrides,
  };
}

function status(overrides: Partial<CrossCheckStatus> = {}): CrossCheckStatus {
  return { jobId: 'xc1', status: 'running', stage: 'transcribing', ...overrides };
}

describe('crossCheckPollPatch (local audio/ASR half poll mapping)', () => {
  it('maps "completed" -> "ok" and passes the raw stage through', () => {
    const j = job({ id: 'xc1', origin: 'local', reportId: 'r1', status: 'running' });
    const patch = crossCheckPollPatch(j, status({ status: 'completed', stage: 'done' }));
    expect(patch.status).toBe('ok');
    expect(patch.stage).toBe('done');
    expect(patch.completedAt).toBeTypeOf('number');
  });

  it('maps "failed" -> "error" and carries the sidecar error message', () => {
    const j = job({ id: 'xc1', origin: 'local', reportId: 'r1', status: 'running' });
    const patch = crossCheckPollPatch(j, status({ status: 'failed', stage: 'engine crashed', error: 'boom' }));
    expect(patch.status).toBe('error');
    expect(patch.error).toBe('boom');
    expect(patch.completedAt).toBeTypeOf('number');
  });

  it('passes "queued"/"running" through unchanged, without setting completedAt', () => {
    const j = job({ id: 'xc1', origin: 'local', reportId: 'r1', status: 'queued' });
    const patch = crossCheckPollPatch(j, status({ status: 'running', stage: 'reconciling engines' }));
    expect(patch.status).toBe('running');
    expect(patch.stage).toBe('reconciling engines');
    expect(patch.completedAt).toBeUndefined();
  });

  it('does not overwrite an already-set completedAt', () => {
    const j = job({ id: 'xc1', origin: 'local', reportId: 'r1', status: 'running', completedAt: 42 });
    const patch = crossCheckPollPatch(j, status({ status: 'completed' }));
    expect(patch.completedAt).toBeUndefined(); // UPDATE spreads onto the existing job — 42 survives
  });
});

describe('deriveCrossCheckBadge (two-job pure derivation, never manual setState)', () => {
  const audio = (overrides: Partial<Job> = {}) =>
    job({ id: 'a1', origin: 'local', reportId: 'r1', ...overrides });
  const review = (overrides: Partial<Job> = {}) =>
    job({ id: 'v1', origin: 'hosted', reportId: 'r1', ...overrides });

  it('returns null with no audio job tracked (nothing to show)', () => {
    expect(deriveCrossCheckBadge(undefined, undefined, 0)).toBeNull();
  });

  it('running while the audio half is active, passing its stage through', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'running', stage: 'reconciling engines' }), undefined, 0);
    expect(badge).toEqual({ status: 'running', stage: 'reconciling engines' });
  });

  it('failed when the audio half errors (review never starts)', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'error', error: 'sidecar unavailable' }), undefined, 0);
    expect(badge).toEqual({ status: 'failed', stage: 'sidecar unavailable' });
  });

  it('running "Medical review…" once audio is ok but the review job has not appeared/settled yet', () => {
    expect(deriveCrossCheckBadge(audio({ status: 'ok' }), undefined, 0)).toEqual({
      status: 'running',
      stage: 'Medical review…',
    });
    expect(
      deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'running' }), 0),
    ).toEqual({ status: 'running', stage: 'Medical review…' });
  });

  it('completed with a suggestion count once both halves settle ok', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'ok' }), 3);
    expect(badge).toEqual({ status: 'completed', stage: '3 suggestions' });
  });

  it('singularizes "1 suggestion" and reports "no changes" for zero', () => {
    expect(deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'ok' }), 1)).toEqual({
      status: 'completed',
      stage: '1 suggestion',
    });
    expect(deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'ok' }), 0)).toEqual({
      status: 'completed',
      stage: 'no changes',
    });
  });

  it('completed with "medical review unavailable" when the review half fails — never hides the ASR-only result', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'error' }), 2);
    expect(badge).toEqual({ status: 'completed', stage: '2 suggestions · medical review unavailable' });
  });

  it('failed (distinct fixed copy) when the audio half is cancelled — a separate branch from error, exercised independently', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'cancelled' }), undefined, 0);
    expect(badge).toEqual({ status: 'failed', stage: 'Cross-check cancelled.' });
  });

  it('completed with "medical review unavailable" when the review half is CANCELLED (shares the error branch — exercised independently so a future split cannot silently regress it)', () => {
    const badge = deriveCrossCheckBadge(audio({ status: 'ok' }), review({ status: 'cancelled' }), 1);
    expect(badge).toEqual({ status: 'completed', stage: '1 suggestion · medical review unavailable' });
  });
});

describe('latestCrossCheckJob (job-pair selection)', () => {
  it('picks the most recently created, non-dismissed job of the requested origin for the report', () => {
    const jobs: Job[] = [
      job({ id: 'old', origin: 'local', reportId: 'r1', createdAt: 1 }),
      job({ id: 'new', origin: 'local', reportId: 'r1', createdAt: 2 }),
      job({ id: 'other-report', origin: 'local', reportId: 'r2', createdAt: 3 }),
      job({ id: 'other-origin', origin: 'hosted', reportId: 'r1', createdAt: 3 }),
      job({ id: 'not-crosscheck', origin: 'local', reportId: 'r1', kind: 'local-generate', createdAt: 3 }),
    ];
    expect(latestCrossCheckJob(jobs, 'r1', 'local')?.id).toBe('new');
  });

  it('excludes dismissed jobs (a stale, dismissed pair never resurfaces the badge)', () => {
    const jobs: Job[] = [
      job({ id: 'dismissed-newer', origin: 'local', reportId: 'r1', createdAt: 5, dismissed: true }),
      job({ id: 'kept', origin: 'local', reportId: 'r1', createdAt: 1 }),
    ];
    expect(latestCrossCheckJob(jobs, 'r1', 'local')?.id).toBe('kept');
  });

  it('returns undefined when no matching job exists', () => {
    expect(latestCrossCheckJob([], 'r1', 'local')).toBeUndefined();
  });
});

describe('jobKindLabel — cross-check labeled by ORIGIN, not mode (reconciliation #3)', () => {
  it('labels the local audio half distinctly from the hosted review half', () => {
    expect(jobKindLabel({ kind: 'crosscheck', mode: 'findings', origin: 'local' })).toBe('Cross-check (audio)');
    expect(jobKindLabel({ kind: 'crosscheck', mode: 'findings', origin: 'hosted' })).toBe('Cross-check (review)');
  });

  it('is unaffected by the mode value (mode is the normalized sectionKey/"report", never a fixed literal)', () => {
    expect(jobKindLabel({ kind: 'crosscheck', mode: 'report', origin: 'hosted' })).toBe('Cross-check (review)');
    expect(jobKindLabel({ kind: 'crosscheck', mode: 'impression', origin: 'hosted' })).toBe('Cross-check (review)');
  });

  it('still labels ordinary ai/generate/local-generate jobs as before', () => {
    expect(jobKindLabel({ kind: 'ai', mode: 'impression', origin: 'hosted' })).toBe('Impression');
    expect(jobKindLabel({ kind: 'ai', mode: 'cleanup', origin: 'hosted' })).toBe('Dictation cleanup');
    expect(jobKindLabel({ kind: 'generate', mode: 'generate', origin: 'hosted' })).toBe('Draft generation');
    expect(jobKindLabel({ kind: 'local-generate', mode: 'report', origin: 'local' })).toBe('Local draft (MedGemma)');
  });
});

describe('openReportHref — only the HOSTED cross-check review half deep-links', () => {
  it('the hosted review half gets the ?aiJob= hint (opens the corrections panel)', () => {
    const href = openReportHref(job({ id: 'v1', origin: 'hosted', reportId: 'r1', kind: 'crosscheck' }));
    expect(href).toContain('aiJob=v1');
  });

  it('the local audio half never deep-links — nothing to apply from the widget on its own', () => {
    const href = openReportHref(job({ id: 'a1', origin: 'local', reportId: 'r1', kind: 'crosscheck' }));
    expect(href).not.toContain('aiJob=');
    expect(href).not.toContain('localJob=');
  });

  it('an already-applied review job carries no hint (widget stops nudging once consumed)', () => {
    const href = openReportHref(
      job({ id: 'v1', origin: 'hosted', reportId: 'r1', kind: 'crosscheck', applied: true }),
    );
    expect(href).not.toContain('aiJob=');
  });
});

describe('crossCheckSession (module-level, cross-mount store — code-review hardening)', () => {
  beforeEach(() => {
    crossCheckSession._reset();
  });

  it('start/get round-trips the submit-time context, keyed by the audio jobId', () => {
    crossCheckSession.start('audio1', { transcript: 'liver is normal', sectionKey: 'findings', useUbag: true });
    expect(crossCheckSession.get('audio1')).toEqual({
      transcript: 'liver is normal',
      sectionKey: 'findings',
      useUbag: true,
      reviewSubmitted: false,
    });
  });

  it('get returns undefined for a job this session never started — the "hydrated from elsewhere" no-op path', () => {
    expect(crossCheckSession.get('never-submitted')).toBeUndefined();
  });

  it('claimReviewSubmit returns true exactly once per audio job, then false on every subsequent call', () => {
    crossCheckSession.start('audio1', { transcript: 't', sectionKey: 'findings', useUbag: false });
    expect(crossCheckSession.claimReviewSubmit('audio1')).toBe(true);
    expect(crossCheckSession.claimReviewSubmit('audio1')).toBe(false);
    expect(crossCheckSession.claimReviewSubmit('audio1')).toBe(false);
  });

  it('claimReviewSubmit returns false for a job that was never started (nothing to claim)', () => {
    expect(crossCheckSession.claimReviewSubmit('unknown')).toBe(false);
  });

  it('survives what would be a ReportClient remount: a session started before "unmount" is still readable/claimable after', () => {
    // Simulates the exact bug this module fixes — the audio job finishes while
    // the report page is unmounted (a component ref would be gone; this store
    // is module-level and outlives any single mount).
    crossCheckSession.start('audio1', { transcript: 't', sectionKey: 'impression', useUbag: false });
    // ... time passes, ReportClient unmounts and remounts (no-op here — the
    // whole point is that nothing needs to happen to this store) ...
    const info = crossCheckSession.get('audio1');
    expect(info?.sectionKey).toBe('impression');
    expect(crossCheckSession.claimReviewSubmit('audio1')).toBe(true);
  });

  it('setAsr/takeAsr: takeAsr returns the retained set exactly once, then clears it', () => {
    crossCheckSession.setAsr('review1', {
      sectionKey: 'findings',
      corrections: [
        {
          id: 'c1', sectionKey: 'findings', originalText: 'liver normal', correctedText: 'liver is normal',
          startOffset: 0, endOffset: 12, reason: 'grammar', category: 'style', source: 'asr',
          confidence: 0.9, severity: 'info',
        },
      ],
    });
    const first = crossCheckSession.takeAsr('review1');
    expect(first?.corrections).toHaveLength(1);
    expect(crossCheckSession.takeAsr('review1')).toBeUndefined(); // consumed — a second read is empty, not a stale replay
  });

  it('takeAsr on a jobId with nothing retained returns undefined (asr-loss falls back to an empty array in the caller, never throws)', () => {
    expect(crossCheckSession.takeAsr('never-had-asr')).toBeUndefined();
  });

  it('setActivity/getActivity/clearActivity round-trips the activity-rail entry id', () => {
    crossCheckSession.setActivity('job1', 42);
    expect(crossCheckSession.getActivity('job1')).toBe(42);
    crossCheckSession.clearActivity('job1');
    expect(crossCheckSession.getActivity('job1')).toBeUndefined();
  });
});

