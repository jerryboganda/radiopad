// PRD §14.15 (CR-001..010) — the critical-results panel must load the report's existing
// results, badge criticality/status with the documented tones, surface the due-in
// countdown, and send explicit create / communicate / acknowledge calls. This guards the
// wiring between the panel and `api.criticalResults` — nothing may fire automatically.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';
import CriticalResultPanel, { formatDueIn } from '@/components/critical/CriticalResultPanel';
import type { CriticalResult } from '@/lib/api';

const list = vi.fn();
const create = vi.fn();
const communicate = vi.fn();
const acknowledge = vi.fn();
const close = vi.fn();

vi.mock('@/lib/api', async () => {
  // Keep the real badge/label maps — the point is that the panel uses THEM, not
  // hardcoded colours.
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
  return {
    ...actual,
    api: {
      criticalResults: {
        list: (params: unknown) => list(params),
        create: (body: unknown) => create(body),
        communicate: (id: string, body: unknown) => communicate(id, body),
        acknowledge: (id: string) => acknowledge(id),
        close: (id: string) => close(id),
      },
    },
  };
});

let canManage = true;
vi.mock('@/lib/permissions', () => ({
  usePermissions: () => ({
    loading: false,
    signedOut: false,
    permissions: canManage ? ['critical_results.read', 'critical_results.manage'] : ['critical_results.read'],
    role: 0,
    roleName: 'Radiologist',
    can: (key: string) =>
      canManage
        ? key === 'critical_results.read' || key === 'critical_results.manage'
        : key === 'critical_results.read',
  }),
}));

const REPORT_ID = 'report-1';

function makeResult(over: Partial<CriticalResult> = {}): CriticalResult {
  const now = Date.now();
  return {
    id: 'cr-1',
    reportId: REPORT_ID,
    criticality: 'Red',
    status: 'Open',
    findingSummary: 'Large right pneumothorax',
    communicatedTo: null,
    communicationMethod: null,
    communicatedAt: null,
    acknowledgedBy: null,
    acknowledgedAt: null,
    dueAt: new Date(now + 15 * 60000).toISOString(),
    escalatedAt: null,
    closedAt: null,
    notes: '',
    isOverdue: false,
    createdAt: new Date(now).toISOString(),
    updatedAt: new Date(now).toISOString(),
    ...over,
  };
}

beforeEach(() => {
  canManage = true;
  list.mockReset();
  create.mockReset();
  communicate.mockReset();
  acknowledge.mockReset();
  close.mockReset();
});

describe('formatDueIn', () => {
  const now = Date.parse('2026-07-20T12:00:00.000Z');

  it('counts down while the deadline is ahead', () => {
    expect(formatDueIn('2026-07-20T12:12:00.000Z', now)).toBe('due in 12 min');
  });

  it('reports how far past the deadline it is', () => {
    expect(formatDueIn('2026-07-20T11:55:00.000Z', now)).toBe('overdue by 5 min');
  });

  it('switches to hours once the window is long', () => {
    expect(formatDueIn('2026-07-21T12:00:00.000Z', now)).toBe('due in 24 h');
  });
});

describe('CriticalResultPanel', () => {
  it('loads only this report\'s critical results', async () => {
    list.mockResolvedValue([]);
    const { findByText } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findByText(/no critical results on this report/i);
    expect(list).toHaveBeenCalledWith({ reportId: REPORT_ID });
  });

  /** Every `.badge` in the tree as `[className, text]` — tone AND label together. */
  function badges(container: HTMLElement): [string, string][] {
    return Array.from(container.querySelectorAll('.badge')).map((b) => [
      b.className,
      (b.textContent ?? '').trim(),
    ]);
  }

  it('badges criticality and status with the documented tones and shows the countdown', async () => {
    list.mockResolvedValue([makeResult()]);
    const { findByText, container } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findByText('Large right pneumothorax');

    // Red is the blocker-grade class → red (`danger`) tone. Never hue-only: the
    // badge carries its label text too.
    expect(badges(container)).toContainEqual(['badge danger', 'Red — immediate']);
    expect(badges(container)).toContainEqual(['badge warn', 'Open']);
    expect(container.textContent).toMatch(/due in \d+ min/);
  });

  it('uses the amber tone for Orange and the blue tone for Yellow', async () => {
    list.mockResolvedValue([
      makeResult({ id: 'cr-o', criticality: 'Orange' }),
      makeResult({ id: 'cr-y', criticality: 'Yellow' }),
    ]);
    const { findAllByTestId, container } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findAllByTestId('critical-result-row');

    expect(badges(container)).toContainEqual(['badge warn', 'Orange — urgent']);
    expect(badges(container)).toContainEqual(['badge info', 'Yellow — actionable']);
  });

  it('logs a new critical result with the picked criticality and trimmed summary', async () => {
    list.mockResolvedValue([]);
    create.mockResolvedValue(makeResult({ id: 'cr-new', criticality: 'Orange' }));

    const { findByText, getByLabelText, getByRole } = render(
      <CriticalResultPanel reportId={REPORT_ID} />,
    );
    await findByText(/no critical results on this report/i);

    fireEvent.change(getByLabelText(/criticality/i), { target: { value: 'Orange' } });
    fireEvent.change(getByLabelText(/finding/i), {
      target: { value: '  New intracranial haemorrhage  ' },
    });
    fireEvent.click(getByRole('button', { name: /log critical result/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith({
        reportId: REPORT_ID,
        criticality: 'Orange',
        findingSummary: 'New intracranial haemorrhage',
      }),
    );
  });

  it('will not log an empty finding', async () => {
    list.mockResolvedValue([]);
    const { findByText, getByRole } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findByText(/no critical results on this report/i);

    // The button is disabled by the live check, so clicking is a no-op.
    fireEvent.click(getByRole('button', { name: /log critical result/i }));
    expect(create).not.toHaveBeenCalled();
  });

  it('records a communication with the recipient and method', async () => {
    list.mockResolvedValue([makeResult()]);
    communicate.mockResolvedValue(
      makeResult({
        status: 'Communicated',
        communicatedTo: 'Dr Osei (ED)',
        communicationMethod: 'Phone',
        communicatedAt: new Date().toISOString(),
      }),
    );

    const { findByText, getByLabelText, getByRole } = render(
      <CriticalResultPanel reportId={REPORT_ID} />,
    );
    fireEvent.click(await findByText(/record communication/i));

    fireEvent.change(getByLabelText(/communicated to/i), { target: { value: 'Dr Osei (ED)' } });
    fireEvent.change(getByLabelText(/^how$/i), { target: { value: 'SecureMessage' } });
    fireEvent.click(getByRole('button', { name: /save communication/i }));

    await waitFor(() =>
      expect(communicate).toHaveBeenCalledWith('cr-1', {
        communicatedTo: 'Dr Osei (ED)',
        method: 'SecureMessage',
      }),
    );
    await findByText(/communicated to dr osei \(ed\)/i);
  });

  it('blocks acknowledge until the communication has been recorded', async () => {
    list.mockResolvedValue([makeResult()]); // communicatedAt === null
    const { findByRole } = render(<CriticalResultPanel reportId={REPORT_ID} />);

    const ackButton = await findByRole('button', { name: /acknowledge/i });
    expect((ackButton as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(ackButton);
    expect(acknowledge).not.toHaveBeenCalled();
  });

  it('acknowledges a communicated result', async () => {
    list.mockResolvedValue([
      makeResult({ status: 'Communicated', communicatedTo: 'Dr Osei', communicatedAt: new Date().toISOString() }),
    ]);
    acknowledge.mockResolvedValue(
      makeResult({
        status: 'Acknowledged',
        communicatedTo: 'Dr Osei',
        communicatedAt: new Date().toISOString(),
        acknowledgedBy: 'Dr Osei',
        acknowledgedAt: new Date().toISOString(),
      }),
    );

    const { findByRole, findByText } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    fireEvent.click(await findByRole('button', { name: /acknowledge/i }));

    await waitFor(() => expect(acknowledge).toHaveBeenCalledWith('cr-1'));
    expect((await findByText('Acknowledged')).className).toContain('ok');
  });

  it('hides every mutating control from a read-only user', async () => {
    canManage = false;
    list.mockResolvedValue([makeResult()]);

    const { findByText, queryByRole } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findByText('Large right pneumothorax');

    expect(queryByRole('button', { name: /log critical result/i })).toBeNull();
    expect(queryByRole('button', { name: /record communication/i })).toBeNull();
    expect(queryByRole('button', { name: /acknowledge/i })).toBeNull();
  });

  it('surfaces a load failure with a retry', async () => {
    list.mockRejectedValueOnce(new Error('network is down'));
    const { findByText } = render(<CriticalResultPanel reportId={REPORT_ID} />);
    await findByText(/network is down/i);

    list.mockResolvedValue([makeResult()]);
    fireEvent.click(await findByText(/try again/i));
    await findByText('Large right pneumothorax');
  });
});
