'use client';

import { useTranslations } from 'next-intl';
import { useShell } from './ShellContext';
import Breadcrumbs, { type BreadcrumbItem } from './Breadcrumbs';
import { PageActionsSlot } from './PageActionsSlot';

export interface TopbarProps {
  breadcrumbs?: BreadcrumbItem[];
}

export default function Topbar({ breadcrumbs = [] }: TopbarProps) {
  const tBar = useTranslations('topbar');
  const { openDrawer } = useShell();

  return (
    <header className="rp-topbar">
      <button
        type="button"
        className="rp-topbar-menu"
        aria-label={tBar('openMenu')}
        onClick={openDrawer}
      >
        <svg width={18} height={18} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden>
          <path d="M4 7h16 M4 12h16 M4 17h16" />
        </svg>
      </button>

      <Breadcrumbs items={breadcrumbs} />

      <div className="rp-topbar-spacer" />

      <div className="rp-topbar-actions">
        <PageActionsSlot />
      </div>
    </header>
  );
}
