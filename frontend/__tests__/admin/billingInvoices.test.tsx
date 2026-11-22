/**
 * Billing page — `/api/billing/invoices` answers 503 when Stripe is not configured for the
 * tenant. That is an expected state: show the server's message calmly, not as a warning banner.
 */
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import * as React from 'react';

const { pending, invoicesMock } = vi.hoisted(() => ({
  pending: () => new Promise<never>(() => {}),
  invoicesMock: vi.fn(),
}));

vi.mock('@/lib/useAuthSession', () => ({
  useAuthSession: () => ({ loading: false, signedOut: false }),
  isAuthError: (e: { status?: number }) => e?.status === 401 || e?.status === 403,
}));
vi.mock('@/lib/api', () => ({
  publicEnv: () => '',
  api: {
    billing: { status: pending, features: pending, credits: pending, invoices: () => invoicesMock() },
    analytics: { summary: pending },
  },
}));
vi.mock('next/link', () => ({
  default: ({ href, children }: React.PropsWithChildren<{ href: string }>) => <a href={href}>{children}</a>,
}));

import BillingDashboardPage from '@/app/(web)/admin/billing/page';

describe('billing invoices', () => {
  it('shows the 503 message as an info state, not a warning banner', async () => {
    invoicesMock.mockRejectedValue(
      Object.assign(new Error('API 503 Service Unavailable: Stripe is not configured.'), {
        status: 503,
        body: { error: 'Stripe is not configured.', kind: 'provider_unavailable' },
      }),
    );
    render(<BillingDashboardPage />);

    expect(await screen.findByText('Stripe is not configured.')).toBeInTheDocument();
    expect(screen.getByText("Invoices aren't available yet")).toBeInTheDocument();
    expect(screen.queryByText('No invoices issued yet')).toBeNull();
    expect(document.querySelector('.rp-banner-warn')).toBeNull();
  });
});
