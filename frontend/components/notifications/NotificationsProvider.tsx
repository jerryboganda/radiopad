'use client';

/**
 * NOTIF-001 — global notifications provider. Owns the inbox model (recent server
 * rows + session-only transient notes + unread counts), subscribes to the SAME
 * `hostedEvents` SSE singleton the `JobsProvider` uses (filtering for the
 * `notification` event type), and exposes optimistic `markRead`/`ack`/`bulk` with
 * rollback.
 *
 * Mounted in `AppShell` beside `JobsProvider` but — unlike the jobs stack — it is
 * NOT desktop-gated: web-admin users have inboxes too (System / Billing notices),
 * so this runs on every surface. Only the OS-toast bridge (`ShellBridge`) is
 * desktop-only, and it self-guards.
 *
 * Live delivery: on each SSE `notification` event we upsert the row (idempotent by
 * id) and re-dispatch a PHI-minimised `radiopad:notification` CustomEvent for the
 * desktop OS-toast listener — {id, category, urgency, linkHref} ONLY (NOTIF-004;
 * ShellBridge derives generic toast wording, no body/title text crosses the seam).
 * While the stream is not `open`, a 60 s unread-count poll keeps the badge honest.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  type ReactNode,
} from 'react';
import { api } from '@/lib/api';
import { hostedEvents, type AppEvent, type StreamStatus } from '@/lib/events';
import {
  NOTIFY_EVENT,
  UNREAD_POLL_MS,
  initialNotificationsState,
  notificationsReducer,
  type NotificationItem,
  type NotificationsState,
  type TransientInput,
} from '@/lib/notifications';

export interface NotificationsContextValue {
  state: NotificationsState;
  /** Optimistically mark one row read (rolls back on API failure). */
  markRead: (id: string) => Promise<void>;
  /** Optimistically acknowledge a RequiresAck row (rolls back on API failure). */
  ack: (id: string) => Promise<void>;
  /** Bulk mark-read / ack. Throws the backend `apiError` (e.g. 400
   *  `confirmation_required`) unchanged so the caller decides what to show. */
  bulk: (
    ids: string[],
    action: 'read' | 'ack',
    confirm: boolean,
  ) => Promise<{ updated: number }>;
  /** Re-pull the recent list + counts (bell hydrate, reconnect resume, focus). */
  refetch: () => Promise<void>;
  /** Reset the unseen-transient badge contribution (bell popover open). */
  clearTransient: () => void;
}

const NotificationsContext = createContext<NotificationsContextValue | null>(null);

function isAuthError(e: unknown): boolean {
  return (e as { status?: number })?.status === 401;
}

export default function NotificationsProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(
    notificationsReducer,
    undefined,
    initialNotificationsState,
  );

  // Always-current view for the async callbacks (SSE handler, mutations) so they
  // never read a stale closure — same discipline as JobsProvider's stateRef.
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  });
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // --- hydrate (list + counts). Shared by mount, reconnect resume, and refetch. -
  const refetch = useCallback(async (): Promise<void> => {
    try {
      const [list, counts] = await Promise.all([
        api.notifications.list({ limit: 20 }),
        api.notifications.unreadCount(),
      ]);
      if (!mountedRef.current) return;
      dispatch({
        type: 'HYDRATE',
        items: list.notifications,
        nextCursor: list.nextCursor ?? null,
        unread: counts.unread,
        unacked: counts.unacked,
      });
    } catch (e) {
      // 401 is AuthGate's job; anything else degrades silently (poll/next SSE).
      if (!isAuthError(e)) console.warn('[notifications] hydrate failed', e);
    }
  }, []);

  const refreshCount = useCallback(async (): Promise<void> => {
    try {
      const counts = await api.notifications.unreadCount();
      if (!mountedRef.current) return;
      dispatch({ type: 'HYDRATE', unread: counts.unread, unacked: counts.unacked });
    } catch {
      /* best effort — the next poll / SSE event reconciles */
    }
  }, []);

  useEffect(() => {
    void refetch();
  }, [refetch]);

  // --- live SSE notification events (same singleton JobsProvider subscribes to) --
  useEffect(() => {
    const off = hostedEvents.subscribe((e: AppEvent) => {
      if (e.type !== 'notification') return;
      const item = e.data as NotificationItem;
      if (!item || typeof item.id !== 'string') return;
      dispatch({ type: 'RECEIVE_SSE', item });
      // PHI-minimised (NOTIF-004) re-dispatch for the desktop OS-toast bridge.
      // Mirrors JobsProvider's `radiopad:job-terminal`: pass only non-identifying
      // fields; ShellBridge's notificationToastTitle/Body derive generic wording.
      if (typeof window !== 'undefined') {
        window.dispatchEvent(
          new CustomEvent('radiopad:notification', {
            detail: {
              id: item.id,
              category: item.category,
              urgency: item.urgency,
              linkHref: item.linkHref,
            },
          }),
        );
      }
    });
    return off;
  }, []);

  // --- 60 s unread-count poll fallback while the stream is not open -------------
  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | null = null;
    const stop = () => {
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
    };
    const sync = (status: StreamStatus) => {
      if (status === 'open') {
        stop();
      } else if (!timer) {
        timer = setInterval(() => void refreshCount(), UNREAD_POLL_MS);
      }
    };
    const off = hostedEvents.onStatus(sync);
    sync(hostedEvents.status); // initialise for the current status
    return () => {
      off();
      stop();
    };
  }, [refreshCount]);

  // --- rp-notify transient feed (job toasts / banners) -------------------------
  useEffect(() => {
    const onNotify = (e: Event) => {
      const detail = (e as CustomEvent<TransientInput>).detail;
      if (!detail?.title) return;
      dispatch({
        type: 'RECEIVE_TRANSIENT',
        note: {
          ...detail,
          id: `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
          at: Date.now(),
        },
      });
    };
    window.addEventListener(NOTIFY_EVENT, onNotify);
    return () => window.removeEventListener(NOTIFY_EVENT, onNotify);
  }, []);

  // --- refresh the badge the instant the user returns (deep-link fallback, 2.7) -
  useEffect(() => {
    if (typeof document === 'undefined') return;
    const onVisible = () => {
      if (!document.hidden) void refreshCount();
    };
    const onFocus = () => void refreshCount();
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('focus', onFocus);
    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('focus', onFocus);
    };
  }, [refreshCount]);

  // --- public mutations --------------------------------------------------------
  const markRead = useCallback(
    async (id: string): Promise<void> => {
      const previous = stateRef.current.items.find((n) => n.id === id);
      // Optimistic path for a row the bell holds; older inbox rows (not in state)
      // skip the optimistic dispatch and reconcile the badge via refetch.
      if (previous && !previous.readAt) dispatch({ type: 'MARK_READ', id });
      try {
        await api.notifications.markRead(id);
        if (!previous) await refreshCount();
      } catch (e) {
        if (previous && !previous.readAt) dispatch({ type: 'ROLLBACK_READ', previous });
        throw e;
      }
    },
    [refreshCount],
  );

  const ack = useCallback(
    async (id: string): Promise<void> => {
      const previous = stateRef.current.items.find((n) => n.id === id);
      const optimistic = !!previous && previous.requiresAck && !previous.acknowledgedAt;
      if (optimistic) dispatch({ type: 'ACK', id });
      try {
        await api.notifications.ack(id);
        if (!optimistic) await refreshCount();
      } catch (e) {
        if (optimistic && previous) dispatch({ type: 'ROLLBACK_ACK', previous });
        throw e;
      }
    },
    [refreshCount],
  );

  const bulk = useCallback(
    async (
      ids: string[],
      action: 'read' | 'ack',
      confirm: boolean,
    ): Promise<{ updated: number }> => {
      // No optimistic dispatch: a bulk set may span rows the bell doesn't hold.
      // Let the backend enforce NOTIF-011 (it throws `confirmation_required` when
      // required), then reconcile the list + counts on success.
      const res = await api.notifications.bulk({ ids, action, confirm });
      await refetch();
      return res;
    },
    [refetch],
  );

  const clearTransient = useCallback(() => dispatch({ type: 'CLEAR_TRANSIENT' }), []);

  const value = useMemo<NotificationsContextValue>(
    () => ({ state, markRead, ack, bulk, refetch, clearTransient }),
    [state, markRead, ack, bulk, refetch, clearTransient],
  );

  return (
    <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>
  );
}

export function useNotifications(): NotificationsContextValue {
  const ctx = useContext(NotificationsContext);
  if (!ctx) throw new Error('useNotifications must be used within <NotificationsProvider>');
  return ctx;
}
