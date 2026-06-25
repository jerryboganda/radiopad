import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, fireEvent, act, screen } from '@testing-library/react';
import * as React from 'react';
import DictationOverlay from '@/components/dictation/DictationOverlay';
import { _resetFocusTracker } from '@/lib/dictation/focusTracker';

// Minimal Web-Speech stand-in the tests can drive.
class FakeRecognition {
  lang = '';
  continuous = false;
  interimResults = false;
  onresult: ((e: unknown) => void) | null = null;
  onerror: ((e: unknown) => void) | null = null;
  onend: (() => void) | null = null;
  start = vi.fn();
  stop = vi.fn(() => this.onend?.());
  emit(transcript: string, isFinal: boolean) {
    this.onresult?.({ resultIndex: 0, results: [Object.assign([{ transcript }], { isFinal })] });
  }
}

const w = window as unknown as Record<string, unknown>;
let rec: FakeRecognition;

beforeEach(() => {
  _resetFocusTracker();
  document.body.innerHTML = '';
  rec = new FakeRecognition();
  w.SpeechRecognition = function () {
    return rec;
  };
});
afterEach(() => {
  delete w.SpeechRecognition;
  delete w.webkitSpeechRecognition;
});

function focusEditable(el: HTMLElement) {
  el.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
}

describe('DictationOverlay', () => {
  it('shows a disabled mic when Web Speech is unavailable (e.g. desktop WebView2)', () => {
    delete w.SpeechRecognition;
    render(<DictationOverlay />);
    expect(screen.getByTestId('dictation-fab')).toBeDisabled();
  });

  it('toggles listening on click', () => {
    render(<DictationOverlay />);
    const fab = screen.getByTestId('dictation-fab');
    expect(fab).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(fab);
    expect(rec.start).toHaveBeenCalledTimes(1);
    expect(fab).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(fab);
    expect(rec.stop).toHaveBeenCalledTimes(1);
    expect(fab).toHaveAttribute('aria-pressed', 'false');
  });

  it('also toggles when the desktop radiopad:dictate event fires', () => {
    render(<DictationOverlay />);
    const fab = screen.getByTestId('dictation-fab');
    act(() => {
      window.dispatchEvent(new CustomEvent('radiopad:dictate'));
    });
    expect(fab).toHaveAttribute('aria-pressed', 'true');
    expect(rec.start).toHaveBeenCalledTimes(1);
  });

  it('inserts a formatted final transcript into the last focused textarea', () => {
    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    render(<DictationOverlay />);
    focusEditable(ta);
    fireEvent.click(screen.getByTestId('dictation-fab'));
    act(() => rec.emit('lungs are clear full stop', true));
    expect(ta.value).toBe('Lungs are clear.');
  });

  it('updates a React-controlled field via the native setter + input event', () => {
    function Host() {
      const [v, setV] = React.useState('');
      return (
        <>
          <textarea data-testid="field" value={v} onChange={(e) => setV(e.target.value)} />
          <DictationOverlay />
        </>
      );
    }
    render(<Host />);
    const field = screen.getByTestId('field') as HTMLTextAreaElement;
    focusEditable(field);
    fireEvent.click(screen.getByTestId('dictation-fab'));
    act(() => rec.emit('no acute findings full stop', true));
    expect(field.value).toBe('No acute findings.');
  });

  it('asks the host page to clean the dictation when Fix is pressed', () => {
    render(<DictationOverlay />);
    const onCleanup = vi.fn();
    window.addEventListener('radiopad:dictation-cleanup', onCleanup);
    fireEvent.click(screen.getByTestId('dictation-fix'));
    expect(onCleanup).toHaveBeenCalledTimes(1);
    window.removeEventListener('radiopad:dictation-cleanup', onCleanup);
  });
});
