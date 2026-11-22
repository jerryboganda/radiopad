// The verifier's verdict is the server's full-chain walk; the browser must not recompute over a
// recent window (row 1 of a window has an unknown predecessor → false "broken" for >500 events).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';
import { api } from '@/lib/api';
import AuditVerifyPage from '@/app/(desktop)/audit/verify/page';

const EVENTS = [
  { id: 'aaaaaaaa-1', action: 1, integrityChain: 'abc123abc123abc123', createdAt: '2026-01-02T00:00:00Z' },
  { id: 'bbbbbbbb-2', action: 2, integrityChain: 'def456def456def456', createdAt: '2026-01-01T00:00:00Z' },
];

describe('AuditVerifyPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(api.audit, 'query').mockResolvedValue(EVENTS);
  });

  it('shows the server verdict: intact', async () => {
    vi.spyOn(api.audit, 'verify').mockResolvedValue({ intact: true, eventCount: 1234, lastVerifiedAt: null });
    render(<AuditVerifyPage />);
    await screen.findByText(/Latest 2 events/);
    fireEvent.click(screen.getByRole('button', { name: /verify audit chain/i }));
    await waitFor(() => expect(screen.getByText(/Chain intact \(1234 events\)/)).toBeTruthy());
  });

  it('flags the first broken row the server reports', async () => {
    vi.spyOn(api.audit, 'verify').mockResolvedValue({
      intact: false, eventCount: 1234, firstBrokenEventId: 'bbbbbbbb-2', lastVerifiedAt: null,
    });
    render(<AuditVerifyPage />);
    await screen.findByText(/Latest 2 events/);
    fireEvent.click(screen.getByRole('button', { name: /verify audit chain/i }));
    await waitFor(() => expect(screen.getByText(/Chain broken/)).toBeTruthy());
    expect(screen.getAllByText('first broken row')).toHaveLength(1);
  });
});
