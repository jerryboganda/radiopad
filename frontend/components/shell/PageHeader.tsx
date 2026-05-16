import type { ReactNode } from 'react';

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  primaryAction?: ReactNode;
  secondaryActions?: ReactNode;
}

export default function PageHeader({ title, description, primaryAction, secondaryActions }: PageHeaderProps) {
  return (
    <header className="rp-page-header">
      <div className="rp-page-header-text">
        <h1 className="rp-page-title">{title}</h1>
        {description && <p className="rp-page-sub">{description}</p>}
      </div>
      {(primaryAction || secondaryActions) && (
        <div className="rp-page-actions">
          {secondaryActions}
          {primaryAction}
        </div>
      )}
    </header>
  );
}
