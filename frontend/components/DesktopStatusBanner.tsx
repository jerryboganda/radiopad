'use client';

import { useEffect, useState } from 'react';

type BackendStatus = {
  state?: string;
  message?: string | null;
  restartCount?: number;
};

function statusCopy(status: BackendStatus): { className: string; text: string } | null {
  switch (status.state) {
    case 'starting':
      return { className: 'info', text: 'Starting the local RadioPad service...' };
    case 'restarting':
      return { className: 'warn', text: 'Restarting the local RadioPad service...' };
    case 'degraded':
      return {
        className: 'warn',
        text: status.message
          ? `Local RadioPad service is not ready: ${status.message}`
          : 'Local RadioPad service is not ready yet.',
      };
    case 'failed':
      return {
        className: 'danger',
        text: status.message || 'Local RadioPad service failed to start.',
      };
    default:
      return null;
  }
}

export default function DesktopStatusBanner() {
  const [status, setStatus] = useState<BackendStatus | null>(null);

  useEffect(() => {
    const onStatus = (event: Event) => {
      const detail = (event as CustomEvent<BackendStatus>).detail;
      setStatus(detail ?? null);
    };
    window.addEventListener('radiopad:backend-status', onStatus);
    return () => window.removeEventListener('radiopad:backend-status', onStatus);
  }, []);

  if (!status || status.state === 'ready' || status.state === 'disabled') return null;
  const copy = statusCopy(status);
  if (!copy) return null;

  return (
    <div className={`banner ${copy.className} rp-desktop-status`} role="status">
      {copy.text}
      {typeof status.restartCount === 'number' && status.restartCount > 0 ? (
        <span className="rp-desktop-status-meta">Restart attempt {status.restartCount}</span>
      ) : null}
    </div>
  );
}
