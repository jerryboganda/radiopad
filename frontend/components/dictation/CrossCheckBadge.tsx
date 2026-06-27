'use client';

// Small fixed processing badge (bottom-right) shown while a cross-check runs and
// when it finishes. Non-blocking — the radiologist keeps working while it polls.

export interface CrossCheckBadgeProps {
  status: 'running' | 'completed' | 'failed';
  stage: string;
  onDismiss?: () => void;
}

export default function CrossCheckBadge({ status, stage, onDismiss }: CrossCheckBadgeProps) {
  const tone = status === 'failed' ? 'danger' : status === 'completed' ? 'success' : 'info';
  return (
    <div className={`rp-xc-badge ${tone}`} role="status" data-testid="crosscheck-badge">
      {status === 'running' && <span className="rp-xc-spinner" aria-hidden="true" />}
      <span className="rp-xc-badge-label">{stage}</span>
      {status !== 'running' && onDismiss && (
        <button
          type="button"
          className="rp-xc-badge-dismiss"
          aria-label="Dismiss"
          data-testid="crosscheck-badge-dismiss"
          onClick={onDismiss}
        >
          ×
        </button>
      )}
    </div>
  );
}
