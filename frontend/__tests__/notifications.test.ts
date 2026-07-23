import { describe, it, expect } from 'vitest';
import {
  initialNotificationsState,
  notificationsReducer,
  unreadTotal,
  type NotificationItem,
  type NotificationsState,
} from '@/lib/notifications';

// The pure NOTIF-001 model: HYDRATE merge doctrine, idempotent SSE upsert,
// optimistic read + rollback, transient cap/clear, and the unread-total math.
// No React, no network.

function item(p: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'n1',
    category: 'AiJob',
    urgency: 'Info',
    title: 'AI result ready',
    body: 'CT Chest',
    requiresAck: false,
    createdAt: new Date(1_000).toISOString(),
    ...p,
  };
}

function hydrate(
  state: NotificationsState,
  items: NotificationItem[],
  unread = 0,
  unacked = 0,
): NotificationsState {
  return notificationsReducer(state, { type: 'HYDRATE', items, unread, unacked });
}

describe('notificationsReducer', () => {
  it('HYDRATE merge preserves an optimistic ReadAt the server has not confirmed', () => {
    let s = hydrate(initialNotificationsState(), [item({ id: 'n1' })], 1);
    // Optimistically mark it read locally.
    s = notificationsReducer(s, { type: 'MARK_READ', id: 'n1' });
    const optimisticRead = s.items[0].readAt;
    expect(optimisticRead).toBeTruthy();
    expect(s.serverUnread).toBe(0);

    // A later server list still shows it unread (readAt null) — the optimistic
    // value must survive until the server confirms it.
    s = hydrate(s, [item({ id: 'n1', readAt: undefined })], 1);
    expect(s.items[0].readAt).toBe(optimisticRead);
  });

  it('RECEIVE_SSE is idempotent by id — a duplicate delivery adds no row or count', () => {
    let s = notificationsReducer(initialNotificationsState(), {
      type: 'RECEIVE_SSE',
      item: item({ id: 'a' }),
    });
    expect(s.items).toHaveLength(1);
    expect(s.serverUnread).toBe(1);

    s = notificationsReducer(s, { type: 'RECEIVE_SSE', item: item({ id: 'a' }) });
    expect(s.items).toHaveLength(1); // no duplicate row
    expect(s.serverUnread).toBe(1); // no double count
  });

  it('RECEIVE_SSE bumps the unacked count only for an unacked RequiresAck row', () => {
    let s = notificationsReducer(initialNotificationsState(), {
      type: 'RECEIVE_SSE',
      item: item({ id: 'crit', requiresAck: true, urgency: 'Critical' }),
    });
    expect(s.serverUnread).toBe(1);
    expect(s.serverUnacked).toBe(1);
    // An already-acknowledged row does not raise the unacked count.
    s = notificationsReducer(s, {
      type: 'RECEIVE_SSE',
      item: item({ id: 'crit2', requiresAck: true, acknowledgedAt: new Date().toISOString() }),
    });
    expect(s.serverUnacked).toBe(1);
  });

  it('caps transients at 5 and CLEAR_TRANSIENT resets only the unseen count', () => {
    let s = initialNotificationsState();
    for (let i = 0; i < 7; i++) {
      s = notificationsReducer(s, {
        type: 'RECEIVE_TRANSIENT',
        note: { id: `t${i}`, title: `toast ${i}`, at: i },
      });
    }
    expect(s.transients).toHaveLength(5); // capped
    expect(s.transients[0].id).toBe('t6'); // newest first
    expect(s.transientUnseen).toBe(7);

    s = notificationsReducer(s, { type: 'CLEAR_TRANSIENT' });
    expect(s.transientUnseen).toBe(0);
    expect(s.transients).toHaveLength(5); // the list itself persists (same UX as today)
  });

  it('unreadTotal sums server unread and unseen transients', () => {
    let s = hydrate(initialNotificationsState(), [], 3, 1);
    s = notificationsReducer(s, {
      type: 'RECEIVE_TRANSIENT',
      note: { id: 't', title: 'x', at: 1 },
    });
    expect(unreadTotal(s)).toBe(4);
  });

  it('MARK_READ is optimistic and ROLLBACK_READ restores the unread state on failure', () => {
    let s = hydrate(initialNotificationsState(), [item({ id: 'n1' })], 1);
    const previous = s.items[0];

    s = notificationsReducer(s, { type: 'MARK_READ', id: 'n1' });
    expect(s.items[0].readAt).toBeTruthy();
    expect(s.serverUnread).toBe(0);

    s = notificationsReducer(s, { type: 'ROLLBACK_READ', previous });
    expect(s.items[0].readAt).toBeUndefined();
    expect(s.serverUnread).toBe(1);
  });

  it('ACK is optimistic (read + ack) and ROLLBACK_ACK restores both counts', () => {
    let s = hydrate(
      initialNotificationsState(),
      [item({ id: 'c1', requiresAck: true, urgency: 'Critical' })],
      1,
      1,
    );
    const previous = s.items[0];

    s = notificationsReducer(s, { type: 'ACK', id: 'c1' });
    expect(s.items[0].acknowledgedAt).toBeTruthy();
    expect(s.items[0].readAt).toBeTruthy(); // ack implies read
    expect(s.serverUnacked).toBe(0);
    expect(s.serverUnread).toBe(0);

    s = notificationsReducer(s, { type: 'ROLLBACK_ACK', previous });
    expect(s.items[0].acknowledgedAt).toBeUndefined();
    expect(s.serverUnacked).toBe(1);
    expect(s.serverUnread).toBe(1);
  });
});
