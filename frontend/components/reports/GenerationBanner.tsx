'use client';

// Slim, non-modal, dismissible banner shown at the top of the report editor
// while a whole-report generation for THIS report is still running in the
// background (Phase 6.2). Unlike the old full-surface GenerationOverlay it does
// NOT block interaction — the radiologist can keep reading/editing while the
// draft is produced (submitted from the New Report wizard, from the AI bar, or
// from another surface). It reads the same tracked `Job` the topbar widget does,
// so the two can never disagree.
//
// RC tokens only (`.banner ai` → --ai-bg/--ai-border/--ai; --mono; --text-muted)
// so both themes render correctly with no hardcoded colour.

import { useEffect, useState, type ReactNode } from 'react';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { formatElapsed, isActiveStatus, stageLabel, type Job } from '@/lib/jobs';

export interface GenerationBannerProps {
  /** The active (non-terminal) generate / local-generate job for this report. */
  job: Job;
  onDismiss?: () => void;
  /**
   * Optional stop control. When supplied and the job is still active (and no
   * cancel has already been requested) a "Stop" button is shown; the caller
   * wires it to `jobs.cancel(job.id)` (a future PR). Absent while a cancel is
   * already in flight.
   */
  onStop?: () => void;
  /**
   * Optional live-output node revealed behind an opt-in "Show live output"
   * toggle. Collapsed by default so the banner stays slim; FE-PR4 fills this
   * with the streamed `AiStreamPreview`. This PR only builds the collapsible
   * mechanism + prop plumbing.
   */
  previewSlot?: ReactNode;
}

export default function GenerationBanner({ job, onDismiss, onStop, previewSlot }: GenerationBannerProps) {
  // Tick once a second so the elapsed timer stays live. Keyed off nothing but
  // the wall clock — the banner unmounts the moment the job goes terminal.
  const [now, setNow] = useState(() => Date.now());
  const [showPreview, setShowPreview] = useState(false);
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, []);

  const started = job.startedAt ?? job.createdAt;
  const elapsed = Math.max(0, now - started);
  const stage = job.origin === 'local' ? stageLabel(job.stage) : null;
  const showStop = !!onStop && isActiveStatus(job.status) && !job.cancelRequested;

  return (
    <div className="banner ai rp-genbanner" role="status" data-testid="generation-banner">
      <span className="rp-spinner sm" aria-hidden />
      <span className="rp-genbanner-label">Generating draft…</span>
      {stage && <span className="rp-genbanner-stage">{stage}</span>}
      <span className="rp-genbanner-timer" aria-label="Elapsed time">
        {formatElapsed(elapsed)}
      </span>
      {job.progress?.tokens ? (
        <span className="rp-genbanner-tokens">~{job.progress.tokens} tokens</span>
      ) : null}
      {showStop && (
        <button type="button" className="ghost rp-genbanner-stop" onClick={onStop}>
          Stop
        </button>
      )}
      {previewSlot && (
        <button
          type="button"
          className="ghost rp-genbanner-preview-toggle"
          aria-expanded={showPreview}
          onClick={() => setShowPreview((v) => !v)}
        >
          {showPreview ? 'Hide live output' : 'Show live output'}
          {showPreview ? <ChevronUp size={13} aria-hidden /> : <ChevronDown size={13} aria-hidden />}
        </button>
      )}
      {onDismiss && (
        <button type="button" className="ghost rp-genbanner-dismiss" onClick={onDismiss}>
          Dismiss
        </button>
      )}
      {previewSlot && showPreview && <div className="rp-genbanner-preview">{previewSlot}</div>}
    </div>
  );
}
