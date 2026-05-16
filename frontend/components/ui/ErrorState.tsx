'use client';

import type { ReactNode } from 'react';

export interface ErrorStateProps {
  title?: ReactNode;
  message?: ReactNode;
  onRetry?: () => void;
  retryLabel?: string;
}

export default function ErrorState({
  title = 'Something went wrong',
  message,
  onRetry,
  retryLabel = 'Try again',
}: ErrorStateProps) {
  return (
    <div className="rp-error" role="alert">
      <span className="rp-error-icon" aria-hidden>
        <svg width={18} height={18} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 8v5 M12 16h.01 M12 3l10 18H2z" />
        </svg>
      </span>
      <p className="rp-error-title">{title}</p>
      {message && <p className="rp-error-desc">{message}</p>}
      {onRetry && (
        <div className="rp-error-actions">
          <button type="button" onClick={onRetry}>
            {retryLabel}
          </button>
        </div>
      )}
    </div>
  );
}
