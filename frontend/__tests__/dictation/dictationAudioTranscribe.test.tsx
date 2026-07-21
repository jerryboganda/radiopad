import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, fireEvent, screen, waitFor, act } from '@testing-library/react';

const transcribeMock = vi.fn(async (_id: string, _audio: Blob, _ack?: boolean) => ({
  transcript: 'lungs are clear full stop',
  provider: 'UBAG',
  model: 'gemini_web',
  latencyMs: 10,
}));
// Mirrors api.ts: the on-device sidecar resolves to a non-empty base URL only
// inside the desktop shell; empty string means "no local sidecar" (web, or
// desktop before the sidecar is up).
const localSttBaseMock = vi.fn(async () => '');
vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      transcribe: (id: string, audio: Blob, ack?: boolean) => transcribeMock(id, audio, ack),
    },
  },
  localSttBase: () => localSttBaseMock(),
}));

const readQueryParamMock = vi.fn(() => 'rpt-1');
vi.mock('@/lib/browserParams', () => ({ readQueryParam: (name: string) => readQueryParamMock(name) }));

import DictationOverlay from '@/components/dictation/DictationOverlay';
import { _resetFocusTracker } from '@/lib/dictation/focusTracker';

// Minimal MediaRecorder stand-in: stop() flushes one chunk then fires onstop.
class FakeMediaRecorder {
  state = 'inactive';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  constructor(public stream: unknown, public opts?: unknown) {}
  start() {
    this.state = 'recording';
  }
  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['xxxxx'], { type: 'audio/webm' }) });
    this.onstop?.();
  }
}

const w = window as unknown as Record<string, unknown>;

beforeEach(() => {
  _resetFocusTracker();
  document.body.innerHTML = '';
  transcribeMock.mockClear();
  readQueryParamMock.mockClear();
  readQueryParamMock.mockReturnValue('rpt-1');
  localSttBaseMock.mockClear();
  localSttBaseMock.mockResolvedValue('');
  w.MediaRecorder = FakeMediaRecorder;
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: {
      getUserMedia: vi.fn(async () => ({ getTracks: () => [{ stop: vi.fn() }] })),
    },
  });
  delete w.SpeechRecognition; // Web Speech absent; the HQ path is independent.
});
afterEach(() => {
  delete w.MediaRecorder;
});

function focusEditable(el: HTMLElement) {
  el.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
}

async function recordAndStop(hq: HTMLElement) {
  await act(async () => {
    fireEvent.click(hq); // start recording
  });
  await waitFor(() => expect(hq).toHaveAttribute('aria-pressed', 'true'));
  await act(async () => {
    fireEvent.click(hq); // stop -> transcribe -> insert
  });
}

describe('DictationOverlay — HQ audio transcription', () => {
  it('records, transcribes, and inserts the formatted transcript at the cursor', async () => {
    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    render(<DictationOverlay />);
    focusEditable(ta);

    const hq = screen.getByTestId('dictation-hq');
    await recordAndStop(hq);

    await waitFor(() => expect(transcribeMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(ta.value).toBe('Lungs are clear.'));
    expect(transcribeMock.mock.calls[0][0]).toBe('rpt-1');
    expect(transcribeMock.mock.calls[0][1]).toBeInstanceOf(Blob);
  });

  it('transcribes via the on-device sidecar with no report open yet (New Report wizard)', async () => {
    // Steps 2/3 of the New Report wizard (Positive findings / Clinical
    // history) dictate before any report exists, so the URL has no `?id=`.
    readQueryParamMock.mockReturnValue('');
    localSttBaseMock.mockResolvedValue('http://127.0.0.1:5555');

    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    render(<DictationOverlay />);
    focusEditable(ta);

    const hq = screen.getByTestId('dictation-hq');
    await recordAndStop(hq);

    await waitFor(() => expect(transcribeMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(ta.value).toBe('Lungs are clear.'));
    expect(screen.queryByTestId('dictation-error')).not.toBeInTheDocument();
  });

  it('still blocks HQ dictation with no report open and no on-device sidecar (web)', async () => {
    readQueryParamMock.mockReturnValue('');
    localSttBaseMock.mockResolvedValue('');

    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    render(<DictationOverlay />);
    focusEditable(ta);

    const hq = screen.getByTestId('dictation-hq');
    await recordAndStop(hq);

    await waitFor(() =>
      expect(screen.getByTestId('dictation-error')).toHaveTextContent('HQ dictation needs an open report'),
    );
    expect(transcribeMock).not.toHaveBeenCalled();
  });
});
