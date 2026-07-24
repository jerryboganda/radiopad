/**
 * Durable async-AI-job model shared by the global `JobsProvider` and the
 * top-right `JobsIndicator` (Phase 4.2 / 5 of the async-job platform).
 *
 * This module is deliberately React-free: it holds the `Job` shape, the pure
 * `jobsReducer` (unit-tested directly), and the small mappers/label helpers the
 * provider and widget both use. All network I/O and side effects live in
 * `components/jobs/JobsProvider.tsx`.
 *
 * Clinical-safety note: nothing here (and nothing the provider persists) ever
 * stores a job's `result` payload or any clinical text. The localStorage seed is
 * metadata-only (`{id, reportId, kind, createdAt}`) so the widget can paint a
 * placeholder row on reload before the network list resolves.
 */

import type {
  AiJobEnvelope,
  JobKind,
  JobStatus,
  JobSummary,
  LocalGenerateDto,
} from './api';
import { reportHref } from './routes';

/** Which engine a tracked job runs on. `hosted` = the tenant API job engine
 *  (`api.jobs.*` / report-scoped poll); `local` = the desktop sidecar. */
export type JobOrigin = 'hosted' | 'local';

/** The non-PHI report descriptor a row renders. Narrower than the wire
 *  `JobReportDescriptor` (drops `status`) — modality/bodyPart drive the label,
 *  accession is retained only for display, never emitted to OS notifications. */
export interface JobReportInfo {
  accession: string;
  modality: string;
  bodyPart: string;
}

/** One tracked generation attempt. `id` is always the server-assigned job id
 *  (hosted API or sidecar) — the provider awaits submit before adding, so there
 *  are no client-only ids to reconcile. */
export interface Job {
  id: string;
  origin: JobOrigin;
  kind: JobKind;
  /** AI mode for `ai` jobs; `"generate"` / `"report"` for the whole-report kinds. */
  mode: string;
  /** The HOSTED report id (for local jobs this is the submit `correlationId`). */
  reportId: string;
  report?: JobReportInfo;
  status: JobStatus;
  /** Local-origin only — sidecar pipeline stage. */
  stage?: 'queued' | 'model-loading' | 'generating';
  /** Live progress from the registry/bus (active jobs only; never persisted to
   *  localStorage). `percent` is present ONLY when the server computed a real
   *  ratio (design §3.10 — indeterminate on every current path); never faked. */
  progress?: { tokens: number; percent?: number };
  createdAt: number;
  startedAt?: number;
  completedAt?: number;
  error?: string;
  /** Backend errorKind, plus the client-synthesised `lost` / `sidecar_restart`. */
  errorKind?: string;
  attempt: number;
  retryOfJobId?: string;
  /** The result was applied to the report (suppresses the `?aiJob=`/`?localJob=` hint). */
  applied?: boolean;
  /** User hid this (terminal) row / cleared finished. */
  dismissed: boolean;
  /** The popover has shown this terminal job (clears the unseen badge tone). */
  seen: boolean;
  /** Terminal side effects (toast / notify / event) already fired once. */
  notified: boolean;
  /** A cancel was requested; hides the Cancel button until the poll confirms. */
  cancelRequested?: boolean;
  /**
   * Client-side in-flight work with NO server job id to poll (Rewrite/Regenerate:
   * one awaited request). Tracked so the indicator animates while it runs, but
   * excluded from the poll ticker, Cancel, and Retry — there is nothing to
   * poll, cancel, or resubmit.
   */
  sync?: boolean;
}

/**
 * The argument to `JobsProvider.submit()`. A discriminated union over the three
 * generation paths. **This is the contract the trigger-site refactor (Phase 6)
 * depends on** — keep it stable.
 */
export type JobSubmitSpec =
  | {
      origin: 'hosted';
      kind: 'ai';
      reportId: string;
      mode: string;
      providerId?: string;
      /** For `mode: 'cleanup'` only — the assembled raw dictation (the five
       *  sections joined). When present the provider routes the submit to the
       *  durable cleanup endpoint (`/dictation/cleanup/jobs`, structured
       *  `cleanedSections` result) instead of the generic `/ai/jobs` path, while
       *  the job is still tracked/deduped as a normal hosted `ai`/`cleanup` job.
       *  Held in memory only — never persisted (no clinical text at rest). */
      rawDictation?: string;
      report?: JobReportInfo;
    }
  | {
      origin: 'hosted';
      kind: 'generate';
      reportId: string;
      providerId?: string;
      report?: JobReportInfo;
    }
  | {
      origin: 'local';
      kind: 'local-generate';
      /** The hosted report id — becomes the sidecar `correlationId`. */
      reportId: string;
      /** Study context for the sidecar. Held in memory only (never persisted)
       *  so a client-side retry can re-submit it. */
      dto: LocalGenerateDto;
      mode?: string;
      report?: JobReportInfo;
    };

/** The value exposed by `useJobs()`. */
export interface JobsContextValue {
  /** Visible jobs (non-dismissed), newest first. */
  jobs: Job[];
  submit: (spec: JobSubmitSpec) => Promise<string>;
  /** Track a job created OUTSIDE `submit()` (e.g. the Cross Check audio half,
   *  submitted via the sidecar multipart, and its hosted review half). The generic
   *  seam for externally-submitted work: it dispatches the same `ADD` action and
   *  kicks the shared ticker, defaulting the client lifecycle flags exactly as the
   *  reducer does for every other path. */
  trackExternal: (job: Omit<Job, 'dismissed' | 'seen' | 'notified' | 'attempt'> & { attempt?: number }) => void;
  /**
   * Start tracking an AI action that has NO server-side job to poll —
   * Rewrite/Regenerate runs as one awaited request/response, not a durable job
   * id. Adds a `running` row (flagged `sync`, so the poll ticker, Cancel, and
   * Retry all skip it) purely so the indicator animates and the panel shows the
   * work while it is in flight. Returns the client job id — pass it to
   * `settleSync` in a `finally` so a row can never be stranded as "running".
   */
  beginSync: (spec: { mode: string; reportId: string; report?: JobReportInfo }) => string;
  /**
   * Settle a `beginSync` row into its terminal state, firing the same toast +
   * notification-bell + AI-jobs-panel treatment as an async job finishing.
   */
  settleSync: (
    jobId: string,
    result: { status: 'ok' | 'error'; error?: string; errorKind?: string },
  ) => void;
  /**
   * One-shot convenience for an action that already finished: adds the row
   * directly in its terminal state (never `queued`/`running`). Equivalent to a
   * `beginSync` + immediate `settleSync`.
   */
  logSyncResult: (spec: {
    mode: string;
    reportId: string;
    report?: JobReportInfo;
    status: 'ok' | 'error';
    error?: string;
    errorKind?: string;
  }) => void;
  cancel: (jobId: string) => Promise<void>;
  retry: (jobId: string) => Promise<string | null>;
  dismiss: (jobId: string) => void;
  clearFinished: () => void;
  markSeen: () => void;
  markApplied: (jobId: string) => void;
  /** Whether the widget should offer a Retry for this (terminal) job. */
  canRetry: (job: Job) => boolean;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** localStorage key for the metadata-only local-job seed (ids + timestamps). */
export const JOBS_STORAGE_KEY = 'rp.jobs.v1';

/** Poll cadence — mirrors the (module-private) `api.ts` constants: a fast first
 *  poll, ramping toward the 2s steady cadence, widened to a 5s floor while the
 *  tab is hidden. */
export const JOB_FIRST_POLL_MS = 300;
export const JOB_POLL_MS = 2_000;
export const JOB_HIDDEN_POLL_MS = 5_000;

// ---------------------------------------------------------------------------
// Status helpers
// ---------------------------------------------------------------------------

export function isTerminalStatus(status: JobStatus): boolean {
  return status === 'ok' || status === 'error' || status === 'cancelled';
}

export function isActiveStatus(status: JobStatus): boolean {
  return status === 'queued' || status === 'running';
}

/** Next poll delay for the shared ticker: double the previous, clamped to the
 *  steady 2s cadence — widened to a 5s floor while the tab is hidden. */
export function nextPollDelay(prevMs: number, hidden: boolean): number {
  const cap = hidden ? JOB_HIDDEN_POLL_MS : JOB_POLL_MS;
  return Math.min(prevMs * 2, cap);
}

/** The single-flight identity of a logical submission. */
export function dedupeKey(reportId: string, kind: JobKind, mode: string): string {
  return `${reportId}::${kind}::${mode}`;
}

/** The effective `mode` for a spec (generate/local kinds have a fixed mode). */
export function specMode(spec: JobSubmitSpec): string {
  if (spec.kind === 'generate') return 'generate';
  if (spec.kind === 'local-generate') return spec.mode ?? 'report';
  return spec.mode;
}

// ---------------------------------------------------------------------------
// Labels (inline English — matches the sibling copy modules aiErrors.ts /
// GenerationOverlay.tsx, which are not routed through next-intl)
// ---------------------------------------------------------------------------

/** Human label for a job's kind+mode: "Draft generation" / "Impression" / … */
export function jobKindLabel(job: Pick<Job, 'kind' | 'mode'> & { origin?: JobOrigin }): string {
  if (job.kind === 'generate') return 'Draft generation';
  if (job.kind === 'local-generate') return 'Local draft (MedGemma)';
  // Cross-check is a first-class kind (durable async-job platform): the audio half
  // runs locally on the sidecar, the medical review half runs hosted. Label by
  // ORIGIN — the widget then reads "Cross-check (audio)" vs "Cross-check (review)".
  if (job.kind === 'crosscheck') {
    return job.origin === 'local' ? 'Cross-check (audio)' : 'Cross-check (review)';
  }
  // kind === 'ai'
  switch (job.mode) {
    case 'impression':
      return 'Impression';
    case 'rewrite':
      return 'Rewrite';
    case 'cleanup':
      return 'Dictation cleanup';
    default:
      return job.mode
        ? `AI · ${job.mode.charAt(0).toUpperCase()}${job.mode.slice(1)}`
        : 'AI generation';
  }
}

/** "CT · Chest" when a descriptor is known, else the kind label. */
export function jobDescriptor(job: Job): string {
  const r = job.report;
  if (r && (r.modality || r.bodyPart)) {
    return [r.modality, r.bodyPart].filter(Boolean).join(' · ');
  }
  return jobKindLabel(job);
}

/** Sidecar stage → user copy (local jobs only). */
export function stageLabel(stage: Job['stage']): string | null {
  switch (stage) {
    case 'queued':
      return 'Queued behind another local job';
    case 'model-loading':
      return 'Loading model…';
    case 'generating':
      return 'Generating…';
    default:
      return null;
  }
}

/**
 * "Open report" destination for a finished job. Carries the apply-hint param a
 * later report-page wave consumes: `?aiJob=` for an unapplied `ai` result, or
 * `?localJob=` for an unapplied local draft. `reportHref` already emits
 * `?id=<id>`, so these append with `&`.
 */
export function openReportHref(job: Job): string {
  let href = reportHref(job.reportId);
  if (!job.applied) {
    // `?aiJob=` opens the report-page apply flow for an `ai` result AND for a
    // hosted `crosscheck` review job (its corrections populate the panel). The
    // local `crosscheck` audio half carries NO deep-link — its result is only an
    // input to the review half, consumed in-session (marked applied).
    if (job.kind === 'ai' || (job.kind === 'crosscheck' && job.origin === 'hosted')) {
      href += `&aiJob=${encodeURIComponent(job.id)}`;
    } else if (job.origin === 'local' && job.kind === 'local-generate') {
      href += `&localJob=${encodeURIComponent(job.id)}`;
    }
  }
  return href;
}

/** mm:ss elapsed formatter (lifted from GenerationOverlay so both surfaces
 *  format the running timer identically). */
export function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

/**
 * Clinical-safety gate for the report-page apply flow (Phase 6.1). Decides
 * whether an incoming AI result may overwrite the editor field WITHOUT a manual
 * confirm. `before` is the field's text captured at submit time; it is
 * `undefined` when the job was submitted from another session (the widget's
 * `?aiJob=` deep-link opened the report fresh).
 *
 * - With a submit snapshot: only auto-apply when the field is byte-for-byte
 *   unchanged since submit — any keystroke under it forces the non-destructive
 *   preview instead (never clobber in-progress typing; mirrors the never-auto
 *   culture behind `.ai-mark`).
 * - Without a snapshot: only auto-apply into a still-empty field; a populated
 *   field means there is work to protect, so route to the preview.
 */
export function canAutoApplyAiResult(current: string, before: string | undefined): boolean {
  if (before === undefined) return (current ?? '').trim().length === 0;
  return (current ?? '') === before;
}

// ---------------------------------------------------------------------------
// Mappers (wire → Job)
// ---------------------------------------------------------------------------

/** Map a `JobSummary` (rehydration list) to a tracked `Job`. Terminal rows that
 *  arrive already-finished are marked `notified` so we don't fire a stale toast
 *  for something that completed before this page load. */
export function summaryToJob(summary: JobSummary, origin: JobOrigin): Job {
  const terminal = isTerminalStatus(summary.status);
  return {
    id: summary.jobId,
    origin,
    kind: summary.kind,
    mode: summary.mode,
    reportId: summary.reportId,
    report: summary.report
      ? {
          accession: summary.report.accession,
          modality: summary.report.modality,
          bodyPart: summary.report.bodyPart,
        }
      : undefined,
    status: summary.status,
    createdAt: summary.createdAt ? Date.parse(summary.createdAt) : Date.now(),
    startedAt: summary.startedAt ? Date.parse(summary.startedAt) : undefined,
    completedAt: summary.completedAt ? Date.parse(summary.completedAt) : undefined,
    error: summary.error ?? undefined,
    errorKind: summary.errorKind ?? undefined,
    attempt: summary.attempt ?? 1,
    retryOfJobId: summary.retryOfJobId ?? undefined,
    progress:
      summary.progress && !terminal
        ? { tokens: summary.progress.tokens, percent: summary.progress.percent ?? undefined }
        : undefined,
    dismissed: false,
    seen: false,
    notified: terminal,
  };
}

/** The metadata-only seed persisted for local-origin jobs. */
export interface LocalJobSeed {
  id: string;
  reportId: string;
  kind: JobKind;
  createdAt: number;
}

/** Paint a placeholder row from a localStorage seed before the network resolves.
 *  Optimistically `running` so the badge shows it as active; the first poll then
 *  replaces the status (or marks it `sidecar_restart` if the sidecar is gone). */
export function seedToJob(seed: LocalJobSeed): Job {
  return {
    id: seed.id,
    origin: 'local',
    kind: seed.kind,
    mode: 'report',
    reportId: seed.reportId,
    status: 'running',
    createdAt: seed.createdAt,
    attempt: 1,
    dismissed: false,
    seen: false,
    notified: false,
  };
}

/** Build the poll patch from a status envelope (report-scoped or sidecar poll). */
export function envelopePatch<T>(
  job: Job,
  env: AiJobEnvelope<T> & { stage?: Job['stage'] },
): Partial<Job> {
  const patch: Partial<Job> = { status: env.status };
  if (env.error != null) patch.error = env.error;
  if (env.errorKind != null) patch.errorKind = env.errorKind;
  if (job.origin === 'local' && env.stage) patch.stage = env.stage;
  if (job.startedAt == null && (env.status === 'running' || isTerminalStatus(env.status))) {
    patch.startedAt = Date.now() - (env.elapsedMs || 0);
  }
  if (isTerminalStatus(env.status) && job.completedAt == null) {
    patch.completedAt = Date.now();
  }
  if (env.progress && isActiveStatus(env.status)) {
    patch.progress = { tokens: env.progress.tokens, percent: env.progress.percent ?? undefined };
  }
  return patch;
}

/**
 * Patch from a bus `progress` event (durable async-job platform). The caller
 * (JobsProvider) checks the job is known + active before dispatching; the
 * reducer's first-terminal-wins guard is the backstop that makes a late or
 * duplicate progress patch on an already-terminal job a no-op.
 */
export function progressPatch(ev: { tokens: number; percent?: number | null }): Partial<Job> {
  return { progress: { tokens: ev.tokens, percent: ev.percent ?? undefined } };
}

/**
 * Build an UPDATE patch from a `JobSummary` — the shape of both the unified
 * `GET /api/jobs/{id}` detail (a superset) and a bus `job` event. Carries only
 * server-owned fields; the reducer preserves the client lifecycle flags
 * (seen / notified / dismissed / applied) and its first-terminal-wins guard
 * drops a stray patch on a settled job. `job` supplies the fallbacks for a
 * backend that omits `startedAt` / `completedAt`.
 */
export function summaryPatch(job: Job, s: JobSummary): Partial<Job> {
  const patch: Partial<Job> = { status: s.status };
  if (s.error != null) patch.error = s.error;
  if (s.errorKind != null) patch.errorKind = s.errorKind;
  if (s.report) {
    patch.report = {
      accession: s.report.accession,
      modality: s.report.modality,
      bodyPart: s.report.bodyPart,
    };
  }
  if (s.startedAt) patch.startedAt = Date.parse(s.startedAt);
  else if (job.startedAt == null && s.elapsedMs) patch.startedAt = Date.now() - s.elapsedMs;
  if (s.completedAt) patch.completedAt = Date.parse(s.completedAt);
  else if (isTerminalStatus(s.status) && job.completedAt == null) patch.completedAt = Date.now();
  if (s.progress && isActiveStatus(s.status)) {
    patch.progress = { tokens: s.progress.tokens, percent: s.progress.percent ?? undefined };
  }
  return patch;
}

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

export interface JobsState {
  jobs: Job[];
}

export type JobsAction =
  | { type: 'HYDRATE'; jobs: Job[] }
  | { type: 'ADD'; job: Job }
  | { type: 'UPDATE'; id: string; patch: Partial<Job> }
  | { type: 'CANCEL_REQUESTED'; id: string }
  | { type: 'MARK_NOTIFIED'; id: string }
  | { type: 'MARK_SEEN' }
  | { type: 'MARK_APPLIED'; id: string }
  | { type: 'DISMISS'; id: string }
  | { type: 'CLEAR_FINISHED' }
  | { type: 'CLEAR_ALL' };

export function initialJobsState(): JobsState {
  return { jobs: [] };
}

/** Merge one incoming (hydrated) job into an existing one, preserving the
 *  client-only lifecycle flags (seen / notified / dismissed / applied). Server
 *  fields (status / report / timestamps) win. */
function mergeHydrated(existing: Job, incoming: Job): Job {
  return {
    ...existing,
    ...incoming,
    report: incoming.report ?? existing.report,
    startedAt: incoming.startedAt ?? existing.startedAt,
    completedAt: incoming.completedAt ?? existing.completedAt,
    error: incoming.error ?? existing.error,
    errorKind: incoming.errorKind ?? existing.errorKind,
    // Preserve local lifecycle state so a reload doesn't re-toast / un-dismiss.
    seen: existing.seen,
    notified: existing.notified || incoming.notified,
    dismissed: existing.dismissed,
    applied: existing.applied ?? incoming.applied,
    cancelRequested: existing.cancelRequested,
  };
}

export function jobsReducer(state: JobsState, action: JobsAction): JobsState {
  switch (action.type) {
    case 'HYDRATE': {
      const byId = new Map(state.jobs.map((j) => [j.id, j] as const));
      for (const incoming of action.jobs) {
        const prev = byId.get(incoming.id);
        byId.set(incoming.id, prev ? mergeHydrated(prev, incoming) : incoming);
      }
      return { jobs: Array.from(byId.values()) };
    }
    case 'ADD': {
      // Idempotent by id — the server single-flights concurrent submits to one
      // jobId, so two racing submits collapse to a single row here.
      if (state.jobs.some((j) => j.id === action.job.id)) {
        return {
          jobs: state.jobs.map((j) =>
            j.id === action.job.id ? { ...j, ...action.job } : j,
          ),
        };
      }
      return { jobs: [...state.jobs, action.job] };
    }
    case 'UPDATE': {
      return {
        jobs: state.jobs.map((j) => {
          if (j.id !== action.id) return j;
          // First terminal outcome wins — never move a settled job.
          if (isTerminalStatus(j.status)) return j;
          return { ...j, ...action.patch };
        }),
      };
    }
    case 'CANCEL_REQUESTED': {
      return {
        jobs: state.jobs.map((j) =>
          j.id === action.id && isActiveStatus(j.status)
            ? { ...j, cancelRequested: true }
            : j,
        ),
      };
    }
    case 'MARK_NOTIFIED': {
      return {
        jobs: state.jobs.map((j) => (j.id === action.id ? { ...j, notified: true } : j)),
      };
    }
    case 'MARK_SEEN': {
      return {
        jobs: state.jobs.map((j) =>
          isTerminalStatus(j.status) && !j.seen ? { ...j, seen: true } : j,
        ),
      };
    }
    case 'MARK_APPLIED': {
      return {
        jobs: state.jobs.map((j) => (j.id === action.id ? { ...j, applied: true } : j)),
      };
    }
    case 'DISMISS': {
      return {
        jobs: state.jobs.map((j) => (j.id === action.id ? { ...j, dismissed: true } : j)),
      };
    }
    case 'CLEAR_FINISHED': {
      return {
        jobs: state.jobs.map((j) =>
          isTerminalStatus(j.status) ? { ...j, dismissed: true } : j,
        ),
      };
    }
    case 'CLEAR_ALL': {
      return { jobs: [] };
    }
    default:
      return state;
  }
}

/** The visible, newest-first list derived from state (non-dismissed). */
export function visibleJobs(state: JobsState): Job[] {
  return state.jobs
    .filter((j) => !j.dismissed)
    .sort((a, b) => b.createdAt - a.createdAt);
}
