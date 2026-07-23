'use client';

/**
 * NOTIF-001 inbox page. Filter chips (category / urgency / unread / needs-ack)
 * drive `api.notifications.list`; each row carries an urgency `StatusBadge` (always
 * with its text label — NOTIF-002), a relative timestamp, an inline two-step
 * Acknowledge on RequiresAck rows, and a checkbox for multi-select bulk actions.
 *
 * NOTIF-011 — a bulk action over any CriticalResult / System / RequiresAck row
 * opens a confirmation dialog and then calls `bulk(..., confirm:true)`, mirroring
 * the server rule so the 400 never surprises the user in the normal flow (a race
 * that still returns `confirmation_required` re-opens the dialog).
 *
 * NOTIF-010 states: <Skeleton/> while loading, <EmptyState/> when the filtered
 * list is empty, <ErrorState onRetry/> on a fetch failure, and an amber
 * offline/stale banner when the stream is down AND the browser is offline or the
 * last good fetch is > 2 minutes old.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { api, type NotificationBulkResult } from '@/lib/api';
import { hostedEvents, type StreamStatus } from '@/lib/events';
import {
  NOTIFICATION_CATEGORIES,
  NOTIFICATION_URGENCIES,
  categoryLabelKey,
  isInboxStale,
  relativeTime,
  selectListUiState,
  urgencyBadgeTone,
  urgencyLabelKey,
  urgencyTone,
  type NotificationItem,
} from '@/lib/notifications';
import { useNotifications } from '@/components/notifications/NotificationsProvider';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import StatusBadge from '@/components/ui/StatusBadge';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

interface Filters {
  category: string | null;
  urgency: string | null;
  unread: boolean;
  requiresAck: boolean;
}

const NO_FILTERS: Filters = { category: null, urgency: null, unread: false, requiresAck: false };

/** A bulk action needs explicit confirmation when it spans a clinical / compliance
 *  row — mirrors the backend NOTIF-011 predicate exactly. */
function bulkNeedsConfirm(rows: NotificationItem[], ids: Set<string>): boolean {
  return rows.some(
    (r) =>
      ids.has(r.id) &&
      (r.requiresAck || r.category === 'CriticalResult' || r.category === 'System'),
  );
}

export default function NotificationsPageClient() {
  const t = useTranslations('notifications');
  const { ack, bulk, markRead } = useNotifications();

  const [filters, setFilters] = useState<Filters>(NO_FILTERS);
  const [rows, setRows] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastFetchAt, setLastFetchAt] = useState<number | null>(null);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [ackConfirmId, setAckConfirmId] = useState<string | null>(null);
  const [pendingBulk, setPendingBulk] = useState<{ ids: string[]; action: 'read' | 'ack' } | null>(
    null,
  );
  const [busy, setBusy] = useState(false);

  // Liveness inputs for the stale banner.
  const [streamStatus, setStreamStatus] = useState<StreamStatus>(hostedEvents.status);
  const [online, setOnline] = useState(
    typeof navigator === 'undefined' ? true : navigator.onLine,
  );
  const [now, setNow] = useState(() => Date.now());

  // --- fetch the filtered list (re-created — and re-run — on filter change) ----
  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.notifications.list({
        limit: 50,
        unread: filters.unread || undefined,
        requiresAck: filters.requiresAck || undefined,
        category: filters.category ?? undefined,
        urgency: filters.urgency ?? undefined,
      });
      setRows(res.notifications);
      setLastFetchAt(Date.now());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    void load();
  }, [load]);

  // --- liveness wiring for the stale banner ----------------------------------
  useEffect(() => {
    const off = hostedEvents.onStatus(setStreamStatus);
    setStreamStatus(hostedEvents.status);
    return off;
  }, []);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const on = () => setOnline(true);
    const offline = () => setOnline(false);
    window.addEventListener('online', on);
    window.addEventListener('offline', offline);
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => {
      window.removeEventListener('online', on);
      window.removeEventListener('offline', offline);
      clearInterval(id);
    };
  }, []);

  const stale = isInboxStale({
    streamOpen: streamStatus === 'open',
    online,
    lastFetchAt,
    now,
  });
  const uiState = selectListUiState({ loading, error: error != null, count: rows.length });

  // --- selection helpers ------------------------------------------------------
  const toggleSelect = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };
  const clearSelection = () => setSelected(new Set());

  // --- single-row acknowledge (two-step inline confirm) ----------------------
  const doAck = async (n: NotificationItem) => {
    setAckConfirmId(null);
    setActionError(null);
    setBusy(true);
    try {
      await ack(n.id);
      const iso = new Date().toISOString();
      setRows((prev) =>
        prev.map((r) =>
          r.id === n.id ? { ...r, acknowledgedAt: iso, readAt: r.readAt ?? iso } : r,
        ),
      );
    } catch (e) {
      setActionError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  // --- bulk actions -----------------------------------------------------------
  const runBulk = useCallback(
    async (ids: string[], action: 'read' | 'ack', confirm: boolean) => {
      setActionError(null);
      setBusy(true);
      try {
        const res: NotificationBulkResult = await bulk(ids, action, confirm);
        const iso = new Date().toISOString();
        const idSet = new Set(ids);
        setRows((prev) =>
          prev.map((r) => {
            if (!idSet.has(r.id)) return r;
            if (action === 'read') return r.readAt ? r : { ...r, readAt: iso };
            if (r.requiresAck && !r.acknowledgedAt)
              return { ...r, acknowledgedAt: iso, readAt: r.readAt ?? iso };
            return r;
          }),
        );
        clearSelection();
        setPendingBulk(null);
        return res;
      } catch (e) {
        const err = e as { kind?: string; message?: string };
        // A race still returned the server's confirmation gate — surface the dialog.
        if (err.kind === 'confirmation_required') {
          setPendingBulk({ ids, action });
        } else {
          setActionError(err.message ?? 'Bulk action failed.');
        }
      } finally {
        setBusy(false);
      }
    },
    [bulk],
  );

  const startBulk = (action: 'read' | 'ack') => {
    const ids = Array.from(selected);
    if (ids.length === 0) return;
    if (bulkNeedsConfirm(rows, selected)) {
      setPendingBulk({ ids, action });
    } else {
      void runBulk(ids, action, false);
    }
  };

  const openRow = (n: NotificationItem) => {
    void markRead(n.id);
    setRows((prev) => (n.readAt ? prev : prev.map((r) => (r.id === n.id ? { ...r, readAt: new Date().toISOString() } : r))));
  };

  const selectedCount = selected.size;
  const staleTime = useMemo(
    () => (lastFetchAt ? relativeTime(new Date(lastFetchAt).toISOString(), now) : ''),
    [lastFetchAt, now],
  );

  return (
    <Container>
      <PageHeader
        title={t('title')}
        description={t('subtitle')}
        secondaryActions={
          <button type="button" className="subtle" onClick={() => void load()} disabled={loading}>
            {t('refresh')}
          </button>
        }
      />

      {stale && (
        <div className="banner warn" role="status">
          {t('offline', { time: staleTime || t('recently') })}
        </div>
      )}

      <div className="rp-panel" aria-live="polite" aria-busy={loading}>
        <div className="rp-inbox-filters" role="group" aria-label={t('filtersLabel')}>
          <button
            type="button"
            className={filters === NO_FILTERS ? 'rp-inbox-filter-chip is-active' : 'rp-inbox-filter-chip'}
            aria-pressed={filters === NO_FILTERS}
            onClick={() => setFilters(NO_FILTERS)}
          >
            {t('filterAll')}
          </button>
          <button
            type="button"
            className={`rp-inbox-filter-chip${filters.unread ? ' is-active' : ''}`}
            aria-pressed={filters.unread}
            onClick={() => setFilters((f) => ({ ...f, unread: !f.unread }))}
          >
            {t('filterUnread')}
          </button>
          <button
            type="button"
            className={`rp-inbox-filter-chip${filters.requiresAck ? ' is-active' : ''}`}
            aria-pressed={filters.requiresAck}
            onClick={() => setFilters((f) => ({ ...f, requiresAck: !f.requiresAck }))}
          >
            {t('filterNeedsAck')}
          </button>
          <span className="rp-inbox-filter-sep" aria-hidden />
          {NOTIFICATION_URGENCIES.map((u) => (
            <button
              key={u}
              type="button"
              className={`rp-inbox-filter-chip${filters.urgency === u ? ' is-active' : ''}`}
              aria-pressed={filters.urgency === u}
              onClick={() => setFilters((f) => ({ ...f, urgency: f.urgency === u ? null : u }))}
            >
              {t(urgencyLabelKey(u))}
            </button>
          ))}
          <span className="rp-inbox-filter-sep" aria-hidden />
          {NOTIFICATION_CATEGORIES.map((c) => (
            <button
              key={c}
              type="button"
              className={`rp-inbox-filter-chip${filters.category === c ? ' is-active' : ''}`}
              aria-pressed={filters.category === c}
              onClick={() => setFilters((f) => ({ ...f, category: f.category === c ? null : c }))}
            >
              {t(categoryLabelKey(c))}
            </button>
          ))}
        </div>

        {actionError && (
          <p className="rp-page-sub" role="alert" style={{ marginBottom: 10 }}>
            <span className="badge danger">{t('actionFailed')}</span> {actionError}
          </p>
        )}

        {selectedCount > 0 && (
          <div className="rp-inbox-bulkbar" role="group" aria-label={t('bulkLabel')}>
            <span className="rp-inbox-bulkbar-count">{t('selectedCount', { count: selectedCount })}</span>
            <button type="button" className="subtle" disabled={busy} onClick={() => startBulk('read')}>
              {t('bulkMarkRead')}
            </button>
            <button type="button" className="primary-ghost" disabled={busy} onClick={() => startBulk('ack')}>
              {t('bulkAck')}
            </button>
            <button type="button" className="ghost" onClick={clearSelection}>
              {t('clearSelection')}
            </button>
          </div>
        )}

        {uiState === 'loading' ? (
          <div className="rp-inbox-list" role="status" aria-busy="true">
            <span className="rp-sr-only">{t('loading')}</span>
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} variant="block" height={64} />
            ))}
          </div>
        ) : uiState === 'error' ? (
          <ErrorState title={t('errorTitle')} message={error ?? undefined} onRetry={() => void load()} />
        ) : uiState === 'empty' ? (
          <EmptyState title={t('emptyTitle')} description={t('emptyDesc')} />
        ) : (
          <ul className="rp-inbox-list">
            {rows.map((n) => {
              const unread = !n.readAt;
              return (
                <li
                  key={n.id}
                  className={`rp-inbox-item tone-${urgencyTone(n.urgency)}`}
                  data-unread={unread ? 'true' : 'false'}
                >
                  <input
                    type="checkbox"
                    className="rp-inbox-check"
                    checked={selected.has(n.id)}
                    onChange={() => toggleSelect(n.id)}
                    aria-label={t('selectRow')}
                  />
                  <div className="rp-inbox-item-main">
                    {unread && <span className="rp-sr-only">{t('unread')}</span>}
                    <div className="rp-inbox-item-head">
                      <StatusBadge tone={urgencyBadgeTone(n.urgency)}>
                        {t(urgencyLabelKey(n.urgency))}
                      </StatusBadge>
                      <span className="rp-inbox-badge">{t(categoryLabelKey(n.category))}</span>
                      <span className="rp-inbox-meta">{relativeTime(n.createdAt, now)}</span>
                    </div>
                    <p className="rp-inbox-item-title">{n.title}</p>
                    {n.body && <p className="rp-inbox-item-sub">{n.body}</p>}
                  </div>
                  <div className="rp-inbox-item-actions">
                    {n.requiresAck && !n.acknowledgedAt ? (
                      ackConfirmId === n.id ? (
                        <button type="button" className="primary" disabled={busy} onClick={() => void doAck(n)}>
                          {t('ackConfirm')}
                        </button>
                      ) : (
                        <button type="button" className="primary-ghost" onClick={() => setAckConfirmId(n.id)}>
                          {t('ack')}
                        </button>
                      )
                    ) : n.acknowledgedAt ? (
                      <span className="badge ok">{t('acknowledged')}</span>
                    ) : null}
                    {n.linkHref && (
                      <Link href={n.linkHref} className="rp-inbox-open" onClick={() => openRow(n)}>
                        {t('open')} →
                      </Link>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {pendingBulk && (
        <div
          className="rp-inbox-confirm-scrim"
          role="presentation"
          onClick={() => setPendingBulk(null)}
        >
          <div
            className="rp-inbox-confirm"
            role="dialog"
            aria-modal="true"
            aria-label={t('confirmTitle')}
            onClick={(e) => e.stopPropagation()}
          >
            <h2 className="rp-inbox-confirm-title">{t('confirmTitle')}</h2>
            <p className="rp-inbox-confirm-body">
              {t('confirmBody', { count: pendingBulk.ids.length })}
            </p>
            <div className="rp-inbox-confirm-actions">
              <button type="button" className="ghost" onClick={() => setPendingBulk(null)}>
                {t('cancel')}
              </button>
              <button
                type="button"
                className="primary"
                disabled={busy}
                onClick={() => void runBulk(pendingBulk.ids, pendingBulk.action, true)}
              >
                {t('confirmProceed')}
              </button>
            </div>
          </div>
        </div>
      )}
    </Container>
  );
}
