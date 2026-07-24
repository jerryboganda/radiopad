'use client';

/**
 * Top-right async-AI-job status widget (Phase 5). A non-blocking icon button +
 * count badge with a popover listing every tracked job (hosted + local sidecar),
 * with per-status actions (Cancel / Retry / Open report / Dismiss) and a
 * "Clear finished" footer.
 *
 * Desktop surface only. The outer component early-returns before any hook so the
 * web/mobile bundles (which have no `JobsProvider`) never call `useJobs()`.
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { isDesktopSurface } from '@/lib/surface';
import EmptyState from '@/components/ui/EmptyState';
import { describeAiError } from '@/lib/aiErrors';
import { useJobs } from './JobsProvider';
import {
  formatElapsed,
  isActiveStatus,
  isTerminalStatus,
  jobDescriptor,
  jobKindLabel,
  openReportHref,
  stageLabel,
  type Job,
} from '@/lib/jobs';
import { useRouter } from 'next/navigation';

export default function JobsIndicator() {
  if (!isDesktopSurface) return null;
  return <JobsIndicatorInner />;
}

/** Re-render on a 1s cadence while there is active work AND the popover is open,
 *  so the running-job timers tick without spinning up a timer per row. */
function useTickWhile(enabled: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!enabled) return;
    setNow(Date.now());
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [enabled]);
  return now;
}

function JobsIndicatorInner() {
  const { jobs, cancel, retry, dismiss, clearFinished, markSeen, canRetry } = useJobs();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement | null>(null);

  const activeCount = useMemo(() => jobs.filter((j) => isActiveStatus(j.status)).length, [jobs]);
  const terminal = useMemo(() => jobs.filter((j) => isTerminalStatus(j.status)), [jobs]);
  const unseenTerminal = useMemo(() => terminal.filter((j) => !j.seen), [terminal]);
  const hasUnseenError = unseenTerminal.some((j) => j.status === 'error');

  const now = useTickWhile(open && activeCount > 0);

  // Outside-click + Esc to close (pattern lifted from NotificationsBell).
  useEffect(() => {
    if (!open) return;
    markSeen();
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onEsc);
    return () => {
      window.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onEsc);
    };
  }, [open, markSeen]);

  // Nothing to show and nothing open → render nothing at all.
  if (jobs.length === 0 && !open) return null;

  const badgeCount = activeCount > 0 ? activeCount : unseenTerminal.length;
  const badgeTone =
    activeCount > 0 ? 'active' : hasUnseenError ? 'danger' : 'success';
  const label = `AI jobs — ${activeCount} running, ${terminal.length} finished`;

  const openReport = (job: Job) => {
    setOpen(false);
    router.push(openReportHref(job));
  };

  return (
    <div className="rp-jobs" ref={ref}>
      <button
        type="button"
        className={`rp-topbar-iconbtn rp-jobs-btn${activeCount > 0 ? ' is-active' : ''}`}
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <Sparkles size={16} aria-hidden />
        {activeCount > 0 && <span className="rp-jobs-ring" aria-hidden />}
        {badgeCount > 0 && (
          <span className={`rp-jobs-badge tone-${badgeTone}`} aria-hidden>
            {badgeCount > 9 ? '9+' : badgeCount}
          </span>
        )}
      </button>

      {open && (
        <div className="rp-jobs-popover" role="dialog" aria-label="AI jobs">
          <div className="rp-jobs-popover-title">AI jobs</div>
          {jobs.length === 0 ? (
            <EmptyState title="No AI jobs" description="Generation jobs you start will show up here." />
          ) : (
            <>
              <div className="rp-jobs-list">
                {jobs.map((job) => (
                  <JobRow
                    key={job.id}
                    job={job}
                    now={now}
                    canRetry={canRetry(job)}
                    onCancel={() => void cancel(job.id)}
                    onRetry={() => void retry(job.id)}
                    onDismiss={() => dismiss(job.id)}
                    onOpen={() => openReport(job)}
                  />
                ))}
              </div>
              {terminal.length > 0 && (
                <div className="rp-jobs-footer">
                  <button type="button" className="subtle" onClick={clearFinished}>
                    Clear finished
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function toneForStatus(status: Job['status']): 'success' | 'danger' | 'warning' | 'info' {
  switch (status) {
    case 'ok':
      return 'success';
    case 'error':
      return 'danger';
    case 'cancelled':
      return 'warning';
    default:
      return 'info';
  }
}

function statusText(job: Job): string {
  switch (job.status) {
    case 'queued':
      return job.cancelRequested ? 'Cancelling…' : 'Queued';
    case 'running':
      return job.cancelRequested ? 'Cancelling…' : 'Running';
    case 'ok':
      return 'Ready';
    case 'error':
      return 'Failed';
    case 'cancelled':
      return 'Cancelled';
    default:
      return job.status;
  }
}

function JobRow({
  job,
  now,
  canRetry,
  onCancel,
  onRetry,
  onDismiss,
  onOpen,
}: {
  job: Job;
  now: number;
  canRetry: boolean;
  onCancel: () => void;
  onRetry: () => void;
  onDismiss: () => void;
  onOpen: () => void;
}) {
  const active = isActiveStatus(job.status);
  const tone = toneForStatus(job.status);
  const stage = job.origin === 'local' ? stageLabel(job.stage) : null;
  const elapsed = active ? formatElapsed(now - (job.startedAt ?? job.createdAt)) : null;
  // Determinate only with a real ratio; the coordinator sends null in v1 (a token
  // ceiling is not a target — design §3.10 bans faked percentages), so this is
  // null for every real job today and the bar renders indeterminate. Never invent one.
  const pct =
    job.progress?.percent != null
      ? Math.min(100, Math.max(0, Math.round(job.progress.percent)))
      : null;

  let sub: string | null;
  if (job.status === 'error') sub = describeAiError(job.errorKind, job.error);
  else if (stage) sub = stage;
  else sub = jobDescriptor(job);

  return (
    <div className={`rp-jobs-item tone-${tone}`}>
      <div className="rp-jobs-item-head">
        <span className="rp-jobs-item-title">
          {active &&
            (job.cancelRequested ? (
              <span className="rp-jobs-dot" aria-hidden />
            ) : (
              <span className="rp-jobs-spinner" aria-hidden />
            ))}
          {jobKindLabel(job)}
        </span>
        <span className={`rp-jobs-status tone-${tone}`}>{statusText(job)}</span>
      </div>
      {sub && <span className="rp-jobs-item-sub">{sub}</span>}
      {active && (
        <div
          className="rp-progress rp-jobs-progress"
          role="progressbar"
          aria-label={`${jobKindLabel(job)} progress`}
          aria-valuenow={pct ?? undefined}
          aria-valuemin={pct != null ? 0 : undefined}
          aria-valuemax={pct != null ? 100 : undefined}
          data-indeterminate={pct != null ? undefined : 'true'}
        >
          <span
            className="rp-progress-fill"
            style={pct != null ? { width: `${pct}%` } : undefined}
          />
        </div>
      )}
      <div className="rp-jobs-item-foot">
        {elapsed && <span className="rp-jobs-item-meta">{elapsed}</span>}
        {job.progress?.tokens ? (
          <span className="rp-jobs-tokens">~{job.progress.tokens} tokens</span>
        ) : null}
        <div className="rp-jobs-item-actions">
          {active && !job.cancelRequested && !job.sync && (
            <button type="button" className="subtle" onClick={onCancel}>
              Cancel
            </button>
          )}
          {(job.status === 'error' || job.status === 'cancelled') && canRetry && (
            <button type="button" className="primary-ghost" onClick={onRetry}>
              Retry
            </button>
          )}
          {job.status === 'cancelled' && (
            <button type="button" className="subtle" onClick={onDismiss}>
              Dismiss
            </button>
          )}
          {isTerminalStatus(job.status) && job.status !== 'cancelled' && (
            <button type="button" className="primary-ghost" onClick={onOpen}>
              Open report
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
