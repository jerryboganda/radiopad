/**
 * NOTIF-001 — the React-free notifications model shared by the global
 * `NotificationsProvider`, the topbar `NotificationsBell`, and the `/notifications`
 * inbox page. Mirrors `lib/jobs.ts`: the wire shape, the pure `notificationsReducer`
 * (unit-tested directly), the label/tone maps, and the small state→UI selectors the
 * provider and both surfaces share. No React, no network here.
 *
 * Two feeds coexist and never mix:
 *  - SERVER rows (`items`) come from `/api/notifications` + the SSE `notification`
 *    event; they carry `readAt`/`acknowledgedAt` and drive the persistent inbox.
 *  - TRANSIENT notes (`transients`) are session-only mirrors of the existing
 *    `rp-notify` CustomEvent (job toasts, banners). They are never persisted and
 *    never merged into the server list — cap 5, cleared-on-open like the old bell.
 *
 * Clinical-safety note (NOTIF-002): every urgency/category maps to a *tone* AND a
 * *text label*. Tone is never the only signal — callers always render the label
 * alongside the colour.
 */

// ---------------------------------------------------------------------------
// Wire shape (mirrors backend NotificationView.Of — camelCase, already stable)
// ---------------------------------------------------------------------------

/** All notification categories the backend can emit (enum-as-string). */
export const NOTIFICATION_CATEGORIES = [
  'AiJob',
  'CriticalResult',
  'PeerReview',
  'RulebookApproval',
  'TemplateApproval',
  'Mention',
  'System',
] as const;
export type NotificationCategory = (typeof NOTIFICATION_CATEGORIES)[number];

/** Notification severity (NOTIF-002). */
export const NOTIFICATION_URGENCIES = ['Info', 'Warning', 'Critical'] as const;
export type NotificationUrgency = (typeof NOTIFICATION_URGENCIES)[number];

/** One persisted server notification row (the shape `GET /api/notifications`
 *  returns and the SSE `notification` event carries — both are
 *  `NotificationView.Of`). `category` is a plain string so a future enum value
 *  degrades to the fallback label rather than a type error. */
export interface NotificationItem {
  id: string;
  category: string;
  urgency: NotificationUrgency;
  title: string;
  body: string;
  linkHref?: string;
  sourceKind?: string;
  sourceId?: string;
  requiresAck: boolean;
  /** ISO-8601 (DateTimeOffset). Absent/optimistic-null while unread. */
  readAt?: string;
  acknowledgedAt?: string;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Transient notes (rp-notify) — the session-only client toast feed
// ---------------------------------------------------------------------------

export type TransientTone = 'info' | 'success' | 'warn' | 'danger';

/** The payload feature code dispatches via `notify()`. */
export interface TransientInput {
  title: string;
  detail?: string;
  tone?: TransientTone;
}

/** A session-only note prepended to the "This session" bell section. Sourced from
 *  the existing `rp-notify` CustomEvent; never persisted, never merged with the
 *  server list. */
export interface TransientNote extends TransientInput {
  id: string;
  at: number;
}

/** The CustomEvent name job toasts / banners already dispatch on. Kept here (not
 *  in a component) so both `NotificationsBell` and `NotificationsProvider` import
 *  the single source of truth without a component↔provider import cycle. */
export const NOTIFY_EVENT = 'rp-notify';

/** Fire-and-forget helper for feature code: `notify({ title: 'Report exported' })`.
 *  `JobsProvider` and banners call this; `NotificationsProvider` ingests it. */
export function notify(input: TransientInput): void {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(NOTIFY_EVENT, { detail: input }));
}

// ---------------------------------------------------------------------------
// Labels + tones (NOTIF-002 — tone is paired with text, never colour alone)
// ---------------------------------------------------------------------------

/** design.md status vocabulary tone for a bell/inbox row accent. */
export type NotificationTone = 'info' | 'warn' | 'danger';

const URGENCY_TONE: Record<NotificationUrgency, NotificationTone> = {
  Info: 'info', // blue
  Warning: 'warn', // amber
  Critical: 'danger', // red
};

/** Row-accent tone (`.rp-inbox-item.tone-*` / `.rp-bell-item.tone-*`). */
export function urgencyTone(urgency: string): NotificationTone {
  return URGENCY_TONE[urgency as NotificationUrgency] ?? 'info';
}

/** `StatusBadge` tone (its vocabulary uses `warning`, not `warn`). Critical→red,
 *  Warning→amber, Info→blue — always rendered with the urgency text label. */
export function urgencyBadgeTone(urgency: string): 'info' | 'warning' | 'danger' {
  switch (urgency) {
    case 'Critical':
      return 'danger';
    case 'Warning':
      return 'warning';
    default:
      return 'info';
  }
}

const CATEGORY_LABEL_KEY: Record<NotificationCategory, string> = {
  AiJob: 'category.aiJob',
  CriticalResult: 'category.criticalResult',
  PeerReview: 'category.peerReview',
  RulebookApproval: 'category.rulebookApproval',
  TemplateApproval: 'category.templateApproval',
  Mention: 'category.mention',
  System: 'category.system',
};

const URGENCY_LABEL_KEY: Record<NotificationUrgency, string> = {
  Info: 'urgency.info',
  Warning: 'urgency.warning',
  Critical: 'urgency.critical',
};

/** next-intl key (under the `notifications` namespace) for a category's display
 *  label. Unknown/future categories fall back to a generic key. */
export function categoryLabelKey(category: string): string {
  return CATEGORY_LABEL_KEY[category as NotificationCategory] ?? 'category.other';
}

/** next-intl key (under `notifications`) for an urgency's display label. */
export function urgencyLabelKey(urgency: string): string {
  return URGENCY_LABEL_KEY[urgency as NotificationUrgency] ?? 'urgency.info';
}

// ---------------------------------------------------------------------------
// Relative time (terse, locale-neutral units)
// ---------------------------------------------------------------------------

/** "now" / "3m" / "2h" / "5d" / a short date. Terse units keep the timestamp
 *  compact in the bell row and the offline banner. */
export function relativeTime(iso: string, now: number): string {
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return '';
  const diff = Math.max(0, now - t);
  const s = Math.floor(diff / 1000);
  if (s < 45) return 'now';
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h`;
  const d = Math.floor(h / 24);
  if (d < 7) return `${d}d`;
  return new Date(t).toLocaleDateString();
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Max session-only transient notes kept (matches the old bell). */
export const TRANSIENT_CAP = 5;

/** Fallback unread-count poll cadence while the SSE stream is not `open`. */
export const UNREAD_POLL_MS = 60_000;

/** The inbox is "stale" once the stream is down AND the last good fetch is older
 *  than this (or the browser is offline). */
export const STALE_AFTER_MS = 2 * 60_000;

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

export interface NotificationsState {
  /** Server rows, newest first. Holds the recent page for the bell (limit 20). */
  items: NotificationItem[];
  /** Session-only transient notes, newest first, capped at TRANSIENT_CAP. */
  transients: TransientNote[];
  /** Transient notes not yet seen (reset to 0 on popover open). */
  transientUnseen: number;
  /** Authoritative unread count from `/unread-count`, optimistically adjusted. */
  serverUnread: number;
  /** Authoritative unacknowledged (RequiresAck) count, optimistically adjusted. */
  serverUnacked: number;
  /** Keyset cursor for the bell's list (older rows live on the inbox page). */
  nextCursor: string | null;
  /** True once the initial list has hydrated (drives the bell's loaded UX). */
  loaded: boolean;
}

export type NotificationsAction =
  | {
      type: 'HYDRATE';
      items?: NotificationItem[];
      nextCursor?: string | null;
      unread?: number;
      unacked?: number;
    }
  | { type: 'RECEIVE_SSE'; item: NotificationItem }
  | { type: 'MARK_READ'; id: string }
  | { type: 'ROLLBACK_READ'; previous: NotificationItem }
  | { type: 'ACK'; id: string }
  | { type: 'ROLLBACK_ACK'; previous: NotificationItem }
  | { type: 'RECEIVE_TRANSIENT'; note: TransientNote }
  | { type: 'CLEAR_TRANSIENT' };

export function initialNotificationsState(): NotificationsState {
  return {
    items: [],
    transients: [],
    transientUnseen: 0,
    serverUnread: 0,
    serverUnacked: 0,
    nextCursor: null,
    loaded: false,
  };
}

function cmpCreatedDesc(a: NotificationItem, b: NotificationItem): number {
  return Date.parse(b.createdAt) - Date.parse(a.createdAt);
}

/** Merge one incoming server row over an existing one. Server fields win EXCEPT a
 *  locally-optimistic `readAt`/`acknowledgedAt` the server has not yet confirmed
 *  (server null but local set) — kept until a later hydrate confirms it. Mirrors
 *  `lib/jobs.ts`'s HYDRATE merge doctrine. */
function mergeRow(prev: NotificationItem, incoming: NotificationItem): NotificationItem {
  return {
    ...incoming,
    readAt: incoming.readAt ?? prev.readAt,
    acknowledgedAt: incoming.acknowledgedAt ?? prev.acknowledgedAt,
  };
}

function mergeServerItems(
  existing: NotificationItem[],
  incoming: NotificationItem[],
): NotificationItem[] {
  const byId = new Map(existing.map((n) => [n.id, n] as const));
  for (const inc of incoming) {
    const prev = byId.get(inc.id);
    byId.set(inc.id, prev ? mergeRow(prev, inc) : inc);
  }
  return Array.from(byId.values()).sort(cmpCreatedDesc);
}

function replaceById(items: NotificationItem[], next: NotificationItem): NotificationItem[] {
  return items.map((n) => (n.id === next.id ? next : n));
}

export function notificationsReducer(
  state: NotificationsState,
  action: NotificationsAction,
): NotificationsState {
  switch (action.type) {
    case 'HYDRATE': {
      const items = action.items ? mergeServerItems(state.items, action.items) : state.items;
      return {
        ...state,
        items,
        nextCursor: action.items ? action.nextCursor ?? null : state.nextCursor,
        serverUnread: action.unread ?? state.serverUnread,
        serverUnacked: action.unacked ?? state.serverUnacked,
        loaded: action.items ? true : state.loaded,
      };
    }

    case 'RECEIVE_SSE': {
      const inc = action.item;
      const idx = state.items.findIndex((n) => n.id === inc.id);
      if (idx >= 0) {
        // Idempotent: a duplicate / out-of-order delivery patches the row in place
        // and NEVER bumps the counts or adds a second row.
        const items = replaceById(state.items, mergeRow(state.items[idx], inc));
        return { ...state, items };
      }
      const items = [inc, ...state.items];
      return {
        ...state,
        items,
        serverUnread: state.serverUnread + (inc.readAt ? 0 : 1),
        serverUnacked:
          state.serverUnacked + (inc.requiresAck && !inc.acknowledgedAt ? 1 : 0),
      };
    }

    case 'MARK_READ': {
      const idx = state.items.findIndex((n) => n.id === action.id);
      if (idx < 0) return state;
      const item = state.items[idx];
      if (item.readAt) return state; // already read — idempotent
      const items = replaceById(state.items, { ...item, readAt: new Date().toISOString() });
      return { ...state, items, serverUnread: Math.max(0, state.serverUnread - 1) };
    }

    case 'ROLLBACK_READ': {
      // Restore the exact pre-mutation row and re-add its unread contribution.
      const prev = action.previous;
      if (!state.items.some((n) => n.id === prev.id)) return state;
      const items = replaceById(state.items, prev);
      return { ...state, items, serverUnread: state.serverUnread + (prev.readAt ? 0 : 1) };
    }

    case 'ACK': {
      const idx = state.items.findIndex((n) => n.id === action.id);
      if (idx < 0) return state;
      const item = state.items[idx];
      if (!item.requiresAck || item.acknowledgedAt) return state;
      const now = new Date().toISOString();
      const wasUnread = !item.readAt;
      const items = replaceById(state.items, {
        ...item,
        acknowledgedAt: now,
        readAt: item.readAt ?? now,
      });
      return {
        ...state,
        items,
        serverUnacked: Math.max(0, state.serverUnacked - 1),
        serverUnread: wasUnread ? Math.max(0, state.serverUnread - 1) : state.serverUnread,
      };
    }

    case 'ROLLBACK_ACK': {
      const prev = action.previous;
      if (!state.items.some((n) => n.id === prev.id)) return state;
      const items = replaceById(state.items, prev);
      return {
        ...state,
        items,
        serverUnacked: state.serverUnacked + (prev.requiresAck && !prev.acknowledgedAt ? 1 : 0),
        serverUnread: state.serverUnread + (prev.readAt ? 0 : 1),
      };
    }

    case 'RECEIVE_TRANSIENT': {
      const transients = [action.note, ...state.transients].slice(0, TRANSIENT_CAP);
      return { ...state, transients, transientUnseen: state.transientUnseen + 1 };
    }

    case 'CLEAR_TRANSIENT': {
      if (state.transientUnseen === 0) return state;
      return { ...state, transientUnseen: 0 };
    }

    default:
      return state;
  }
}

/** Badge count for the bell = authoritative server unread + unseen transients. */
export function unreadTotal(state: NotificationsState): number {
  return state.serverUnread + state.transientUnseen;
}

// ---------------------------------------------------------------------------
// Pure state→UI selectors (NOTIF-010) — unit-tested without rendering
// ---------------------------------------------------------------------------

/** Which primary element the inbox list region shows. */
export type ListUiState = 'loading' | 'error' | 'empty' | 'ready';

export function selectListUiState(input: {
  loading: boolean;
  error: boolean;
  count: number;
}): ListUiState {
  if (input.error) return 'error';
  if (input.loading) return 'loading';
  if (input.count === 0) return 'empty';
  return 'ready';
}

/** The amber stale/offline banner shows when the stream is NOT open AND either the
 *  browser is offline or the last good fetch is older than {@link STALE_AFTER_MS}. */
export function isInboxStale(input: {
  streamOpen: boolean;
  online: boolean;
  lastFetchAt: number | null;
  now: number;
}): boolean {
  if (input.streamOpen) return false;
  if (!input.online) return true;
  if (input.lastFetchAt == null) return false;
  return input.now - input.lastFetchAt > STALE_AFTER_MS;
}
