import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createTypeDictationStreamer } from '@/lib/companionTypeDictation';

describe('companion type-dictation streamer', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  function harness(opts?: { format?: (t: string) => string }) {
    const sent: Array<{ text: string; isFinal: boolean }> = [];
    const committed: Array<{ formatted: string; raw: string }> = [];
    const streamer = createTypeDictationStreamer({
      send: (text, isFinal) => sent.push({ text, isFinal }),
      format: opts?.format,
      onCommitted: (formatted, raw) => committed.push({ formatted, raw }),
      idleCommitMs: 1600,
      interimThrottleMs: 150,
    });
    return { sent, committed, streamer };
  }

  it('streams a throttled interim while typing', () => {
    const { sent, streamer } = harness();
    streamer.onTextChange('right lower');
    streamer.onTextChange('right lower lobe');
    expect(sent).toHaveLength(0); // trailing throttle — nothing yet
    vi.advanceTimersByTime(150);
    expect(sent).toEqual([{ text: 'right lower lobe', isFinal: false }]);
  });

  it('auto-commits formatted text after the idle pause and clears state', () => {
    const { sent, committed, streamer } = harness({ format: (t) => t.toUpperCase() });
    streamer.onTextChange('no acute findings');
    vi.advanceTimersByTime(1600);
    expect(sent.at(-1)).toEqual({ text: 'NO ACUTE FINDINGS', isFinal: true });
    expect(committed).toEqual([{ formatted: 'NO ACUTE FINDINGS', raw: 'no acute findings' }]);
    // Nothing further fires once committed.
    const count = sent.length;
    vi.advanceTimersByTime(5000);
    expect(sent).toHaveLength(count);
  });

  it('keeps typing alive across pauses shorter than the idle window', () => {
    const { sent, streamer } = harness();
    streamer.onTextChange('mild');
    vi.advanceTimersByTime(1000);
    streamer.onTextChange('mild cardiomegaly');
    vi.advanceTimersByTime(1000);
    expect(sent.every((s) => !s.isFinal)).toBe(true); // no commit yet
    vi.advanceTimersByTime(600);
    expect(sent.at(-1)).toEqual({ text: 'mild cardiomegaly', isFinal: true });
  });

  it('manual commit fires immediately and cancels the idle timer', () => {
    const { sent, streamer } = harness();
    streamer.onTextChange('impression unremarkable');
    const text = streamer.commit();
    expect(text).toBe('impression unremarkable');
    expect(sent.at(-1)).toEqual({ text: 'impression unremarkable', isFinal: true });
    const count = sent.length;
    vi.advanceTimersByTime(5000);
    expect(sent).toHaveLength(count); // idle timer was cancelled
  });

  it('clearing the field clears a shown desktop preview instead of committing', () => {
    const { sent, streamer } = harness();
    streamer.onTextChange('stray words');
    vi.advanceTimersByTime(150); // preview shown on the desktop
    streamer.onTextChange('');
    vi.advanceTimersByTime(1600); // idle fires with empty text
    const finals = sent.filter((s) => s.isFinal);
    expect(finals).toEqual([{ text: '', isFinal: true }]); // ghost cleared, nothing inserted
  });

  it('dispose clears a lingering preview and goes inert', () => {
    const { sent, streamer } = harness();
    streamer.onTextChange('half a sentence');
    vi.advanceTimersByTime(150);
    streamer.dispose();
    expect(sent.at(-1)).toEqual({ text: '', isFinal: true });
    const count = sent.length;
    streamer.onTextChange('after dispose');
    expect(streamer.commit()).toBe('');
    vi.advanceTimersByTime(5000);
    expect(sent).toHaveLength(count);
  });

  it('whitespace-only text never commits as a final insert', () => {
    const { sent, committed, streamer } = harness();
    streamer.onTextChange('   ');
    vi.advanceTimersByTime(1600);
    expect(sent.filter((s) => s.isFinal && s.text)).toHaveLength(0);
    expect(committed).toHaveLength(0);
  });

  it('defers the idle auto-commit while the IME is composing, then commits', () => {
    const sent: Array<{ text: string; isFinal: boolean }> = [];
    let composing = true;
    const streamer = createTypeDictationStreamer({
      send: (text, isFinal) => sent.push({ text, isFinal }),
      deferIdleCommit: () => composing,
      idleCommitMs: 1600,
      interimThrottleMs: 150,
    });
    streamer.onTextChange('mid composition words');
    vi.advanceTimersByTime(1600);
    expect(sent.filter((s) => s.isFinal)).toHaveLength(0); // deferred, field untouched
    vi.advanceTimersByTime(1600);
    expect(sent.filter((s) => s.isFinal)).toHaveLength(0); // still composing, still deferred
    composing = false;
    vi.advanceTimersByTime(1600);
    expect(sent.at(-1)).toEqual({ text: 'mid composition words', isFinal: true });
  });

  it('manual commit ignores the composition deferral', () => {
    const sent: Array<{ text: string; isFinal: boolean }> = [];
    const streamer = createTypeDictationStreamer({
      send: (text, isFinal) => sent.push({ text, isFinal }),
      deferIdleCommit: () => true,
    });
    streamer.onTextChange('insist now');
    expect(streamer.commit()).toBe('insist now');
    expect(sent.at(-1)).toEqual({ text: 'insist now', isFinal: true });
  });
});
