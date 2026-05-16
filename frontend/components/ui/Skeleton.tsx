import type { CSSProperties } from 'react';

export interface SkeletonProps {
  variant?: 'text' | 'block' | 'row';
  width?: number | string;
  height?: number | string;
  style?: CSSProperties;
  className?: string;
}

export default function Skeleton({ variant = 'text', width, height, style, className }: SkeletonProps) {
  const cls = ['rp-skeleton', `rp-skeleton-${variant}`, className].filter(Boolean).join(' ');
  const merged: CSSProperties = { width, height, ...style };
  return <span className={cls} style={merged} aria-hidden />;
}

export function TableSkeleton({ rows = 6, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <div role="status" aria-live="polite" aria-busy="true">
      <span className="rp-sr-only">Loading…</span>
      {Array.from({ length: rows }).map((_, r) => (
        <div key={r} style={{ display: 'flex', gap: 12, padding: '8px 0', borderBottom: '1px solid var(--border-soft)' }}>
          {Array.from({ length: cols }).map((__, c) => (
            <Skeleton key={c} variant="text" width={c === 0 ? '14%' : c === cols - 1 ? '10%' : '18%'} />
          ))}
        </div>
      ))}
    </div>
  );
}
