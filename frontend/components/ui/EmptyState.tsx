import type { ReactNode } from 'react';

export interface EmptyStateProps {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  icon?: ReactNode;
}

export default function EmptyState({ title, description, action, icon }: EmptyStateProps) {
  return (
    <div className="rp-empty" role="status">
      <span className="rp-empty-icon" aria-hidden>
        {icon ?? (
          <svg width={18} height={18} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 7h14v12H5z M5 7l2-3h10l2 3" />
          </svg>
        )}
      </span>
      <p className="rp-empty-title">{title}</p>
      {description && <p className="rp-empty-desc">{description}</p>}
      {action && <div className="rp-empty-actions">{action}</div>}
    </div>
  );
}
