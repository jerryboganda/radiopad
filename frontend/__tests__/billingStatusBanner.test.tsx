// A failed poll must not hide a suspension notice that is already on screen.
import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import * as React from 'react';

const statusMock = vi.fn();
vi.mock('@/lib/api', () => ({ api: { billing: { status: () => statusMock() } } }));
vi.mock('next/link', () => ({
  default: ({ href, children }: React.PropsWithChildren<{ href: string }>) => <a href={href}>{children}</a>,
}));

import BillingStatusBanner from '@/components/BillingStatusBanner';

describe('BillingStatusBanner', () => {
  afterEach(() => vi.useRealTimers());

  it('keeps the last known status when a later poll fails', async () => {
    vi.useFakeTimers();
    statusMock
      .mockResolvedValueOnce({ suspendedAt: '2026-11-20T00:00:00Z', gracePeriodUntil: null })
      .mockRejectedValueOnce(new Error('network'));
    render(<BillingStatusBanner />);

    // findBy*/waitFor poll on timers, which are faked here — flush the first load by hand.
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(screen.getByRole('alert')).toHaveTextContent(/billing suspended/i);

    await act(async () => { await vi.advanceTimersByTimeAsync(5 * 60 * 1000); }); // next poll rejects
    expect(statusMock).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('alert')).toHaveTextContent(/billing suspended/i);
  });
});
