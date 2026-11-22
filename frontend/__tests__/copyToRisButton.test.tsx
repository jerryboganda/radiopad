// RIS copy pastes what the radiologist SEES: on-screen edits are saved first (beforeCopy),
// and a failed save copies nothing rather than putting stale text on the clipboard.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';
import { api } from '@/lib/api';

vi.mock('@/components/ui/ToastProvider', () => ({ useToast: () => ({ toast: vi.fn() }) }));

import CopyToRisButton from '@/app/(desktop)/reports/[id]/CopyToRisButton';

describe('CopyToRisButton', () => {
  const writeText = vi.fn(async (_t: string) => {});
  beforeEach(() => {
    vi.restoreAllMocks();
    writeText.mockClear();
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText, readText: vi.fn(async () => '') } });
  });

  it('saves on-screen edits before fetching the text it copies', async () => {
    const order: string[] = [];
    vi.spyOn(api.reports, 'exportText').mockImplementation(async () => { order.push('export'); return 'FINDINGS: latest'; });
    render(<CopyToRisButton reportId="r1" beforeCopy={async () => { order.push('save'); }} />);
    fireEvent.click(screen.getByRole('button', { name: 'Copy report to clipboard for RIS' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('FINDINGS: latest'));
    expect(order).toEqual(['save', 'export']);
  });

  it('clears the clipboard after 30 s even when Windows rewrote the line endings', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const readText = vi.fn(async () => 'INDICATION:\r\nHeadache');
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText, readText } });
      vi.spyOn(api.reports, 'exportText').mockResolvedValue('INDICATION:\nHeadache');
      render(<CopyToRisButton reportId="r1" />);
      fireEvent.click(screen.getByRole('button', { name: 'Copy report to clipboard for RIS' }));
      await waitFor(() => expect(writeText).toHaveBeenCalledWith('INDICATION:\nHeadache'));
      await vi.advanceTimersByTimeAsync(30_000);
      expect(writeText).toHaveBeenLastCalledWith('');
    } finally {
      vi.useRealTimers();
    }
  });

  it('leaves the clipboard alone if the user copied something else since', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText, readText: vi.fn(async () => 'my own note') } });
      vi.spyOn(api.reports, 'exportText').mockResolvedValue('INDICATION:\nHeadache');
      render(<CopyToRisButton reportId="r1" />);
      fireEvent.click(screen.getByRole('button', { name: 'Copy report to clipboard for RIS' }));
      await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
      await vi.advanceTimersByTimeAsync(30_000);
      expect(writeText).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('copies nothing when the save fails', async () => {
    const exportText = vi.spyOn(api.reports, 'exportText').mockResolvedValue('stale');
    render(<CopyToRisButton reportId="r1" beforeCopy={async () => { throw new Error('Changes are not yet synced.'); }} />);
    fireEvent.click(screen.getByRole('button', { name: 'Copy report to clipboard for RIS' }));
    await waitFor(() => expect(screen.getByText('Changes are not yet synced.')).toBeTruthy());
    expect(exportText).not.toHaveBeenCalled();
    expect(writeText).not.toHaveBeenCalled();
  });
});
