// The on-device formatting route in the draft panel.
//
// This exists because the route did not: `api.reports.dictationDraftLocal` had ZERO callers, so the
// entire offline MedGemma path — model provisioning, llama-server, the whole §4.2 pipeline running
// on the workstation — was unreachable from the UI no matter how well it worked underneath. These
// tests pin the wiring, and pin the two behaviours that make it safe to offer at all: cloud stays
// the default (decision D1), and a fallback to the cloud is never silent.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, fireEvent, waitFor, screen } from '@testing-library/react';
import DictationDraftPanel from '@/app/(desktop)/reports/[id]/DictationDraftPanel';

const dictationDraft = vi.fn();
const dictationDraftLocal = vi.fn();
const lexiconList = vi.fn();
const userCorrectionsList = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      dictationDraft: (...args: unknown[]) => dictationDraft(...args),
      dictationDraftLocal: (...args: unknown[]) => dictationDraftLocal(...args),
    },
    // The on-device path resolves the correction dictionary client-side before calling, because
    // that endpoint is stateless and cannot look it up server-side.
    lexicon: { list: () => lexiconList() },
    userCorrections: { list: () => userCorrectionsList() },
  },
}));

const result = (provider: string) => ({
  sections: { findings: 'Mass in the right kidney.' },
  accepted: true,
  usedFallback: false,
  requiresReview: true,
  violations: [],
  sentinelWarnings: [],
  provider,
  model: 'm',
  latencyMs: 10,
});

function setDesktop(on: boolean) {
  if (on) (window as unknown as { __TAURI__?: unknown }).__TAURI__ = {};
  else delete (window as unknown as { __TAURI__?: unknown }).__TAURI__;
}

async function renderPanel() {
  const view = render(<DictationDraftPanel reportId="r1" initialText="a nodule" onApply={vi.fn()} />);
  // The desktop check runs in an effect, so let it settle before asserting on the toggle.
  await waitFor(() => view);
  return view;
}

beforeEach(() => {
  dictationDraft.mockResolvedValue(result('cloud'));
  dictationDraftLocal.mockResolvedValue(result('local-medgemma'));
  lexiconList.mockResolvedValue([{ term: 'c-spine', replacement: 'cervical spine' }]);
  userCorrectionsList.mockResolvedValue([{ id: '1', from: 'mri', to: 'MRI' }]);
});

afterEach(() => {
  setDesktop(false);
  vi.clearAllMocks();
});

describe('on-device formatting route', () => {
  it('is not offered on the web surface', async () => {
    setDesktop(false);
    await renderPanel();
    expect(screen.queryByTestId('dictation-on-device-toggle')).toBeNull();
  });

  it('is offered on desktop but OFF by default — cloud stays the default (D1)', async () => {
    setDesktop(true);
    await renderPanel();

    const toggle = await screen.findByTestId('dictation-on-device-toggle');
    expect((toggle as HTMLInputElement).checked).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: /format/i }));
    await waitFor(() => expect(dictationDraft).toHaveBeenCalled());
    expect(dictationDraftLocal).not.toHaveBeenCalled();
  });

  it('routes to the on-device formatter when enabled', async () => {
    setDesktop(true);
    await renderPanel();

    fireEvent.click(await screen.findByTestId('dictation-on-device-toggle'));
    fireEvent.click(screen.getByRole('button', { name: /format/i }));

    await waitFor(() => expect(dictationDraftLocal).toHaveBeenCalled());
    expect(dictationDraft).not.toHaveBeenCalled();
  });

  it('applies the correction dictionary on the on-device path', async () => {
    // The cloud endpoint resolves org lexicon + personal corrections server-side; the on-device
    // endpoint is stateless and cannot. Without passing them, switching to on-device would silently
    // drop every correction the radiologist configured — the same dictation producing different
    // text depending only on WHERE it was formatted.
    setDesktop(true);
    await renderPanel();

    fireEvent.click(await screen.findByTestId('dictation-on-device-toggle'));
    fireEvent.click(screen.getByRole('button', { name: /format/i }));

    await waitFor(() => expect(dictationDraftLocal).toHaveBeenCalled());
    const ctx = dictationDraftLocal.mock.calls.at(-1)?.[1] as { corrections?: unknown[] };
    expect(ctx?.corrections).toEqual(
      expect.arrayContaining([
        { from: 'c-spine', to: 'cervical spine' },
        { from: 'mri', to: 'MRI' },
      ]),
    );
  });

  it('still formats when the correction lookup fails', async () => {
    // A dictionary that cannot be fetched must degrade to "no corrections", never block dictation.
    setDesktop(true);
    lexiconList.mockRejectedValue(new Error('offline'));
    userCorrectionsList.mockRejectedValue(new Error('offline'));
    await renderPanel();

    fireEvent.click(await screen.findByTestId('dictation-on-device-toggle'));
    fireEvent.click(screen.getByRole('button', { name: /format/i }));

    await waitFor(() => expect(dictationDraftLocal).toHaveBeenCalled());
    expect(dictationDraft).not.toHaveBeenCalled();
  });

  it('falls back to the cloud when on-device is unavailable — and SAYS SO', async () => {
    // Silently sending PHI to the cloud after the radiologist explicitly asked for on-device
    // processing would be a privacy surprise, not a convenience. The fallback must be visible.
    setDesktop(true);
    dictationDraftLocal.mockRejectedValue(new Error('HTTP 503'));
    await renderPanel();

    fireEvent.click(await screen.findByTestId('dictation-on-device-toggle'));
    fireEvent.click(screen.getByRole('button', { name: /format/i }));

    await waitFor(() => expect(dictationDraft).toHaveBeenCalled());
    expect(await screen.findByText(/formatted in the cloud/i)).toBeTruthy();
  });

  it('still surfaces a real error when BOTH paths fail', async () => {
    setDesktop(true);
    dictationDraftLocal.mockRejectedValue(new Error('HTTP 503'));
    dictationDraft.mockRejectedValue({ message: 'cloud down' });
    await renderPanel();

    fireEvent.click(await screen.findByTestId('dictation-on-device-toggle'));
    fireEvent.click(screen.getByRole('button', { name: /format/i }));

    expect(await screen.findByText(/cloud down/i)).toBeTruthy();
  });
});
