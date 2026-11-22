/**
 * /admin/security — alerts come from the server-side action filter (rows carry the AuditAction
 * enum as a NUMBER, so a client-side name match can never find them), and the uptime tile never
 * claims 100% from zero probes.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import * as React from 'react';

const searchMock = vi.fn();
const availabilityMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    siem: { status: () => Promise.resolve({ sinks: [] }) },
    tenant: { settings: { get: () => Promise.resolve({ ipAllowlistJson: '' }), save: vi.fn() } },
    audit: { search: (p: unknown) => searchMock(p) },
    admin: { observability: { availability: () => availabilityMock() } },
    security: { testWebhook: vi.fn() },
  },
}));

vi.mock('@/components/ui/PermissionGate', () => ({
  default: ({ children }: React.PropsWithChildren) => <>{children}</>,
}));

import SecurityAdminPage from '@/app/(web)/admin/security/page';

describe('admin/security', () => {
  beforeEach(() => {
    searchMock.mockReset();
    availabilityMock.mockReset();
    searchMock.mockResolvedValue([]);
  });

  it('asks the server for SecurityAlert rows and lists them', async () => {
    searchMock.mockResolvedValue([
      { id: 'a1', action: 34, detailsJson: '{"reason":"login failure burst"}', createdAt: '2026-11-22T10:00:00Z' },
    ]);
    availabilityMock.mockResolvedValue({ windowSec: 300, totalProbes: 10, errorCount: 0, errorRate: 0, lastCheckedAt: null, targets: ['api'] });
    render(<SecurityAdminPage />);

    expect(await screen.findByText('login failure burst')).toBeInTheDocument();
    expect(searchMock).toHaveBeenCalledWith({ action: 'SecurityAlert', take: 50 });
  });

  it('shows "No probes yet" instead of 100% uptime when nothing has been probed', async () => {
    availabilityMock.mockResolvedValue({ windowSec: 300, totalProbes: 0, errorCount: 0, errorRate: 0, lastCheckedAt: null, targets: [] });
    render(<SecurityAdminPage />);

    expect(await screen.findByText('No probes yet')).toBeInTheDocument();
    expect(screen.getByText('Uptime (last 5 min)')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('100.00%')).toBeNull());
  });
});
