import { describe, expect, it } from 'vitest';
import { SpeechSegmenter, frameRms, joinPreview } from '@/lib/dictation/chunkedCapture';

const SR = 16000;

/** Synthesize `ms` of speech-like signal (a tone loud enough to clear the VAD threshold). */
function speech(ms: number, amplitude = 0.3): Float32Array {
  const n = Math.round((SR * ms) / 1000);
  const out = new Float32Array(n);
  for (let i = 0; i < n; i++) out[i] = amplitude * Math.sin((2 * Math.PI * 220 * i) / SR);
  return out;
}

/** Synthesize `ms` of near-silence (a little dither, as a real mic floor has). */
function silence(ms: number): Float32Array {
  const n = Math.round((SR * ms) / 1000);
  const out = new Float32Array(n);
  for (let i = 0; i < n; i++) out[i] = 0.0005 * Math.sin(i);
  return out;
}

function concat(...parts: Float32Array[]): Float32Array {
  const total = parts.reduce((s, p) => s + p.length, 0);
  const out = new Float32Array(total);
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}

const durationMs = (s: { samples: Float32Array }) => (s.samples.length / SR) * 1000;

describe('frameRms', () => {
  it('separates speech from silence', () => {
    const s = speech(100);
    const q = silence(100);
    expect(frameRms(s, 0, s.length)).toBeGreaterThan(0.05);
    expect(frameRms(q, 0, q.length)).toBeLessThan(0.01);
  });
});

describe('SpeechSegmenter', () => {
  it('closes a segment after a sustained pause', () => {
    const seg = new SpeechSegmenter();
    const done = seg.push(concat(speech(1200), silence(900)));

    expect(done).toHaveLength(1);
    expect(done[0].reason).toBe('silence');
    // The trailing silence is trimmed (a short tail is kept so a final consonant survives).
    expect(durationMs(done[0])).toBeLessThan(1500);
    expect(durationMs(done[0])).toBeGreaterThan(1000);
  });

  it('does NOT split on the short pause inside a spoken measurement', () => {
    // "three point" (250 ms gap) "two centimetres" — cutting here is the dangerous case: the two
    // fragments could decode to a number that was never dictated. The hold must ride through it.
    const seg = new SpeechSegmenter();
    const done = seg.push(concat(speech(600), silence(250), speech(700)));

    expect(done).toHaveLength(0); // still open — nothing emitted mid-measurement
    const flushed = seg.flush();
    expect(flushed).toHaveLength(1);
    expect(durationMs(flushed[0])).toBeGreaterThan(1400); // both halves in ONE segment
  });

  it('emits on the max-duration cap when the speaker never pauses', () => {
    const seg = new SpeechSegmenter({ maxSegmentMs: 2000 });
    const done = seg.push(speech(5000));

    expect(done.length).toBeGreaterThanOrEqual(2);
    expect(done[0].reason).toBe('max-duration');
  });

  it('is independent of how the caller slices its callbacks', () => {
    const audio = concat(speech(1000), silence(900), speech(800), silence(900));

    const whole = new SpeechSegmenter();
    const a = [...whole.push(audio), ...whole.flush()];

    const sliced = new SpeechSegmenter();
    const b: ReturnType<SpeechSegmenter['push']> = [];
    for (let i = 0; i < audio.length; i += 517) b.push(...sliced.push(audio.subarray(i, i + 517)));
    b.push(...sliced.flush());

    // Same speech in, same number of segments out — results must not depend on buffer sizes.
    expect(b.length).toBe(a.length);
    expect(a.length).toBe(2);
  });

  it('drops blips too short to be speech', () => {
    const seg = new SpeechSegmenter({ minSegmentMs: 400 });
    const done = [...seg.push(concat(silence(200), speech(120), silence(900))), ...seg.flush()];
    expect(done).toHaveLength(0);
  });

  it('ignores leading silence so a segment never starts with dead air', () => {
    const seg = new SpeechSegmenter();
    const done = [...seg.push(concat(silence(1500), speech(900), silence(900))), ...seg.flush()];

    expect(done).toHaveLength(1);
    expect(durationMs(done[0])).toBeLessThan(1300); // the 1.5 s of dead air is not in the audio
    expect(done[0].startSample).toBeGreaterThan(0); // ...and it was accounted for in the offset
  });

  it('flush emits speech that never got a closing pause', () => {
    // Push-to-talk released mid-sentence: the audio must not be silently discarded.
    const seg = new SpeechSegmenter();
    expect(seg.push(speech(800))).toHaveLength(0);

    const flushed = seg.flush();
    expect(flushed).toHaveLength(1);
    expect(flushed[0].reason).toBe('flush');
  });

  it('reports offsets that advance across segments', () => {
    const seg = new SpeechSegmenter();
    const done = [
      ...seg.push(concat(speech(900), silence(900), speech(900), silence(900))),
      ...seg.flush(),
    ];
    expect(done).toHaveLength(2);
    expect(done[1].startSample).toBeGreaterThan(done[0].startSample);
  });

  it('emits nothing for pure silence', () => {
    const seg = new SpeechSegmenter();
    expect([...seg.push(silence(3000)), ...seg.flush()]).toHaveLength(0);
  });

  it('stays linear as the utterance grows', () => {
    // This runs on every captured frame during live dictation on modest hardware. A first version
    // rebuilt a Float32Array of the whole buffer to measure each 30 ms frame — O(n²), which is
    // invisible on a 1 s clip and crippling on a long one. Doubling the audio must roughly double
    // the work, not quadruple it.
    const run = (ms: number) => {
      const audio = speech(ms, 0.3); // unbroken speech: nothing is emitted, the buffer keeps growing
      const seg = new SpeechSegmenter({ maxSegmentMs: 10 ** 7 });
      const t0 = performance.now();
      for (let i = 0; i < audio.length; i += 480) seg.push(audio.subarray(i, i + 480));
      return performance.now() - t0;
    };

    run(500); // warm up the JIT so the first measurement is not penalised
    const short = Math.max(run(2000), 1);
    const long = Math.max(run(8000), 1);

    // 4x the audio. Linear would be ~4x; quadratic ~16x. Allow generous headroom for a noisy CI
    // box while still failing loudly on a return to quadratic behaviour.
    expect(long / short).toBeLessThan(10);
  });
});

describe('joinPreview', () => {
  it('joins segment previews into one display string', () => {
    expect(joinPreview(['CT chest with contrast.', '  There is a nodule. '])).toBe(
      'CT chest with contrast. There is a nodule.',
    );
  });

  it('ignores empty and whitespace-only previews', () => {
    expect(joinPreview(['', '   ', 'No pneumothorax.'])).toBe('No pneumothorax.');
    expect(joinPreview([])).toBe('');
  });
});
