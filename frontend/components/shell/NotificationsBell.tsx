'use client';

/**
 * Topbar notifications bell (RC chrome, NOTIF-001). The popover shows two feeds
 * from `useNotifications()`:
 *   (a) "This session" — ephemeral transient notes mirrored from the existing
 *       `rp-notify` CustomEvent (job toasts / banners). Never persisted; the
 *       unseen contribution clears when the popover opens (same UX as before).
 *   (b) The persistent server inbox — recent rows with a per-urgency tone accent,
 *       category label + relative time, unread rows bolded with a dot + an
 *       sr-only "Unread" (never colour/weight alone — NOTIF-002).
 *
 * Clicking a server row marks it read and follows its `linkHref`. The footer
 * "View all" link (desktop only — the inbox page ships only there) opens
 * `/notifications`. The badge is the combined `unreadTotal`.
 *
 * `notify()` / `NOTIFY_EVENT` are re-exported from `lib/notifications` so existing
 * callers (`JobsProvider`) keep working without importing the provider — the
 * transient dispatcher lives in the React-free model, not this component.
 */

import { useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Bell } from 'lucide-react';
import { surfaceAllows } from '@/lib/surface';
import {
  categoryLabelKey,
  relativeTime,
  unreadTotal,
  urgencyTone,
} from '@/lib/notifications';
import { useNotifications } from '@/components/notifications/NotificationsProvider';

// Re-exported single source of truth (see lib/notifications) — keeps
// `import { notify } from '@/components/shell/NotificationsBell'` (JobsProvider,
// banners) working while the dispatcher itself stays React-free.
export { NOTIFY_EVENT, notify } from '@/lib/notifications';
export type { TransientInput as AppNotificationInput } from '@/lib/notifications';

export default function NotificationsBell() {
  const t = useTranslations('topbar.notifications');
  const tn = useTranslations('notifications');
  const router = useRouter();
  const { state, markRead, clearTransient } = useNotifications();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement | null>(null);

  const count = unreadTotal(state);
  const showViewAll = surfaceAllows(['desktop']);

  useEffect(() => {
    if (!open) return;
    // Mirror the old unread-reset-on-open: mark the session notes seen.
    clearTransient();
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
  }, [open, clearTransient]);

  const openRow = (id: string, linkHref?: string) => {
    setOpen(false);
    void markRead(id);
    if (linkHref) router.push(linkHref);
  };

  const now = Date.now();
  const transients = state.transients;
  const items = state.items.slice(0, 8);

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
        {count > 0 && (
          <span className="rp-bell-badge" aria-hidden>
            {count > 9 ? '9+' : count}
          </span>
        )}
      </button>
      {open && (
        <div className="rp-bell-popover" role="menu" aria-label={t('label')}>
          {transients.length > 0 && (
            <>
              <div className="rp-bell-section-title">{t('thisSession')}</div>
              {transients.map((n) => (
                <div key={n.id} className={`rp-bell-item tone-${n.tone ?? 'info'}`}>
                  <span className="rp-bell-item-title">{n.title}</span>
                  {n.detail && <span className="rp-bell-item-detail">{n.detail}</span>}
                </div>
              ))}
            </>
          )}

          <div className="rp-bell-section-title">{t('inbox')}</div>
          {items.length === 0 ? (
            <div className="rp-bell-empty">{t('empty')}</div>
          ) : (
            items.map((n) => {
              const unread = !n.readAt;
              return (
                <button
                  key={n.id}
                  type="button"
                  className={`rp-bell-item rp-bell-row tone-${urgencyTone(n.urgency)}`}
                  data-unread={unread ? 'true' : 'false'}
                  onClick={() => openRow(n.id, n.linkHref)}
                >
                  {unread && <span className="rp-sr-only">{t('unread')}</span>}
                  <span className="rp-bell-row-head">
                    <span className="rp-bell-item-cat">{tn(categoryLabelKey(n.category))}</span>
                    <span className="rp-bell-meta">{relativeTime(n.createdAt, now)}</span>
                  </span>
                  <span className="rp-bell-item-title">{n.title}</span>
                </button>
              );
            })
          )}

          {showViewAll && (
            <div className="rp-bell-footer">
              <Link href="/notifications" className="rp-bell-viewall" onClick={() => setOpen(false)}>
                {t('viewAll')}
              </Link>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
