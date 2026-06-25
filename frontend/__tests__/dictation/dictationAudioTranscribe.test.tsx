import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, fireEvent, screen, waitFor, act } from '@testing-library/react';

const transcribeMock = vi.fn(async () => ({
  transcript: 'lungs are clear full stop',
  provider: 'UBAG',
  model: 'gemini_web',
  latencyMs: 10,
}));
vi.mock('@/lib/api', () => ({
  api: { reports: { transcribe: (...args: unknown[]) => transcribeMock(...args) } },
}));
vi.mock('@/lib/browserParams', () => ({ readQueryParam: () => 'rpt-1' }));

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

describe('DictationOverlay — HQ audio transcription', () => {
  it('records, transcribes, and inserts the formatted transcript at the cursor', async () => {
    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    render(<DictationOverlay />);
    focusEditable(ta);

    const hq = screen.getByTestId('dictation-hq');
    await act(async () => {
      fireEvent.click(hq); // start recording
    });
    await waitFor(() => expect(hq).toHaveAttribute('aria-pressed', 'true'));

    await act(async () => {
      fireEvent.click(hq); // stop -> transcribe -> insert
    });

    await waitFor(() => expect(transcribeMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(ta.value).toBe('Lungs are clear.'));
    expect(transcribeMock.mock.calls[0][0]).toBe('rpt-1');
    expect(transcribeMock.mock.calls[0][1]).toBeInstanceOf(Blob);
  });
});
