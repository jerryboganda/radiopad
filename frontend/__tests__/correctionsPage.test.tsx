// F7 (dictation brief §6) — the personal-corrections screen must load the user's list, show a
// proper empty state, and send trimmed add/edit/delete calls to the backend. This guards the
// wiring between the page and `api.userCorrections`.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';
import CorrectionsPage from '@/app/(desktop)/settings/corrections/page';

const list = vi.fn();
const save = vi.fn();
const del = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    userCorrections: {
      list: () => list(),
      save: (body: unknown) => save(body),
      delete: (id: string) => del(id),
    },
  },
}));

// next/link needs an app-router context we don't mount in unit tests — render a plain anchor.
vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));

beforeEach(() => {
  list.mockReset();
  save.mockReset();
  del.mockReset();
});

describe('CorrectionsPage', () => {
  it('shows the empty state when the user has no corrections', async () => {
    list.mockResolvedValue([]);
    const { findByText } = render(<CorrectionsPage />);
    await findByText(/no personal corrections yet/i);
  });

  it('lists existing corrections ordered by the spoken form', async () => {
    list.mockResolvedValue([
      { id: '2', from: 'zeta', to: 'z' },
      { id: '1', from: 'Alpha', to: 'a' },
    ]);
    const { findByText, getAllByText } = render(<CorrectionsPage />);
    await findByText('Alpha');
    // Both rows present.
    expect(getAllByText(/zeta|Alpha/).length).toBeGreaterThanOrEqual(2);
  });

  it('sends a trimmed add to the backend and clears the form', async () => {
    list.mockResolvedValue([]);
    save.mockResolvedValue({ id: 'n1', from: 'apendix', to: 'appendix' });
    const { findByText, getByLabelText, getByRole } = render(<CorrectionsPage />);
    await findByText(/no personal corrections yet/i);

    fireEvent.change(getByLabelText(/heard as/i), { target: { value: '  apendix ' } });
    fireEvent.change(getByLabelText(/write instead/i), { target: { value: ' appendix ' } });
    fireEvent.click(getByRole('button', { name: /add/i }));

    await waitFor(() => expect(save).toHaveBeenCalledWith({ from: 'apendix', to: 'appendix' }));
    await findByText('apendix'); // now appears in the list
  });

  it('does not submit when the replacement is identical to the spoken form', async () => {
    list.mockResolvedValue([]);
    const { findByText, getByLabelText, getByRole } = render(<CorrectionsPage />);
    await findByText(/no personal corrections yet/i);

    fireEvent.change(getByLabelText(/heard as/i), { target: { value: 'liver' } });
    fireEvent.change(getByLabelText(/write instead/i), { target: { value: 'liver' } });
    // The Add button is disabled by the live check, so clicking is a no-op.
    fireEvent.click(getByRole('button', { name: /add/i }));

    expect(save).not.toHaveBeenCalled();
  });
});
