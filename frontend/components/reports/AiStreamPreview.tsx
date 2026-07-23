'use client';

/**
 * AI-013 live token-stream preview (FE-PR4). A small, ephemeral panel that shows
 * the streaming *tail* of a running AI job's output while it generates, with a
 * Stop control (while active) and a Regenerate control (terminal error/cancelled).
 *
 * Ephemeral by construction: the streamed text is read ONLY for display, straight
 * from the off-reducer {@link jobPartials} store via {@link useJobPartial}. It is
 * NEVER written into `report` state, `update(...)`, or any persisted field — the
 * sanctioned apply paths in ReportClient (`applyJobResult` → `canAutoApplyAiResult`)
 * are the only way streamed text ever reaches the report.
 *
 * AI treatment: the panel wears the AI-blue family — the same `--ai` tokens as
 * `.ai-mark` — and a reused `StatusBadge tone="ai"` label, rather than the literal
 * `.ai-mark` class. `.ai-mark::before` prints a "generated — review required"
 * caption that would contradict a preview explicitly labelled "not yet applied";
 * the badge copy ("✨ Live preview", the established sparkle prefix used by
 * `.ai-mark` and the pending-result badge) plus the "Not yet applied" note carry
 * the meaning, so nothing is conveyed by colour alone (NOTIF-002).
 *
 * Screen-reader note: the streamed body is `aria-hidden`. JobsProvider already
 * runs a single throttled `rp-sr-only` live region for job progress; a second,
 * raw-token live region here would violate the rate-limited-announcement rule.
 */

import { useLayoutEffect, useRef } from 'react';
import StatusBadge from '@/components/ui/StatusBadge';
import { useJobs } from '@/components/jobs/JobsProvider';
import { useJobPartial } from '@/components/jobs/useJobPartial';
import { isActiveStatus, type Job } from '@/lib/jobs';

export interface AiStreamPreviewProps {
  jobId: string;
  /** The tracked job — drives status / cancelRequested / Regenerate eligibility. */
  job?: Job;
  variant: 'impression' | 'generate' | 'local';
  /** Wired to `jobs.cancel(job.id)` by the caller (shown while active). */
  onStop?: () => void;
  /** Wired to `jobs.retry(job.id)` by the caller (terminal error/cancelled only). */
  onRegenerate?: () => void;
}

export default function AiStreamPreview({
  jobId,
  job,
  variant,
  onStop,
  onRegenerate,
}: AiStreamPreviewProps) {
  const partial = useJobPartial(jobId);
  // Reuse the provider's exact retryable-job decision (the same helper the topbar
  // widget uses for its Retry button) rather than re-deriving it here.
  const { canRetry } = useJobs();
  const bodyRef = useRef<HTMLDivElement | null>(null);
  const text = partial?.text ?? '';

  // Keep the newest tokens in view: pin the scroll to the bottom whenever the
  // streamed text grows. Layout effect so it lands before paint (no flicker).
  useLayoutEffect(() => {
    const el = bodyRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [text.length]);

  const active = !!job && isActiveStatus(job.status);
  // Nothing streamed yet and the job is not running → there is nothing to show.
  if (!text && !active) return null;

  const showStop = !!onStop && active && !job?.cancelRequested;
  const showRegenerate =
    !!onRegenerate &&
    !!job &&
    (job.status === 'error' || job.status === 'cancelled') &&
    canRetry(job);

  return (
    <div className="rp-stream-preview" data-testid="ai-stream-preview" data-variant={variant}>
      <div className="rp-stream-preview-head">
        <StatusBadge tone="ai">✨ Live preview</StatusBadge>
        <span className="rp-stream-preview-note">Not yet applied</span>
        {partial?.tokens ? (
          <span className="rp-stream-preview-tokens">~{partial.tokens} tokens</span>
        ) : null}
      </div>
      {/* aria-hidden: the throttled provider live region already announces job
          progress; a raw-token region here would double-announce (see header). */}
      <div className="rp-stream-preview-body" aria-hidden="true" ref={bodyRef}>
        {text}
      </div>
      {variant === 'local' && (
        <p className="rp-stream-preview-foot">
          Raw model output — structured sections are applied on completion.
        </p>
      )}
      {(showStop || showRegenerate) && (
        <div className="rp-stream-preview-actions">
          {showStop && (
            <button type="button" className="ghost" onClick={onStop}>
              Stop
            </button>
          )}
          {showRegenerate && (
            <button type="button" className="primary-ghost" onClick={onRegenerate}>
              Regenerate
            </button>
          )}
        </div>
      )}
    </div>
  );
}
