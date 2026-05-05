// Iter-36 MOB — `/mobile/reports/[reportId]/edit` page test. Six
// section panels render, each `<details>` opens on tap, the textarea
// is wired to `api.reports.patch`, and AI-flagged sections wear the
// `.ai-mark` wrapper.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor, act } from '@testing-library/react';

vi.mock('next/navigation', () => ({
  useParams: () => ({ reportId: 'rpt-1' }),
  useRouter: () => ({ push: vi.fn() }),
}));

const get = vi.fn();
const patch = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      get: (...args: unknown[]) => get(...args),
      patch: (...args: unknown[]) => patch(...args),
    },
  },
}));

import Page from '@/app/mobile/reports/[reportId]/edit/page';

const sample = {
  id: 'rpt-1',
  indication: 'cough',
  technique: 'CT',
  comparison: '',
  findings: 'lungs clear',
  impression: 'no acute',
  recommendations: '',
  aiHighlightsJson: JSON.stringify({ impression: true }),
};

describe('mobile edit page', () => {
  beforeEach(() => {
    get.mockReset();
    patch.mockReset();
    get.mockResolvedValue(sample);
    patch.mockResolvedValue(sample);
  });

  it('renders all six section panels', async () => {
    const r = render(<Page />);
    await waitFor(() => r.getByTestId('section-indication'));
    for (const k of ['indication', 'technique', 'comparison', 'findings', 'impression', 'recommendations']) {
      expect(r.getByTestId(`section-${k}`).classList.contains('rp-mobile-section')).toBe(true);
    }
  });

  it('AI-flagged section wears the `.ai-mark` wrapper', async () => {
    const r = render(<Page />);
    await waitFor(() => r.getByTestId('section-impression'));
    const impressionSection = r.getByTestId('section-impression');
    expect(impressionSection.querySelector('.ai-mark')).not.toBeNull();
    // Non-AI section must not have it.
    expect(r.getByTestId('section-findings').querySelector('.ai-mark')).toBeNull();
  });

  it('tap-to-edit reveals a textarea bound to the section', async () => {
    const r = render(<Page />);
    await waitFor(() => r.getByTestId('textarea-indication'));
    const ta = r.getByTestId('textarea-indication') as HTMLTextAreaElement;
    fireEvent.change(ta, { target: { value: 'updated' } });
    expect(ta.value).toBe('updated');
  });

  it('save calls api.reports.patch with the draft', async () => {
    const r = render(<Page />);
    await waitFor(() => r.getByTestId('textarea-indication'));
    fireEvent.change(r.getByTestId('textarea-indication'), { target: { value: 'updated cough' } });
    await act(async () => {
      fireEvent.click(r.getByTestId('save-btn'));
    });
    await waitFor(() => expect(patch).toHaveBeenCalled());
    const [id, body] = patch.mock.calls[0];
    expect(id).toBe('rpt-1');
    expect((body as { indication: string }).indication).toBe('updated cough');
  });
});
