import { describe, expect, it } from 'vitest';
import { TARGET_SAMPLE_RATE, downsampleTo16k } from '@/lib/dictation/interimDecode';

describe('downsampleTo16k', () => {
  it('reduces 48 kHz to 16 kHz by the expected ratio', () => {
    const input = new Float32Array(4800); // 100 ms at 48 kHz
    const out = downsampleTo16k(input, 48000);
    expect(out.length).toBe(1600); // 100 ms at 16 kHz
  });

  it('handles the common 44.1 kHz mic rate', () => {
    const input = new Float32Array(44100); // 1 s
    const out = downsampleTo16k(input, 44100);
    // ~1 s at 16 kHz; the ratio is not integral so allow a sample of slack.
    expect(out.length).toBeGreaterThan(TARGET_SAMPLE_RATE - 2);
    expect(out.length).toBeLessThanOrEqual(TARGET_SAMPLE_RATE);
  });

  it('passes 16 kHz through untouched', () => {
    const input = new Float32Array([0.1, 0.2, 0.3]);
    expect(downsampleTo16k(input, TARGET_SAMPLE_RATE)).toBe(input);
  });

  it('never upsamples a lower-rate stream', () => {
    // Some capture devices report 8 kHz. Interpolating up invents detail; the engine copes with
    // the lower rate, so leave it alone.
    const input = new Float32Array(800);
    expect(downsampleTo16k(input, 8000)).toBe(input);
  });

  it('preserves the signal rather than emitting zeros', () => {
    // A constant input must survive resampling — a broken interpolation typically yields silence,
    // which would look exactly like "the mic is dead".
    const input = new Float32Array(4800).fill(0.5);
    const out = downsampleTo16k(input, 48000);
    expect(out.length).toBeGreaterThan(0);
    for (const v of out) expect(v).toBeCloseTo(0.5, 5);
  });

  it('keeps a tone recognisable (no gross aliasing to silence)', () => {
    const n = 48000;
    const input = new Float32Array(n);
    for (let i = 0; i < n; i++) input[i] = Math.sin((2 * Math.PI * 220 * i) / 48000);

    const out = downsampleTo16k(input, 48000);
    let sum = 0;
    for (const v of out) sum += v * v;
    const rms = Math.sqrt(sum / out.length);
    // A 220 Hz tone is far below the 8 kHz Nyquist limit, so its energy must survive.
    expect(rms).toBeGreaterThan(0.5);
  });

  it('returns an empty array for empty input', () => {
    expect(downsampleTo16k(new Float32Array(0), 48000).length).toBe(0);
  });
});
