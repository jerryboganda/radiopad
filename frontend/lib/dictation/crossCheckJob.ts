// Cross-check migration to a durable async job pair (durable async-job platform,
// 2026-07-23, FE-PR6). Two tracked jobs cooperate for one manual Cross Check run:
// a LOCAL sidecar job (kind='crosscheck', origin='local') re-runs the retained
// dictation audio through the extra on-device engines, and — once it succeeds —
// a HOSTED job (kind='crosscheck', origin='hosted') runs the LLM medical-accuracy
// review on the same transcript. Neither half writes the report: both produce a
// suggestion set the radiologist accepts/rejects per item in the existing
// corrections panel (never a bulk/auto apply).
//
// This module holds the pure, unit-tested core kept out of the heavyweight
// ReportClient component (the reportPage.test.tsx / cleanupJob.test.tsx doctrine):
// the local-job poll-patch mapper and the two-job badge-state derivation.

import type { CrossCheckStatus } from '@/lib/api';
import { isActiveStatus, type Job } from '@/lib/jobs';

/** Map the sidecar's cross-check status vocabulary onto the shared `JobStatus`
 *  wire vocabulary. Distinct from every other job kind's poll envelope (hence
 *  not routed through the generic `envelopePatch`): `completed`/`failed` here,
 *  not `ok`/`error`. */
function toJobStatus(s: CrossCheckStatus['status']): Job['status'] {
  if (s === 'completed') return 'ok';
  if (s === 'failed') return 'error';
  return s; // 'queued' | 'running' pass through unchanged
}

/**
 * Poll patch for the LOCAL audio/ASR half (`JobsProvider.pollOne`'s dedicated
 * crosscheck branch — `api.reports.crossCheckStatus`, never
 * `api.localGenerate.jobStatus`, a different sidecar endpoint entirely). The
 * sidecar's raw `stage` string is passed straight through — `CrossCheckBadge`
 * reads it directly, never through the fixed-vocabulary `stageLabel`.
 * Corrections are deliberately NOT copied onto the `Job` row: clinical text
 * never lands in the shared job model (jobs.ts's own documented invariant) —
 * the terminal handler re-fetches them once, on the `ok` transition.
 */
export function crossCheckPollPatch(job: Job, s: CrossCheckStatus): Partial<Job> {
  const status = toJobStatus(s.status);
  const patch: Partial<Job> = { status, stage: s.stage };
  if (s.error) patch.error = s.error;
  if ((status === 'ok' || status === 'error') && job.completedAt == null) {
    patch.completedAt = Date.now();
  }
  return patch;
}

/** The processing-badge state derived from the two tracked jobs for the
 *  CURRENT run — a pure function, not manual `setState` calls scattered
 *  through the imperative flow. `correctionsCount` is the live count already
 *  surfaced in the editor's corrections panel (the same state both halves
 *  feed), used only for the completed summary line. */
export function deriveCrossCheckBadge(
  audioJob: Job | undefined,
  reviewJob: Job | undefined,
  correctionsCount: number,
): { status: 'running' | 'completed' | 'failed'; stage: string } | null {
  if (!audioJob) return null;

  if (isActiveStatus(audioJob.status)) {
    return { status: 'running', stage: audioJob.stage || 'Re-running engines…' };
  }
  if (audioJob.status === 'error') {
    return { status: 'failed', stage: audioJob.error || 'Cross-check failed.' };
  }
  if (audioJob.status === 'cancelled') {
    return { status: 'failed', stage: 'Cross-check cancelled.' };
  }

  // Audio half ok — the review half either hasn't appeared yet (the brief
  // submit gap between the audio terminal tick and the review job being
  // tracked) or is still running server-side.
  if (!reviewJob || isActiveStatus(reviewJob.status)) {
    return { status: 'running', stage: 'Medical review…' };
  }

  const summary =
    correctionsCount > 0
      ? `${correctionsCount} suggestion${correctionsCount === 1 ? '' : 's'}`
      : 'no changes';
  if (reviewJob.status === 'error' || reviewJob.status === 'cancelled') {
    return { status: 'completed', stage: `${summary} · medical review unavailable` };
  }
  return { status: 'completed', stage: summary };
}

/**
 * The most recently created, non-dismissed tracked job of one cross-check half
 * for a report. There is at most one live run at a time in normal use (the
 * button is re-entrancy-guarded while its own submit is in flight); picking
 * the newest by `createdAt` keeps the pairing correct even if an old pair from
 * a previous run is still sitting un-dismissed.
 */
export function latestCrossCheckJob(
  jobs: Job[],
  reportId: string,
  origin: Job['origin'],
): Job | undefined {
  return jobs
    .filter(
      (j) => j.reportId === reportId && j.kind === 'crosscheck' && j.origin === origin && !j.dismissed,
    )
    .sort((a, b) => b.createdAt - a.createdAt)[0];
}
