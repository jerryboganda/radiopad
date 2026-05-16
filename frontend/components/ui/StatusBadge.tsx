import type { ReactNode } from 'react';

export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'ai';

export interface StatusBadgeProps {
  tone?: StatusTone;
  children: ReactNode;
}

const REPORT_STATUS_TO_TONE: Record<string, StatusTone> = {
  draft: 'neutral',
  validated: 'info',
  acknowledged: 'success',
  exported: 'success',
};

export function reportStatusTone(status: string | number): StatusTone {
  const label = typeof status === 'number' ? ['draft', 'validated', 'acknowledged', 'exported'][status] : String(status).toLowerCase();
  return REPORT_STATUS_TO_TONE[label] ?? 'neutral';
}

export default function StatusBadge({ tone = 'neutral', children }: StatusBadgeProps) {
  return <span className={`rp-status ${tone}`}>{children}</span>;
}
