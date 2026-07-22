'use client';

// Full-surface loading experience shown while the report intake wizard creates
// the draft and runs whole-report generation through the chosen provider. The
// underlying calls are blocking (no token streaming), so this shows STAGED
// progress with a real elapsed timer and real start/finish: the visual stages
// advance on a slowing cadence while the (long) generate call is in flight, and
// snap to complete the moment it resolves. On failure it flips to an inline
// error with Retry / Back so the radiologist is never stranded on a spinner.

import { useEffect, useRef, useState } from 'react';
import { Check, AlertTriangle } from 'lucide-react';
import { formatElapsed } from '@/lib/jobs';

const STAGES = [
  'Preparing study context',
  'Contacting {provider}',
  'Drafting findings',
  'Composing impression',
  'Finalizing report',
] as const;

// Index up to which the visual stages auto-advance while waiting; the last stage
// only completes when the real response arrives (done=true).
const LAST_AUTO_STAGE = STAGES.length - 2;
const STAGE_INTERVAL_MS = 1700;

export interface GenerationOverlayProps {
  /** True while the create → generate pipeline is running. */
  active: boolean;
  /** Flips true when generation resolved successfully (all stages complete). */
  done: boolean;
  /** Provider display name, woven into the "Contacting…" stage. */
  providerName?: string;
  /** Non-null when the pipeline failed; shows the error state. */
  error?: string | null;
  onRetry?: () => void;
  onBack?: () => void;
}

export default function GenerationOverlay({
  active,
  done,
  providerName,
  error,
  onRetry,
  onBack,
}: GenerationOverlayProps) {
  const [stage, setStage] = useState(0);
  const [elapsed, setElapsed] = useState(0);
  const startRef = useRef<number | null>(null);

  // Elapsed timer — ticks while running, freezes on done/error.
  useEffect(() => {
    if (!active) {
      startRef.current = null;
      setElapsed(0);
      setStage(0);
      return;
    }
    startRef.current = performance.now();
    const id = window.setInterval(() => {
      if (startRef.current != null) setElapsed(performance.now() - startRef.current);
    }, 100);
    return () => window.clearInterval(id);
  }, [active]);

  // Auto-advance the visual stages up to LAST_AUTO_STAGE on a fixed cadence.
  useEffect(() => {
    if (!active || done || error) return;
    const id = window.setInterval(() => {
      setStage((s) => (s < LAST_AUTO_STAGE ? s + 1 : s));
    }, STAGE_INTERVAL_MS);
    return () => window.clearInterval(id);
  }, [active, done, error]);

  // Snap every stage to complete when the real response lands.
  useEffect(() => {
    if (done) setStage(STAGES.length);
  }, [done]);

  if (!active) return null;

  const provider = providerName?.trim() || 'the AI provider';
  const isError = !!error;
  const pct = isError
    ? Math.min(100, (stage / STAGES.length) * 100)
    : done
      ? 100
      : Math.min(96, ((stage + 0.5) / STAGES.length) * 100);

  return (
    <div className="rp-genoverlay" role="dialog" aria-modal="true" aria-live="polite">
      <div className="rp-genoverlay-card rp-anim-scale-in">
        {isError ? (
          <>
            <div className="rp-genoverlay-icon error" aria-hidden>
              <AlertTriangle size={26} />
            </div>
            <h2 className="rp-genoverlay-title">Generation didn’t complete</h2>
            <p className="rp-genoverlay-sub">{error}</p>
            <div className="rp-genoverlay-actions">
              {onBack && (
                <button className="ghost" onClick={onBack}>
                  Back to intake
                </button>
              )}
              {onRetry && (
                <button className="primary" onClick={onRetry}>
                  Try again
                </button>
              )}
            </div>
          </>
        ) : (
          <>
            <div className={`rp-genoverlay-orb${done ? ' done' : ''}`} aria-hidden>
              <span className="rp-genoverlay-orb-ring" />
              <span className="rp-genoverlay-orb-ring d2" />
              <span className="rp-genoverlay-orb-core" />
            </div>

            <h2 className="rp-genoverlay-title">
              {done ? 'Report ready' : 'Generating your report'}
            </h2>
            <p className="rp-genoverlay-sub">
              {done
                ? 'Opening the editor…'
                : `Working with ${provider}. This can take up to a minute.`}
            </p>

            <ul className="rp-genoverlay-steps">
              {STAGES.map((label, i) => {
                const text = label.replace('{provider}', provider);
                const state = i < stage ? 'done' : i === stage ? 'active' : 'todo';
                return (
                  <li key={label} className={`rp-genoverlay-step ${state}`}>
                    <span className="rp-genoverlay-step-mark" aria-hidden>
                      {state === 'done' ? (
                        <Check size={13} />
                      ) : state === 'active' ? (
                        <span className="rp-genoverlay-step-spin" />
                      ) : (
                        <span className="rp-genoverlay-step-dot" />
                      )}
                    </span>
                    <span className="rp-genoverlay-step-label">{text}</span>
                  </li>
                );
              })}
            </ul>

            <div className="rp-genoverlay-bar" aria-hidden>
              <span
                className={`rp-genoverlay-bar-fill${done ? ' done' : ''}`}
                style={{ width: `${pct}%` }}
              />
            </div>
            <div className="rp-genoverlay-meta">
              <span className="rp-genoverlay-timer">{formatElapsed(elapsed)}</span>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
