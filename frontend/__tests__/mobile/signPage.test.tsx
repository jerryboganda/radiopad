// Iter-36 MOB — `/mobile/reports/sign?reportId=...` page test. Two
// acknowledgement checkboxes block the action button until both are
// checked (the warning checkbox is mandatory only when warnings
// exist). Validation findings render with the locked severity classes
// (`.finding.blocker|warning|info`).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';

const get = vi.fn();
const validate = vi.fn();
const acknowledge = vi.fn();
const exportText = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      get: (...args: unknown[]) => get(...args),
      validate: (...args: unknown[]) => validate(...args),
      acknowledge: (...args: unknown[]) => acknowledge(...args),
      exportText: (...args: unknown[]) => exportText(...args),
      exportJson: vi.fn(),
      exportFhir: vi.fn(),
      exportPdf: vi.fn(),
    },
  },
}));

import Page from '@/app/mobile/reports/sign/page';

const sampleReport = {
  id: 'rpt-1',
  indication: 'cough',
  technique: 'CT',
  comparison: '',
  findings: 'lungs clear',
  impression: 'no acute',
  recommendations: '',
  aiHighlightsJson: JSON.stringify({ impression: true }),
};

beforeEach(() => {
  get.mockReset();
  validate.mockReset();
  acknowledge.mockReset();
  exportText.mockReset();
  window.history.replaceState(null, '', '/mobile/reports/sign?reportId=rpt-1');
  get.mockResolvedValue(sampleReport);
  acknowledge.mockResolvedValue(sampleReport);
  exportText.mockResolvedValue('REPORT TEXT');
  // Stub URL APIs used by the download helper.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (URL as any).createObjectURL = vi.fn(() => 'blob:rpt');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (URL as any).revokeObjectURL = vi.fn();
});

describe('mobile sign page', () => {
  it('Acknowledge & Export disabled until the AI checkbox is ticked (no warnings case)', async () => {
    validate.mockResolvedValue({ blockerPresent: false, findings: [] });
    const r = render(<Page />);
    const btn = await waitFor(() => r.getByTestId('ack-export') as HTMLButtonElement);
    expect(btn.disabled).toBe(true);
    fireEvent.click(r.getByTestId('ack-ai'));
    await waitFor(() => expect((r.getByTestId('ack-export') as HTMLButtonElement).disabled).toBe(false));
  });

  it('renders findings with the locked severity classes', async () => {
    validate.mockResolvedValue({
      blockerPresent: false,
      findings: [
        { ruleId: 'r.1', severity: 'Warning', message: 'amber' },
        { ruleId: 'r.2', severity: 'Info', message: 'blue' },
      ],
    });
    const r = render(<Page />);
    await waitFor(() => r.getAllByTestId('finding'));
    const nodes = r.getAllByTestId('finding');
    expect(nodes[0].classList.contains('finding')).toBe(true);
    expect(nodes[0].classList.contains('warning')).toBe(true);
    expect(nodes[1].classList.contains('info')).toBe(true);
  });

  it('blocks until BOTH checkboxes are ticked when warnings exist', async () => {
    validate.mockResolvedValue({
      blockerPresent: false,
      findings: [{ ruleId: 'r.1', severity: 'Warning', message: 'amber' }],
    });
    const r = render(<Page />);
    const btn = await waitFor(() => r.getByTestId('ack-export') as HTMLButtonElement);
    expect(btn.disabled).toBe(true);
    fireEvent.click(r.getByTestId('ack-ai'));
    expect((r.getByTestId('ack-export') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(r.getByTestId('ack-warn'));
    await waitFor(() => expect((r.getByTestId('ack-export') as HTMLButtonElement).disabled).toBe(false));
  });

  it('disables the action when a Blocker is present even if both checkboxes are ticked', async () => {
    validate.mockResolvedValue({
      blockerPresent: true,
      findings: [{ ruleId: 'r.b', severity: 'Blocker', message: 'red' }],
    });
    const r = render(<Page />);
    await waitFor(() => r.getByTestId('ack-ai'));
    fireEvent.click(r.getByTestId('ack-ai'));
    expect((r.getByTestId('ack-export') as HTMLButtonElement).disabled).toBe(true);
  });
});
