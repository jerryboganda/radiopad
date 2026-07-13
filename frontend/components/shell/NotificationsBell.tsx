'use client';

/**
 * Topbar notifications bell (RC chrome). v1: session-scoped notification
 * feed — records app events dispatched on `rp-notify` CustomEvents (used by
 * banners/long-running jobs) and shows them in a popover with an unread
 * badge. Deeper sources (critical results, mentions) arrive with their
 * PRD modules.
 */

import { useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Bell } from 'lucide-react';

export interface AppNotification {
  id: string;
  title: string;
  detail?: string;
  tone?: 'info' | 'success' | 'warn' | 'danger';
  at: number;
}

export const NOTIFY_EVENT = 'rp-notify';

/** Fire-and-forget helper for feature code: notify('Report exported'). */
export function notify(input: Omit<AppNotification, 'id' | 'at'>) {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(NOTIFY_EVENT, { detail: input }));
}

export default function NotificationsBell() {
  const t = useTranslations('topbar.notifications');
  const [items, setItems] = useState<AppNotification[]>([]);
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const onNotify = (e: Event) => {
      const detail = (e as CustomEvent<Omit<AppNotification, 'id' | 'at'>>).detail;
      if (!detail?.title) return;
      setItems((prev) =>
        [
          { ...detail, id: `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`, at: Date.now() },
          ...prev,
        ].slice(0, 20),
      );
      setUnread((u) => u + 1);
    };
    window.addEventListener(NOTIFY_EVENT, onNotify);
    return () => window.removeEventListener(NOTIFY_EVENT, onNotify);
  }, []);

  useEffect(() => {
    if (!open) return;
    setUnread(0);
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onEsc);
    return () => {
      window.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onEsc);
    };
  }, [open]);

  return (
    <div className="rp-bell" ref={ref}>
      <button
        type="button"
        className="rp-topbar-iconbtn"
        aria-label={t('label')}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <Bell size={16} aria-hidden />
        {unread > 0 && (
          <span className="rp-bell-badge" aria-hidden>
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>
      {open && (
        <div className="rp-bell-popover" role="menu" aria-label={t('label')}>
          <div className="rp-bell-popover-title">{t('label')}</div>
          {items.length === 0 ? (
            <div className="rp-bell-empty">{t('empty')}</div>
          ) : (
            items.map((n) => (
              <div key={n.id} className={`rp-bell-item tone-${n.tone ?? 'info'}`}>
                <span className="rp-bell-item-title">{n.title}</span>
                {n.detail && <span className="rp-bell-item-detail">{n.detail}</span>}
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}
