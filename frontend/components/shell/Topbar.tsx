'use client';

/**
 * RC global topbar (RC-01…RC-10 chrome): brand, global search (Cmd+K),
 * HIPAA pill, update check, notifications, theme toggle, profile menu.
 * Page-level breadcrumbs/actions live in the page header, not here.
 */

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ShieldCheck, Search } from 'lucide-react';
import { useShell } from './ShellContext';
import { PageActionsSlot } from './PageActionsSlot';
import CheckUpdatesButton from './CheckUpdatesButton';
import CommandPalette from './CommandPalette';
import NotificationsBell from './NotificationsBell';
import ProfileMenu from './ProfileMenu';
import ThemeToggle from '@/components/ui/ThemeToggle';

export default function Topbar() {
  const tBar = useTranslations('topbar');
  const { openDrawer } = useShell();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [isMac, setIsMac] = useState(false);

  useEffect(() => {
    setIsMac(/mac/i.test(navigator.platform));
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPaletteOpen((o) => !o);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

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

      <Link href="/" className="rp-topbar-brand" aria-label={tBar('title')}>
        <span className="brand-mark" aria-hidden>
          <span className="brand-mark-letter">R</span>
        </span>
        <span className="rp-topbar-brand-title">{tBar('title')}</span>
      </Link>

      <button
        type="button"
        className="rp-topbar-search"
        onClick={() => setPaletteOpen(true)}
        aria-haspopup="dialog"
        aria-expanded={paletteOpen}
      >
        <Search size={14} aria-hidden />
        <span className="rp-topbar-search-label">{tBar('search')}</span>
        <kbd className="rp-topbar-search-kbd">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
      </button>

      <div className="rp-topbar-spacer" />

      <div className="rp-topbar-actions">
        <PageActionsSlot />
        <span className="rp-hipaa-pill" title={tBar('hipaaTitle')}>
          <ShieldCheck size={13} aria-hidden />
          <span>{tBar('hipaa')}</span>
        </span>
        <CheckUpdatesButton />
        <NotificationsBell />
        <ThemeToggle />
        <ProfileMenu variant="topbar" />
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
    </header>
  );
}
