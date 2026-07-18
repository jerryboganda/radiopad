// F5 — the priors tray must offer a conventional "Compared to …" statement built from the prior
// and insert it into the Comparison section editor on click.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, fireEvent } from '@testing-library/react';
import PriorComparePanel from '@/app/(desktop)/reports/[id]/PriorComparePanel';

const comparePrior = vi.fn();
const prior = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      comparePrior: (...a: unknown[]) => comparePrior(...a),
      prior: (...a: unknown[]) => prior(...a),
    },
  },
}));

const getSectionEditor = vi.fn();
vi.mock('@/lib/editor/sectionEditorRegistry', () => ({
  getSectionEditor: (...a: unknown[]) => getSectionEditor(...a),
}));

beforeEach(() => {
  comparePrior.mockReset();
  prior.mockReset();
  getSectionEditor.mockReset();
});

const STATEMENT = 'Compared to the prior chest study dated January 2, 2026.';

function withPrior() {
  comparePrior.mockResolvedValue({
    current: { id: 'r1', bodyPart: 'Chest' },
    prior: { id: 'p1abcdef', bodyPart: 'Chest', createdAt: '2026-01-02T00:00:00Z' },
    sections: [{ section: 'findings', current: 'a', prior: 'b', changed: true }],
  });
}

describe('PriorComparePanel — F5 comparison statement', () => {
  it('shows the statement and inserts it into the Comparison editor', async () => {
    withPrior();
    const insertAtCursor = vi.fn();
    const focus = vi.fn();
    getSectionEditor.mockReturnValue({ sectionKey: 'comparison', insertAtCursor, focus });

    const { findByText, getByRole } = render(<PriorComparePanel reportId="r1" />);
    await findByText(STATEMENT);

    fireEvent.click(getByRole('button', { name: /insert into comparison/i }));

    expect(getSectionEditor).toHaveBeenCalledWith('comparison');
    expect(insertAtCursor).toHaveBeenCalledWith(STATEMENT);
    await findByText(/inserted/i);
  });

  it('does not render a statement when there is no prior', async () => {
    comparePrior.mockResolvedValue({
      current: { id: 'r1', bodyPart: 'Chest' },
      prior: null,
      sections: [],
    });
    const { findByText, queryByRole } = render(<PriorComparePanel reportId="r1" />);
    await findByText(/no prior studies found/i);
    expect(queryByRole('button', { name: /insert into comparison/i })).toBeNull();
  });
});
