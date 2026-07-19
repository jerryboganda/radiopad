// F3 — the snippets manager must add, list, and delete device-local snippets, and show fill-in
// field counts. Backed by the real localStorage store (no API).
import { describe, it, expect, beforeEach } from 'vitest';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import SnippetsPage from '@/app/(desktop)/settings/snippets/page';
import { SNIPPET_STORAGE_KEY, _resetSnippets, getSnippets } from '@/lib/snippets';

// next/link → plain anchor (no app-router in unit tests).
import { vi } from 'vitest';
vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href: string }) => <a href={href}>{children}</a>,
}));

beforeEach(() => {
  window.localStorage.removeItem(SNIPPET_STORAGE_KEY);
  _resetSnippets();
});

describe('SnippetsPage', () => {
  it('adds a snippet and lists it with its fill-in field count', async () => {
    render(<SnippetsPage />);
    expect(screen.getByText(/no snippets yet/i)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/trigger/i), { target: { value: 'nlchest' } });
    fireEvent.change(screen.getByLabelText(/expands to/i), {
      target: { value: 'The ${vessel} is patent.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));

    await waitFor(() => expect(screen.getByText('nlchest')).toBeInTheDocument());
    expect(screen.getByText(/1 fill-in field/i)).toBeInTheDocument();
  });

  it('rejects a snippet with an empty trigger or body', () => {
    render(<SnippetsPage />);
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));
    expect(screen.getByRole('alert')).toHaveTextContent(/enter both/i);
    expect(screen.getByText(/no snippets yet/i)).toBeInTheDocument();
  });

  it('deletes a snippet', async () => {
    render(<SnippetsPage />);
    fireEvent.change(screen.getByLabelText(/trigger/i), { target: { value: 'nl' } });
    fireEvent.change(screen.getByLabelText(/expands to/i), { target: { value: 'No acute abnormality.' } });
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));
    await waitFor(() => expect(screen.getByText('nl')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /delete nl/i }));
    await waitFor(() => expect(screen.getByText(/no snippets yet/i)).toBeInTheDocument());
  });

  // The clash branch set an error message and then fell straight through to saveSnippet() +
  // resetForm(), which nulls the error again — so the warning never rendered, and saveSnippet with
  // no id matches on trigger and REPLACES the existing body. Re-using a trigger silently destroyed
  // the snippet already stored under it.
  it('refuses to overwrite an existing trigger, and says so', async () => {
    render(<SnippetsPage />);

    fireEvent.change(screen.getByLabelText(/trigger/i), { target: { value: 'nlchest' } });
    fireEvent.change(screen.getByLabelText(/expands to/i), { target: { value: 'The original block.' } });
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));
    await waitFor(() => expect(screen.getByText('nlchest')).toBeInTheDocument());

    // Same trigger, different case, different body — this is the destructive path.
    fireEvent.change(screen.getByLabelText(/trigger/i), { target: { value: 'NLCHEST' } });
    fireEvent.change(screen.getByLabelText(/expands to/i), { target: { value: 'Something else entirely.' } });
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/already exists/i);
    // The stored snippet survives untouched — asserted against the store, because the rejected
    // text legitimately stays in the textarea for the user to correct.
    const stored = getSnippets();
    expect(stored).toHaveLength(1);
    expect(stored[0].body).toBe('The original block.');
  });

  // Expansion matches the single whitespace-delimited word before the caret (/(\S+)$/), so a
  // trigger containing a space can never be typed into existence. The form accepted them anyway,
  // leaving snippets that look saved and simply never fire.
  it('rejects a trigger containing whitespace instead of saving a dead one', () => {
    render(<SnippetsPage />);

    fireEvent.change(screen.getByLabelText(/trigger/i), { target: { value: 'no acute' } });
    fireEvent.change(screen.getByLabelText(/expands to/i), { target: { value: 'No acute abnormality.' } });
    fireEvent.click(screen.getByRole('button', { name: /add snippet/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/single word|space/i);
    expect(screen.getByText(/no snippets yet/i)).toBeInTheDocument();
  });
});
