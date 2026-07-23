import { describe, it, expect } from 'vitest';
import {
  dedupeKey,
  envelopePatch,
  formatElapsed,
  initialJobsState,
  isActiveStatus,
  isTerminalStatus,
  jobDescriptor,
  jobKindLabel,
  jobsReducer,
  nextPollDelay,
  openReportHref,
  progressPatch,
  seedToJob,
  specMode,
  stageLabel,
  summaryPatch,
  summaryToJob,
  visibleJobs,
  type Job,
  type JobsState,
} from '@/lib/jobs';
import type { AiJobEnvelope, JobSummary } from '@/lib/api';

// The pure model behind the durable async-job platform: reducer transitions,
// rehydration merge, and the wire→Job mappers. No React, no network.

function makeJob(partial: Partial<Job> = {}): Job {
  return {
    id: 'j1',
    origin: 'hosted',
    kind: 'generate',
    mode: 'generate',
    reportId: 'r1',
    status: 'queued',
    createdAt: 1_000,
    attempt: 1,
    dismissed: false,
    seen: false,
    notified: false,
    ...partial,
  };
}

function stateWith(...jobs: Job[]): JobsState {
  return { jobs };
}

describe('jobsReducer — lifecycle transitions', () => {
  it('ADD inserts a new job; a second ADD with the same id is idempotent', () => {
    let s = jobsReducer(initialJobsState(), { type: 'ADD', job: makeJob() });
    expect(s.jobs).toHaveLength(1);
    s = jobsReducer(s, { type: 'ADD', job: makeJob({ status: 'running' }) });
    expect(s.jobs).toHaveLength(1);
    expect(s.jobs[0].status).toBe('running');
  });

  it('UPDATE walks queued → running → ok', () => {
    let s = stateWith(makeJob());
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: { status: 'running', startedAt: 2_000 } });
    expect(s.jobs[0].status).toBe('running');
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: { status: 'ok', completedAt: 3_000 } });
    expect(s.jobs[0].status).toBe('ok');
    expect(s.jobs[0].completedAt).toBe(3_000);
  });

  it('first terminal outcome wins — a later UPDATE cannot resurrect a settled job', () => {
    let s = stateWith(makeJob({ status: 'ok' }));
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: { status: 'error', errorKind: 'timeout' } });
    expect(s.jobs[0].status).toBe('ok');
    expect(s.jobs[0].errorKind).toBeUndefined();
  });

  it('UPDATE models the client-synthesised "lost" terminal error', () => {
    let s = stateWith(makeJob({ status: 'running' }));
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: { status: 'error', errorKind: 'lost' } });
    expect(s.jobs[0].status).toBe('error');
    expect(s.jobs[0].errorKind).toBe('lost');
  });

  it('CANCEL_REQUESTED flags only active jobs', () => {
    let s = stateWith(makeJob({ status: 'running' }), makeJob({ id: 'j2', status: 'ok' }));
    s = jobsReducer(s, { type: 'CANCEL_REQUESTED', id: 'j1' });
    s = jobsReducer(s, { type: 'CANCEL_REQUESTED', id: 'j2' });
    expect(s.jobs[0].cancelRequested).toBe(true);
    expect(s.jobs[1].cancelRequested).toBeUndefined();
  });

  it('MARK_SEEN marks every terminal job seen but leaves active ones', () => {
    let s = stateWith(makeJob({ status: 'running' }), makeJob({ id: 'j2', status: 'error' }));
    s = jobsReducer(s, { type: 'MARK_SEEN' });
    expect(s.jobs[0].seen).toBe(false);
    expect(s.jobs[1].seen).toBe(true);
  });

  it('MARK_NOTIFIED and MARK_APPLIED target one job', () => {
    let s = stateWith(makeJob({ status: 'ok' }));
    s = jobsReducer(s, { type: 'MARK_NOTIFIED', id: 'j1' });
    s = jobsReducer(s, { type: 'MARK_APPLIED', id: 'j1' });
    expect(s.jobs[0].notified).toBe(true);
    expect(s.jobs[0].applied).toBe(true);
  });

  it('DISMISS / CLEAR_FINISHED / CLEAR_ALL', () => {
    let s = stateWith(makeJob({ status: 'ok' }), makeJob({ id: 'j2', status: 'running' }));
    s = jobsReducer(s, { type: 'CLEAR_FINISHED' });
    expect(s.jobs[0].dismissed).toBe(true); // terminal
    expect(s.jobs[1].dismissed).toBe(false); // active untouched
    s = jobsReducer(s, { type: 'DISMISS', id: 'j2' });
    expect(s.jobs[1].dismissed).toBe(true);
    s = jobsReducer(s, { type: 'CLEAR_ALL' });
    expect(s.jobs).toHaveLength(0);
  });
});

describe('jobsReducer — HYDRATE merge & dedupe', () => {
  it('merges a server summary onto a localStorage seed of the same id, preserving client flags', () => {
    const seed = seedToJob({ id: 'lj1', reportId: 'r9', kind: 'local-generate', createdAt: 500 });
    let s = stateWith({ ...seed, seen: true, dismissed: false });
    const summary: JobSummary = {
      jobId: 'lj1',
      kind: 'local-generate',
      mode: 'report',
      status: 'running',
      errorKind: null,
      error: null,
      attempt: 1,
      retryOfJobId: null,
      reportId: 'r9',
      report: { accession: 'A1', modality: 'CT', bodyPart: 'Chest', status: 'Draft' },
      createdAt: new Date(500).toISOString(),
      startedAt: null,
      completedAt: null,
      elapsedMs: 0,
    };
    s = jobsReducer(s, { type: 'HYDRATE', jobs: [summaryToJob(summary, 'local')] });
    expect(s.jobs).toHaveLength(1); // deduped by id
    expect(s.jobs[0].report?.modality).toBe('CT'); // server descriptor filled in
    expect(s.jobs[0].seen).toBe(true); // client flag preserved
  });

  it('dedupes two incoming rows with the same id in one HYDRATE', () => {
    const a = makeJob({ id: 'dup', status: 'queued' });
    const b = makeJob({ id: 'dup', status: 'running' });
    const s = jobsReducer(initialJobsState(), { type: 'HYDRATE', jobs: [a, b] });
    expect(s.jobs).toHaveLength(1);
    expect(s.jobs[0].status).toBe('running');
  });
});

describe('visibleJobs', () => {
  it('drops dismissed jobs and sorts newest first', () => {
    const s = stateWith(
      makeJob({ id: 'old', createdAt: 100 }),
      makeJob({ id: 'new', createdAt: 900 }),
      makeJob({ id: 'gone', createdAt: 500, dismissed: true }),
    );
    const vis = visibleJobs(s);
    expect(vis.map((j) => j.id)).toEqual(['new', 'old']);
  });
});

describe('status + scheduling helpers', () => {
  it('classifies terminal vs active', () => {
    expect(isTerminalStatus('ok')).toBe(true);
    expect(isTerminalStatus('cancelled')).toBe(true);
    expect(isActiveStatus('queued')).toBe(true);
    expect(isActiveStatus('running')).toBe(true);
    expect(isActiveStatus('ok')).toBe(false);
  });

  it('nextPollDelay ramps toward the visible cap and the hidden floor', () => {
    expect(nextPollDelay(300, false)).toBe(600);
    expect(nextPollDelay(600, false)).toBe(1_200);
    expect(nextPollDelay(1_200, false)).toBe(2_000); // clamped
    expect(nextPollDelay(2_000, false)).toBe(2_000);
    // hidden: 5s floor lets it ramp past the visible cap
    expect(nextPollDelay(1_200, true)).toBe(2_400);
    expect(nextPollDelay(2_400, true)).toBe(4_800);
    expect(nextPollDelay(4_800, true)).toBe(5_000); // clamped
  });

  it('dedupeKey + specMode identify a logical submission', () => {
    expect(dedupeKey('r1', 'ai', 'impression')).toBe('r1::ai::impression');
    expect(specMode({ origin: 'hosted', kind: 'generate', reportId: 'r1' })).toBe('generate');
    expect(specMode({ origin: 'hosted', kind: 'ai', reportId: 'r1', mode: 'rewrite' })).toBe('rewrite');
    expect(specMode({ origin: 'local', kind: 'local-generate', reportId: 'r1', dto: {} })).toBe('report');
  });
});

describe('labels & descriptors', () => {
  it('maps kind+mode to a label', () => {
    expect(jobKindLabel({ kind: 'generate', mode: 'generate' })).toBe('Draft generation');
    expect(jobKindLabel({ kind: 'local-generate', mode: 'report' })).toBe('Local draft (MedGemma)');
    expect(jobKindLabel({ kind: 'ai', mode: 'impression' })).toBe('Impression');
    expect(jobKindLabel({ kind: 'ai', mode: 'rewrite' })).toBe('Rewrite');
  });

  it('jobDescriptor prefers modality · bodyPart, else the kind label', () => {
    expect(jobDescriptor(makeJob({ report: { accession: 'A', modality: 'MR', bodyPart: 'Brain' } }))).toBe('MR · Brain');
    expect(jobDescriptor(makeJob())).toBe('Draft generation');
  });

  it('stageLabel maps sidecar stages', () => {
    expect(stageLabel('queued')).toBe('Queued behind another local job');
    expect(stageLabel('model-loading')).toBe('Loading model…');
    expect(stageLabel('generating')).toBe('Generating…');
    expect(stageLabel(undefined)).toBeNull();
  });

  it('formatElapsed renders mm:ss', () => {
    expect(formatElapsed(0)).toBe('0:00');
    expect(formatElapsed(65_000)).toBe('1:05');
    expect(formatElapsed(600_000)).toBe('10:00');
  });
});

describe('openReportHref', () => {
  it('appends ?aiJob for an unapplied ai job', () => {
    expect(openReportHref(makeJob({ id: 'a1', kind: 'ai', mode: 'impression' }))).toBe(
      '/reports/view?id=r1&aiJob=a1',
    );
  });
  it('appends ?localJob for an unapplied local job', () => {
    expect(openReportHref(makeJob({ id: 'l1', origin: 'local', kind: 'local-generate' }))).toBe(
      '/reports/view?id=r1&localJob=l1',
    );
  });
  it('omits the hint once applied, and for a plain generate job', () => {
    expect(openReportHref(makeJob({ kind: 'ai', mode: 'impression', applied: true }))).toBe('/reports/view?id=r1');
    expect(openReportHref(makeJob({ kind: 'generate' }))).toBe('/reports/view?id=r1');
  });
});

describe('wire → Job mappers', () => {
  it('summaryToJob marks an already-terminal row notified (no stale toast on reload)', () => {
    const summary: JobSummary = {
      jobId: 'j9', kind: 'ai', mode: 'impression', status: 'ok', errorKind: null, error: null,
      attempt: 2, retryOfJobId: 'j8', reportId: 'r1', report: null,
      createdAt: new Date(1000).toISOString(), startedAt: null, completedAt: null, elapsedMs: 10,
    };
    const job = summaryToJob(summary, 'hosted');
    expect(job.notified).toBe(true);
    expect(job.attempt).toBe(2);
    expect(job.retryOfJobId).toBe('j8');
  });

  it('envelopePatch derives startedAt from elapsedMs and stamps completedAt on terminal', () => {
    const running = makeJob({ status: 'running' });
    const env: AiJobEnvelope<unknown> = {
      jobId: 'j1', kind: 'generate', mode: 'generate', status: 'ok', elapsedMs: 4_000, result: null, error: null, errorKind: null,
    };
    const patch = envelopePatch(running, env);
    expect(patch.status).toBe('ok');
    expect(patch.completedAt).toBeGreaterThan(0);
  });

  it('envelopePatch carries the local stage', () => {
    const local = makeJob({ origin: 'local', kind: 'local-generate', status: 'running' });
    const env = {
      jobId: 'l1', kind: 'local-generate' as const, mode: 'report', status: 'running' as const,
      elapsedMs: 1_000, result: null, error: null, errorKind: null, stage: 'model-loading' as const,
    };
    expect(envelopePatch(local, env).stage).toBe('model-loading');
  });

  it('envelopePatch carries live progress for an active envelope, drops it once terminal', () => {
    const running = makeJob({ status: 'running' });
    const active: AiJobEnvelope<unknown> = {
      jobId: 'j1', kind: 'ai', mode: 'impression', status: 'running', elapsedMs: 500,
      result: null, error: null, errorKind: null, progress: { tokens: 12, percent: null },
    };
    expect(envelopePatch(running, active).progress).toEqual({ tokens: 12, percent: undefined });
    const done: AiJobEnvelope<unknown> = { ...active, status: 'ok', progress: { tokens: 40 } };
    expect(envelopePatch(running, done).progress).toBeUndefined();
  });
});

describe('progress patches (bus + reducer)', () => {
  it('UPDATE applies a progress patch to an active job', () => {
    let s = stateWith(makeJob({ status: 'running' }));
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: progressPatch({ tokens: 42 }) });
    expect(s.jobs[0].progress).toEqual({ tokens: 42, percent: undefined });
    expect(s.jobs[0].status).toBe('running');
  });

  it('UPDATE no-ops a progress patch that arrives after the job is terminal (first-terminal-wins)', () => {
    let s = stateWith(makeJob({ status: 'ok' }));
    s = jobsReducer(s, { type: 'UPDATE', id: 'j1', patch: progressPatch({ tokens: 99 }) });
    expect(s.jobs[0].progress).toBeUndefined();
    expect(s.jobs[0].status).toBe('ok');
  });

  it('progressPatch keeps a real percent but omits a null/undefined one', () => {
    expect(progressPatch({ tokens: 10 }).progress).toEqual({ tokens: 10, percent: undefined });
    expect(progressPatch({ tokens: 10, percent: null }).progress).toEqual({ tokens: 10, percent: undefined });
    expect(progressPatch({ tokens: 10, percent: 0.5 }).progress).toEqual({ tokens: 10, percent: 0.5 });
  });
});

describe('summaryPatch (bus job event / unified detail)', () => {
  function activeSummary(overrides: Partial<JobSummary> = {}): JobSummary {
    return {
      jobId: 'j5', kind: 'ai', mode: 'impression', status: 'running', errorKind: null, error: null,
      attempt: 1, retryOfJobId: null, reportId: 'r3',
      report: { accession: 'A', modality: 'CT', bodyPart: 'Abdomen', status: 'Draft' },
      createdAt: new Date(1000).toISOString(), startedAt: null, completedAt: null, elapsedMs: 0,
      progress: { tokens: 7, percent: null },
      ...overrides,
    };
  }

  it('an unknown-active SSE job event ADDs a Job equivalent to summaryToJob', () => {
    const summary = activeSummary();
    const s = jobsReducer(initialJobsState(), { type: 'ADD', job: summaryToJob(summary, 'hosted') });
    expect(s.jobs[0]).toMatchObject({
      id: 'j5', status: 'running', reportId: 'r3', progress: { tokens: 7, percent: undefined },
    });
    expect(s.jobs[0].report?.bodyPart).toBe('Abdomen');
  });

  it('maps server fields (status/report/progress) onto a known active row', () => {
    const patch = summaryPatch(makeJob({ id: 'j5', status: 'queued' }), activeSummary());
    expect(patch.status).toBe('running');
    expect(patch.report?.bodyPart).toBe('Abdomen');
    expect(patch.progress).toEqual({ tokens: 7, percent: undefined });
  });

  it('stamps completedAt and drops progress on a terminal summary', () => {
    const patch = summaryPatch(
      makeJob({ status: 'running' }),
      activeSummary({ status: 'ok', progress: { tokens: 100 } }),
    );
    expect(patch.status).toBe('ok');
    expect(patch.completedAt).toBeGreaterThan(0);
    expect(patch.progress).toBeUndefined();
  });

  it('summaryToJob copies progress for active rows and drops it once terminal', () => {
    expect(summaryToJob(activeSummary({ status: 'running', progress: { tokens: 3 } }), 'hosted').progress)
      .toEqual({ tokens: 3, percent: undefined });
    expect(summaryToJob(activeSummary({ status: 'ok', progress: { tokens: 3 } }), 'hosted').progress)
      .toBeUndefined();
  });
});
