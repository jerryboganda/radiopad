'use client';

/**
 * Cross-mount-surviving bookkeeping for an in-flight cross-check sequencing run
 * (FE-PR6 hardening — code-review finding). `ReportClient`'s own refs are
 * scoped to ONE component instance and reset on every remount — and a remount
 * happens on ordinary in-app SPA navigation away and back (open the worklist,
 * open another report, come back), not only a full page reload. The tracked
 * `Job` rows themselves persist across that navigation in the app-root
 * `JobsProvider`, which is what surfaced the bug this module fixes: without a
 * matching store at THIS lifetime,
 *
 *   (a) if the audio job finished while the report page was unmounted, the
 *       hosted review half would simply never be submitted (the hand-off
 *       depended on a per-mount ref that no longer existed), and
 *   (b) re-opening an already-completed review job later would replay the
 *       merge with the ASR-only corrections silently missing (they can carry
 *       `severity: 'safety'`) and any already-accepted/rejected LLM
 *       corrections resurrected, because that state also lived only in the
 *       original mount's refs.
 *
 * Module-level, mirroring the established `lib/jobStream.ts` (`jobPartials`)
 * pattern — cleared once consumed, never persisted to storage (correction
 * snippets are clinical text; "gone on a full reload" matches jobPartials'
 * own boundary and is acceptable for the same reason: nothing here is a
 * substitute for the durable server-side retained result).
 */

import type { CrossCheckCorrection } from '@/lib/api';

interface PendingRun {
  transcript: string;
  sectionKey: string;
  useUbag: boolean;
  /** Guards the audio→review hand-off so it fires at most once per audio job,
   *  even if the owning effect ticks more than once before the submit settles. */
  reviewSubmitted: boolean;
}

const runs = new Map<string, PendingRun>(); // audio jobId -> submit-time context
const asrByReviewJob = new Map<string, { sectionKey: string; corrections: CrossCheckCorrection[] }>();
const activityByJob = new Map<string, number>(); // jobId (either half) -> activity-rail entry id

export const crossCheckSession = {
  /** Record the audio half's submit-time context, keyed by its jobId. */
  start(audioJobId: string, ctx: Omit<PendingRun, 'reviewSubmitted'>): void {
    runs.set(audioJobId, { ...ctx, reviewSubmitted: false });
  },

  /** The audio half's context, if this session (this browser tab, any mount)
   *  is the one that submitted it. */
  get(audioJobId: string): PendingRun | undefined {
    return runs.get(audioJobId);
  },

  /** Atomically claims the audio→review hand-off for one audio job — returns
   *  `false` if it was already claimed (by this mount or an earlier one), so
   *  the caller never double-submits the review. Synchronous Map mutation:
   *  JS has no interleaving here, so this needs no lock. */
  claimReviewSubmit(audioJobId: string): boolean {
    const run = runs.get(audioJobId);
    if (!run || run.reviewSubmitted) return false;
    run.reviewSubmitted = true;
    return true;
  },

  /** Retain the audio half's ASR corrections, keyed by the HOSTED review
   *  job's id (assigned only once the review is submitted). */
  setAsr(reviewJobId: string, v: { sectionKey: string; corrections: CrossCheckCorrection[] }): void {
    asrByReviewJob.set(reviewJobId, v);
  },

  /** Consume (fetch + clear) the retained ASR set for a settled review job —
   *  a one-shot read, since the merge it feeds should only ever happen once. */
  takeAsr(reviewJobId: string): { sectionKey: string; corrections: CrossCheckCorrection[] } | undefined {
    const v = asrByReviewJob.get(reviewJobId);
    asrByReviewJob.delete(reviewJobId);
    return v;
  },

  setActivity(jobId: string, activityId: number): void {
    activityByJob.set(jobId, activityId);
  },

  getActivity(jobId: string): number | undefined {
    return activityByJob.get(jobId);
  },

  clearActivity(jobId: string): void {
    activityByJob.delete(jobId);
  },

  /** Test-only reset. */
  _reset(): void {
    runs.clear();
    asrByReviewJob.clear();
    activityByJob.clear();
  },
};
