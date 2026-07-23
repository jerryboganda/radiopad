import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import * as React from 'react';
import { NextIntlClientProvider } from 'next-intl';
import messages from '@/messages/en.json';

// Integration test of the reworked bell: it renders the two feeds (session-only
// transients + persistent server rows) from the REAL NotificationsProvider. Only
// `@/lib/api` and `@/lib/events` (+ next/navigation) are mocked. The regression
// guard is that the existing `rp-notify` CustomEvent is still ingested — job
// toasts must keep flowing into the bell.

const m = vi.hoisted(() => ({
  list: vi.fn(),
  unreadCount: vi.fn(),
  markRead: vi.fn(),
  ack: vi.fn(),
  bulk: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/lib/api', () => ({
  api: {
    notifications: {
      list: m.list,
      unreadCount: m.unreadCount,
      markRead: m.markRead,
      ack: m.ack,
      bulk: m.bulk,
    },
  },
}));
vi.mock('@/lib/events', () => ({
  hostedEvents: {
    subscribe: vi.fn(() => () => {}),
    onStatus: vi.fn(() => () => {}),
    status: 'open',
  },
}));

import NotificationsProvider from '@/components/notifications/NotificationsProvider';
import NotificationsBell from '@/components/shell/NotificationsBell';

function serverRow(id: string, extra: Record<string, unknown> = {}) {
  return {
    id,
    category: 'AiJob',
    urgency: 'Info',
    title: 'AI result ready',
    body: 'CT Chest',
    requiresAck: false,
    readAt: undefined,
    acknowledgedAt: undefined,
    createdAt: new Date(1_000).toISOString(),
    ...extra,
  };
}

function renderBell() {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <NotificationsProvider>
        <NotificationsBell />
      </NotificationsProvider>
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  m.list.mockResolvedValue({ notifications: [serverRow('n1')], nextCursor: null });
  m.unreadCount.mockResolvedValue({ unread: 1, unacked: 0 });
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('NotificationsBell', () => {
  it('shows both the transient and server sections in order, with the summed badge', async () => {
    renderBell();

    // Hydration populates one unread server row → badge shows 1.
    await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument());

    // Fire a job toast on the existing rp-notify channel (regression guard).
    await act(async () => {
      window.dispatchEvent(
        new CustomEvent('rp-notify', {
          detail: { title: 'Draft generation ready', tone: 'success' },
        }),
      );
    });

    // Badge is now server unread (1) + unseen transient (1) = 2.
    await waitFor(() => expect(screen.getByText('2')).toBeInTheDocument());

    // Open the popover.
    await act(async () => {
      screen.getByRole('button', { name: 'Notifications' }).click();
    });

    const popover = screen.getByRole('menu');
    const text = popover.textContent ?? '';
    // Both sections render, session block first, then the persistent inbox.
    expect(text).toContain('This session');
    expect(text).toContain('Inbox');
    expect(text.indexOf('This session')).toBeLessThan(text.indexOf('Inbox'));
    // Both feeds' content is present.
    expect(popover).toHaveTextContent('Draft generation ready'); // transient
    expect(popover).toHaveTextContent('AI result ready'); // server row
    // Desktop surface → the "View all" footer link is present.
    expect(screen.getByText('View all')).toBeInTheDocument();
  });

  it('marks an unread server row read and follows its link on click', async () => {
    m.list.mockResolvedValue({
      notifications: [serverRow('n2', { linkHref: '/reports/view?id=r1' })],
      nextCursor: null,
    });
    m.markRead.mockResolvedValue(undefined);

    renderBell();
    await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument());

    await act(async () => {
      screen.getByRole('button', { name: 'Notifications' }).click();
    });
    await act(async () => {
      screen.getByText('AI result ready').click();
    });

    expect(m.markRead).toHaveBeenCalledWith('n2');
  });
});
