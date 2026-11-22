// Sign & Send signs what the radiologist SEES: on-screen edits are saved first (beforeRun),
// and if that save fails nothing is signed. After success the editor is told to refresh
// (onDone), so its DRAFT badge does not linger on a signed, exported report.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';
import { api } from '@/lib/api';

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

import SignAndSendButton from '@/app/(desktop)/reports/[id]/SignAndSendButton';

async function confirm() {
  fireEvent.click(screen.getByRole('button', { name: 'Sign & Send' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm sign-off' }));
}

describe('SignAndSendButton', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('saves on-screen edits before signing, then tells the editor to refresh', async () => {
    const order: string[] = [];
    vi.spyOn(api.reports, 'signAndSend').mockImplementation(async () => { order.push('signAndSend'); return {} as never; });
    const beforeRun = vi.fn(async () => { order.push('save'); });
    const onDone = vi.fn(() => { order.push('refresh'); });
    render(<SignAndSendButton reportId="r1" beforeRun={beforeRun} onDone={onDone} />);
    await confirm();
    await waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(order).toEqual(['save', 'signAndSend', 'refresh']);
    expect(screen.getByRole('button', { name: 'Next case' })).toBeTruthy();
  });

  it('signs nothing when the save fails', async () => {
    const signAndSend = vi.spyOn(api.reports, 'signAndSend').mockResolvedValue({} as never);
    const onDone = vi.fn();
    render(
      <SignAndSendButton
        reportId="r1"
        beforeRun={async () => { throw new Error('Changes are not yet synced.'); }}
        onDone={onDone}
      />,
    );
    await confirm();
    await waitFor(() => expect(screen.getByText('Changes are not yet synced.')).toBeTruthy());
    expect(signAndSend).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });
});
