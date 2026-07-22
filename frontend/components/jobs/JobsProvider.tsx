'use client';

/**
 * Global async-AI-job provider (Phase 4.2). Owns the tracked job list, one
 * shared poll ticker (never per-component), rehydration on mount, and the
 * fire-once terminal side effects (toast + notifications bell + `radiopad:job-
 * terminal` event + an aria-live announcement).
 *
 * Mounted in `AppShell` under `AuthGate` (so it always polls as an authenticated
 * user) and inside `ToastProvider`. Desktop surface only — on web/mobile the
 * effects are inert (no report editor / sidecar exists there), but the context
 * is still provided so any accidental consumer gets a well-formed no-op.
 *
 * Storage discipline: the only thing written to localStorage is a metadata-only
 * seed of local-origin job ids (`{id, reportId, kind, createdAt}`) so the widget
 * can paint instantly on reload. No `result` payload or clinical text is ever
 * cached — "Apply"/open re-fetches server-side.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useRouter } from 'next/navigation';
import { api, isTransientPollError } from '@/lib/api';
import { describeAiError } from '@/lib/aiErrors';
import { isDesktopSurface } from '@/lib/surface';
import { useToast } from '@/components/ui/ToastProvider';
import { notify } from '@/components/shell/NotificationsBell';
import {
  JOBS_STORAGE_KEY,
  JOB_FIRST_POLL_MS,
  dedupeKey,
  envelopePatch,
  initialJobsState,
  isActiveStatus,
  isTerminalStatus,
  jobDescriptor,
  jobKindLabel,
  jobsReducer,
  nextPollDelay,
  openReportHref,
  seedToJob,
  specMode,
  summaryToJob,
  visibleJobs,
  type Job,
  type JobsContextValue,
  type JobSubmitSpec,
  type LocalJobSeed,
} from '@/lib/jobs';

const JobsContext = createContext<JobsContextValue | null>(null);

// ---------------------------------------------------------------------------
// Small runtime helpers
// ---------------------------------------------------------------------------

function isAuthError(e: unknown): boolean {
  return (e as { status?: number })?.status === 401;
}
function isNotFound(e: unknown): boolean {
  return (e as { status?: number })?.status === 404;
}
function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI__' in window;
}

function readSeed(): LocalJobSeed[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = window.localStorage.getItem(JOBS_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as { tenant?: string; jobs?: LocalJobSeed[] };
    if (!parsed || !Array.isArray(parsed.jobs)) return [];
    // A workspace switch is a full reload; drop a seed left by another tenant so
    // we never paint (or navigate to) a report the current tenant can't see.
    const tenant = window.localStorage.getItem('radiopad.tenant');
    if (parsed.tenant && tenant && parsed.tenant !== tenant) return [];
    return parsed.jobs.filter(
      (s) => s && typeof s.id === 'string' && typeof s.reportId === 'string',
    );
  } catch {
    return [];
  }
}

function writeSeed(jobs: Job[]): void {
  if (typeof window === 'undefined') return;
  try {
    const seed = jobs
      .filter((j) => j.origin === 'local' && !j.dismissed)
      .map((j) => ({ id: j.id, reportId: j.reportId, kind: j.kind, createdAt: j.createdAt }));
    if (seed.length === 0) {
      window.localStorage.removeItem(JOBS_STORAGE_KEY);
      return;
    }
    const tenant = window.localStorage.getItem('radiopad.tenant') ?? '';
    window.localStorage.setItem(JOBS_STORAGE_KEY, JSON.stringify({ v: 1, tenant, jobs: seed }));
  } catch {
    /* storage unavailable — the widget still works in-memory */
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export default function JobsProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { toast } = useToast();
  const [state, dispatch] = useReducer(jobsReducer, undefined, initialJobsState);
  const [announcement, setAnnouncement] = useState('');

  // Always-current view of state for the async callbacks (poll ticker, submit
  // dedupe, cancel/retry) so they never read a stale closure.
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  });

  // Session-only store of local-generate submit specs (holds the study-context
  // dto) so a local job — which has no hosted retry endpoint — can be re-submitted
  // client-side. Never persisted (it carries clinical context).
  const localSpecs = useRef<Map<string, JobSubmitSpec>>(new Map());

  // Ticker state.
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const delayRef = useRef<number>(JOB_FIRST_POLL_MS);
  // JobsProvider is mounted once at the AppShell root and never unmounts during
  // a session in practice, but runTick awaits a network round-trip mid-tick —
  // guard the reschedule below in case a future refactor (or a test harness)
  // unmounts it while a tick is in flight, so a dangling timer never dispatches
  // into a torn-down tree.
  const mountedRef = useRef(true);
  // Which poll path works for a hosted job (unified `api.jobs.get` vs the
  // report-scoped fallback that predates JobsController). Cached per job so we
  // don't double-request every tick during the backend rollout.
  const pollPathRef = useRef<Map<string, 'jobs' | 'report'>>(new Map());
  const tickingRef = useRef(false);
  // Guards the persistence effect from running on the initial empty-state commit
  // (before rehydration reads the seed) — otherwise it would delete the very seed
  // we are about to load.
  const didRehydrate = useRef(false);

  // --- keep localStorage seed in sync with local-origin jobs -----------------
  useEffect(() => {
    if (!isDesktopSurface || !didRehydrate.current) return;
    writeSeed(state.jobs);
  }, [state.jobs]);

  // --- fire-once terminal side effects ---------------------------------------
  const openReport = useCallback(
    (job: Job) => {
      router.push(openReportHref(job));
    },
    [router],
  );

  const fireTerminal = useCallback(
    (job: Job) => {
      const label = jobKindLabel(job);
      const descriptor = jobDescriptor(job);
      if (job.status === 'ok') {
        toast({
          tone: 'success',
          title: `${label} ready`,
          message: (
            <span className="rp-jobs-toast">
              {descriptor}
              <button type="button" className="subtle rp-jobs-toast-btn" onClick={() => openReport(job)}>
                Open report
              </button>
            </span>
          ),
        });
        notify({ title: `${label} ready`, detail: descriptor, tone: 'success' });
        setAnnouncement(`${label} ready — open the report to review.`);
      } else if (job.status === 'error') {
        const msg = describeAiError(job.errorKind, job.error);
        toast({ tone: 'danger', title: `${label} failed`, message: msg });
        notify({ title: `${label} failed`, detail: msg, tone: 'danger' });
        setAnnouncement(`${label} generation failed.`);
      } else if (job.status === 'cancelled') {
        toast({ tone: 'info', title: `${label} cancelled`, message: descriptor });
        notify({ title: `${label} cancelled`, detail: descriptor, tone: 'info' });
        setAnnouncement(`${label} cancelled.`);
      }
      // PHI-minimised event for the later Tauri OS-notification listener
      // (Phase 7): modality/bodyPart/kind only — never accession or identifiers.
      if (typeof window !== 'undefined') {
        window.dispatchEvent(
          new CustomEvent('radiopad:job-terminal', {
            detail: {
              jobId: job.id,
              status: job.status,
              kind: job.kind,
              mode: job.mode,
              reportId: job.reportId,
              report: job.report
                ? { modality: job.report.modality, bodyPart: job.report.bodyPart }
                : null,
            },
          }),
        );
      }
    },
    [toast, openReport],
  );

  useEffect(() => {
    for (const job of state.jobs) {
      if (isTerminalStatus(job.status) && !job.notified) {
        fireTerminal(job);
        dispatch({ type: 'MARK_NOTIFIED', id: job.id });
      }
    }
  }, [state.jobs, fireTerminal]);

  // --- shared poll ticker ----------------------------------------------------
  const clearTimer = useCallback(() => {
    if (timerRef.current != null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const pollOne = useCallback(async (job: Job): Promise<void> => {
    if (job.origin === 'local') {
      try {
        const env = await api.localGenerate.jobStatus(job.id);
        dispatch({ type: 'UPDATE', id: job.id, patch: envelopePatch(job, env) });
      } catch (e) {
        if (isAuthError(e)) throw e;
        if (isTransientPollError(e)) return; // keep polling — the job runs on
        // Sidecar registry is in-memory by doctrine — a 404 (or any deterministic
        // failure) after a restart means the in-flight generation is gone.
        dispatch({ type: 'UPDATE', id: job.id, patch: { status: 'error', errorKind: 'sidecar_restart' } });
      }
      return;
    }

    // Hosted job. Prefer the unified engine; fall back to the report-scoped poll
    // that exists today (JobsController may still be rolling out).
    const path = pollPathRef.current.get(job.id) ?? 'jobs';
    if (path === 'jobs') {
      try {
        const detail = await api.jobs.get(job.id);
        pollPathRef.current.set(job.id, 'jobs');
        dispatch({ type: 'UPDATE', id: job.id, patch: detailPatch(job, detail) });
        return;
      } catch (e) {
        if (isAuthError(e)) throw e;
        if (isTransientPollError(e)) return; // retry the unified path next tick
        // 404 (endpoint not deployed / unknown here) or other deterministic error
        // → the report-scoped endpoint is the proven fallback.
        pollPathRef.current.set(job.id, 'report');
      }
    }
    // report-scoped fallback
    if (!job.reportId) {
      dispatch({ type: 'UPDATE', id: job.id, patch: { status: 'error', errorKind: 'lost' } });
      return;
    }
    try {
      const env = await api.reports.aiJobStatus(job.reportId, job.id);
      dispatch({ type: 'UPDATE', id: job.id, patch: envelopePatch(job, env) });
    } catch (e) {
      if (isAuthError(e)) throw e;
      if (isTransientPollError(e)) return; // keep polling
      // Deterministic failure on the proven path — the job is gone.
      dispatch({ type: 'UPDATE', id: job.id, patch: { status: 'error', errorKind: 'lost' } });
    }
  }, []);

  const runTick = useCallback(async () => {
    timerRef.current = null;
    if (tickingRef.current) return;
    const active = stateRef.current.jobs.filter((j) => isActiveStatus(j.status) && !j.dismissed);
    if (active.length === 0) {
      delayRef.current = JOB_FIRST_POLL_MS;
      return; // idle — no timer running
    }
    tickingRef.current = true;
    const results = await Promise.allSettled(active.map((j) => pollOne(j)));
    tickingRef.current = false;

    if (!mountedRef.current) return; // unmounted while the poll round-trip was in flight

    // A hard 401 anywhere → the session is gone; stop and clear everything.
    if (results.some((r) => r.status === 'rejected' && isAuthError(r.reason))) {
      clearTimer();
      dispatch({ type: 'CLEAR_ALL' });
      return;
    }

    const stillActive = stateRef.current.jobs.some(
      (j) => isActiveStatus(j.status) && !j.dismissed,
    );
    if (!stillActive) {
      delayRef.current = JOB_FIRST_POLL_MS;
      return; // back to idle
    }
    delayRef.current = nextPollDelay(
      delayRef.current,
      typeof document !== 'undefined' && document.hidden,
    );
    clearTimer();
    timerRef.current = setTimeout(() => void runTick(), delayRef.current);
  }, [pollOne, clearTimer]);

  const kickTicker = useCallback(() => {
    delayRef.current = JOB_FIRST_POLL_MS;
    clearTimer();
    timerRef.current = setTimeout(() => void runTick(), JOB_FIRST_POLL_MS);
  }, [clearTimer, runTick]);

  // Restart the ticker whenever an active job appears and no timer is running
  // (covers hydration and any state change that (re)introduces active work).
  useEffect(() => {
    if (!isDesktopSurface) return;
    const hasActive = state.jobs.some((j) => isActiveStatus(j.status) && !j.dismissed);
    if (hasActive && timerRef.current == null && !tickingRef.current) {
      timerRef.current = setTimeout(() => void runTick(), delayRef.current);
    }
  }, [state.jobs, runTick]);

  // Poll promptly when the tab returns to the foreground.
  useEffect(() => {
    if (!isDesktopSurface || typeof document === 'undefined') return;
    const onVis = () => {
      if (!document.hidden) {
        const hasActive = stateRef.current.jobs.some(
          (j) => isActiveStatus(j.status) && !j.dismissed,
        );
        if (hasActive) kickTicker();
      }
    };
    document.addEventListener('visibilitychange', onVis);
    return () => document.removeEventListener('visibilitychange', onVis);
  }, [kickTicker]);

  // Stop the timer on unmount, and stop any in-flight tick from rescheduling.
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      clearTimer();
    };
  }, [clearTimer]);

  // --- rehydration on mount --------------------------------------------------
  useEffect(() => {
    if (!isDesktopSurface) return;
    let cancelled = false;
    // Read the seed BEFORE arming persistence so the empty-state commit can't
    // wipe it (the persistence effect no-ops until this flag is set).
    didRehydrate.current = true;

    // 1. Instant paint from the metadata-only local seed.
    const seed = readSeed();
    if (seed.length > 0) {
      dispatch({ type: 'HYDRATE', jobs: seed.map(seedToJob) });
    }

    // 2. Server truth (durable table) — 404 until JobsController lands = "none".
    void (async () => {
      try {
        const { jobs } = await api.jobs.list({ active: true, limit: 20 });
        if (!cancelled && Array.isArray(jobs)) {
          dispatch({ type: 'HYDRATE', jobs: jobs.map((s) => summaryToJob(s, 'hosted')) });
        }
      } catch (e) {
        if (!isNotFound(e)) {
          // Swallow — never throw into the render tree. Log for diagnostics.
          console.warn('[jobs] hosted rehydration failed', e);
        }
      }
      // 3. Sidecar local jobs (desktop/Tauri only).
      if (isTauriRuntime()) {
        try {
          const { jobs } = await api.localGenerate.listJobs();
          if (!cancelled && Array.isArray(jobs)) {
            dispatch({ type: 'HYDRATE', jobs: jobs.map((s) => summaryToJob(s, 'local')) });
          }
        } catch (e) {
          if (!isNotFound(e)) console.warn('[jobs] sidecar rehydration failed', e);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // --- public API ------------------------------------------------------------
  const findActive = useCallback((reportId: string, key: string): Job | undefined => {
    return stateRef.current.jobs.find(
      (j) => !j.dismissed && isActiveStatus(j.status) && dedupeKey(j.reportId, j.kind, j.mode) === key,
    );
  }, []);

  const submit = useCallback(
    async (spec: JobSubmitSpec): Promise<string> => {
      const mode = specMode(spec);
      const existing = findActive(spec.reportId, dedupeKey(spec.reportId, spec.kind, mode));
      if (existing) return existing.id; // dedupe — one row per logical submission

      let jobId: string;
      if (spec.origin === 'local') {
        ({ jobId } = await api.localGenerate.submitJob({
          ...spec.dto,
          correlationId: spec.reportId,
        }));
        localSpecs.current.set(jobId, spec);
      } else if (spec.kind === 'ai') {
        ({ jobId } = await api.reports.submitAiJob(spec.reportId, {
          mode: spec.mode,
          providerId: spec.providerId,
        }));
      } else {
        ({ jobId } = await api.reports.submitGenerateJob(spec.reportId, {
          providerId: spec.providerId,
        }));
      }

      const job: Job = {
        id: jobId,
        origin: spec.origin,
        kind: spec.kind,
        mode,
        reportId: spec.reportId,
        report: spec.report,
        status: 'queued',
        createdAt: Date.now(),
        attempt: 1,
        dismissed: false,
        seen: false,
        notified: false,
      };
      dispatch({ type: 'ADD', job });
      kickTicker();
      return jobId;
    },
    [findActive, kickTicker],
  );

  const cancel = useCallback(
    async (jobId: string): Promise<void> => {
      const job = stateRef.current.jobs.find((j) => j.id === jobId);
      if (!job || !isActiveStatus(job.status)) return;
      dispatch({ type: 'CANCEL_REQUESTED', id: jobId });
      try {
        const res =
          job.origin === 'local'
            ? await api.localGenerate.cancelJob(jobId)
            : await api.jobs.cancel(jobId);
        if (res.status === 'cancelled') {
          dispatch({ type: 'UPDATE', id: jobId, patch: { status: 'cancelled', completedAt: Date.now() } });
        } else {
          kickTicker(); // running → CancelRequested; the poll flips it to cancelled
        }
      } catch (e) {
        if (isAuthError(e)) dispatch({ type: 'CLEAR_ALL' });
        // otherwise leave it; the poll loop reflects the real state
      }
    },
    [kickTicker],
  );

  const retry = useCallback(
    async (jobId: string): Promise<string | null> => {
      const job = stateRef.current.jobs.find((j) => j.id === jobId);
      if (!job || !(job.status === 'error' || job.status === 'cancelled')) return null;

      if (job.origin === 'hosted') {
        const { jobId: newId } = await api.jobs.retry(jobId);
        dispatch({
          type: 'ADD',
          job: {
            id: newId,
            origin: 'hosted',
            kind: job.kind,
            mode: job.mode,
            reportId: job.reportId,
            report: job.report,
            status: 'queued',
            createdAt: Date.now(),
            attempt: (job.attempt ?? 1) + 1,
            retryOfJobId: jobId,
            dismissed: false,
            seen: false,
            notified: false,
          },
        });
        kickTicker();
        return newId;
      }

      // Local jobs have no hosted retry endpoint — re-submit client-side using
      // the study-context spec captured at submit (in memory only).
      const spec = localSpecs.current.get(jobId);
      if (!spec || spec.origin !== 'local') return null;
      const { jobId: newId } = await api.localGenerate.submitJob({
        ...spec.dto,
        correlationId: spec.reportId,
      });
      localSpecs.current.set(newId, spec);
      dispatch({
        type: 'ADD',
        job: {
          id: newId,
          origin: 'local',
          kind: 'local-generate',
          mode: spec.mode ?? 'report',
          reportId: spec.reportId,
          report: spec.report,
          status: 'queued',
          createdAt: Date.now(),
          attempt: (job.attempt ?? 1) + 1,
          retryOfJobId: jobId,
          dismissed: false,
          seen: false,
          notified: false,
        },
      });
      kickTicker();
      return newId;
    },
    [kickTicker],
  );

  const dismiss = useCallback((jobId: string) => dispatch({ type: 'DISMISS', id: jobId }), []);
  const clearFinished = useCallback(() => dispatch({ type: 'CLEAR_FINISHED' }), []);
  const markSeen = useCallback(() => dispatch({ type: 'MARK_SEEN' }), []);
  const markApplied = useCallback((jobId: string) => dispatch({ type: 'MARK_APPLIED', id: jobId }), []);

  const canRetry = useCallback((job: Job): boolean => {
    if (job.status !== 'error' && job.status !== 'cancelled') return false;
    return job.origin === 'hosted' || localSpecs.current.has(job.id);
  }, []);

  const value = useMemo<JobsContextValue>(
    () => ({
      jobs: visibleJobs(state),
      submit,
      cancel,
      retry,
      dismiss,
      clearFinished,
      markSeen,
      markApplied,
      canRetry,
    }),
    [state, submit, cancel, retry, dismiss, clearFinished, markSeen, markApplied, canRetry],
  );

  return (
    <JobsContext.Provider value={value}>
      {children}
      {/* Announces terminal transitions to assistive tech. */}
      <div className="rp-sr-only" aria-live="polite" role="status">
        {announcement}
      </div>
    </JobsContext.Provider>
  );
}

// ---------------------------------------------------------------------------
// Hosted `JobDetail` → patch (kept local: it needs the api `JobDetail` shape)
// ---------------------------------------------------------------------------
function detailPatch(
  job: Job,
  detail: Awaited<ReturnType<typeof api.jobs.get>>,
): Partial<Job> {
  const patch: Partial<Job> = { status: detail.status };
  if (detail.error != null) patch.error = detail.error;
  if (detail.errorKind != null) patch.errorKind = detail.errorKind;
  if (detail.report) {
    patch.report = {
      accession: detail.report.accession,
      modality: detail.report.modality,
      bodyPart: detail.report.bodyPart,
    };
  }
  if (detail.startedAt) patch.startedAt = Date.parse(detail.startedAt);
  else if (job.startedAt == null && detail.elapsedMs) patch.startedAt = Date.now() - detail.elapsedMs;
  if (detail.completedAt) patch.completedAt = Date.parse(detail.completedAt);
  else if (isTerminalStatus(detail.status) && job.completedAt == null) patch.completedAt = Date.now();
  return patch;
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

export function useJobs(): JobsContextValue {
  const ctx = useContext(JobsContext);
  if (!ctx) throw new Error('useJobs must be used within <JobsProvider>');
  return ctx;
}

/** Jobs for one report (the report-page wave consumes this). */
export function useJobsForReport(reportId: string): Job[] {
  const { jobs } = useJobs();
  return useMemo(() => jobs.filter((j) => j.reportId === reportId), [jobs, reportId]);
}
