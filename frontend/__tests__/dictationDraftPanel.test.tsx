// Dictation-engine brief §4.2 — the draft panel must surface the SAFETY outcome:
// a fail-safe fallback + violations (blocker) when the model output is rejected (§5.3),
// and laterality/negation/sex review warnings (§5.6). It must never silently show
// rejected AI text as if it were clean.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';
import DictationDraftPanel from '@/app/(desktop)/reports/[id]/DictationDraftPanel';

const dictationDraft = vi.fn();
vi.mock('@/lib/api', () => ({
  api: { reports: { dictationDraft: (...args: unknown[]) => dictationDraft(...args) } },
}));

function draftResult(overrides: Record<string, unknown> = {}) {
  return {
    sections: { findings: 'Mass in the right kidney.' },
    accepted: true,
    usedFallback: false,
    requiresReview: true,
    violations: [],
    sentinelWarnings: [],
    provider: 'cloud',
    model: 'gpt',
    latencyMs: 12,
    ...overrides,
  };
}

function setup(result: Record<string, unknown>) {
  dictationDraft.mockResolvedValue(result);
  const onApply = vi.fn();
  const utils = render(
    <DictationDraftPanel reportId="r1" initialText="mass in the right kidney" onApply={onApply} />,
  );
  return { ...utils, onApply };
}

beforeEach(() => {
  dictationDraft.mockReset();
});

describe('DictationDraftPanel', () => {
  it('shows the fail-safe fallback + violations when the model output is rejected (§5.3)', async () => {
    const { getByRole, findByText } = setup(
      draftResult({
        accepted: false,
        usedFallback: true,
        sections: { findings: 'mass in the right kidney' },
        violations: [{ reason: 'AddedMeasurement', detail: "measurement '2.5 cm' not present in the dictation" }],
      }),
    );

    fireEvent.click(getByRole('button', { name: /format \(safety-checked\)/i }));

    await findByText(/showing your dictation/i);
    await findByText(/2\.5 cm/);
  });

  it('shows a pass banner and surfaces sentinel warnings for review (§5.6)', async () => {
    const { getByRole, findByText } = setup(
      draftResult({
        sentinelWarnings: [{ kind: 'Laterality', detail: "report says 'left' but the dictation said 'right'" }],
      }),
    );

    fireEvent.click(getByRole('button', { name: /format \(safety-checked\)/i }));

    await findByText(/passed the safety validator/i);
    await findByText(/possible laterality/i);       // sentinel banner title
    await findByText(/dictation said 'right'/i);
  });

  it('applies the drafted sections when Apply is clicked', async () => {
    const { getByRole, findByRole, onApply } = setup(draftResult());

    fireEvent.click(getByRole('button', { name: /format \(safety-checked\)/i }));
    const applyBtn = await findByRole('button', { name: /apply to report/i });
    fireEvent.click(applyBtn);

    await waitFor(() => expect(onApply).toHaveBeenCalledWith({ findings: 'Mass in the right kidney.' }));
  });
});
