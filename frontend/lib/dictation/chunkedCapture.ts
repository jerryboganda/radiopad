// P0.3 / decision D4 — incremental dictation decode.
//
// WHAT THIS IS, PRECISELY. Both on-device engines are sherpa-onnx *offline* recognizers (MedASR is
// Conformer-CTC, Parakeet is TDT); neither exposes sherpa's streaming `OnlineRecognizer`. So this
// is NOT frame-level streaming ASR. It is chunked incremental decode: while the radiologist keeps
// talking, audio is cut at natural pauses and each completed segment is decoded on its own, so text
// appears during dictation instead of only after it. Calling it "streaming" would overstate it.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────
// SAFETY CONTRACT — interim segment text is DISPLAY-ONLY.
//
// A chunk boundary can fall inside a spoken measurement: "three point" | "two centimetres" decodes
// as two fragments and can normalize to a number that was never said. That is exactly the class of
// error §5.2's token lock and §5.3's validation-diff exist to prevent, so the authoritative
// transcript — the one that feeds the pass-through, the formatter, and the audit — MUST be the
// decode of the COMPLETE buffer taken when the user releases push-to-talk. Segment text is a
// live preview and nothing else; it never reaches a report.
// ─────────────────────────────────────────────────────────────────────────────────────────────
//
// The segmenter is a pure function of the sample stream (no audio APIs, no timers) so its behaviour
// around measurements and pauses is unit-testable rather than something we hope holds live.

/** A completed speech segment, ready to be decoded for a live preview. */
export interface SpeechSegment {
  /** Mono 16 kHz samples for this segment, silence padding trimmed to what was detected. */
  samples: Float32Array;
  /** Offset of the first sample within the session buffer — lets callers order/replace previews. */
  startSample: number;
  /** Why the segment was cut. `max-duration` means no pause arrived in time. */
  reason: 'silence' | 'max-duration' | 'flush';
}

export interface SegmenterOptions {
  sampleRate?: number;
  /** RMS below which a frame counts as silence. Normalized float samples (-1..1). */
  silenceThreshold?: number;
  /** Sustained silence needed to close a segment. Long enough to sit through the natural pause
   *  inside a dictated phrase ("three point two ... centimetres") rather than cutting through it. */
  silenceHoldMs?: number;
  /** Hard cap so a radiologist who never pauses still gets feedback. */
  maxSegmentMs?: number;
  /** Segments shorter than this are dropped as noise (a cough, a chair creak). */
  minSegmentMs?: number;
  /** Analysis frame size. 30 ms is the usual VAD granularity. */
  frameMs?: number;
}

const DEFAULTS = {
  sampleRate: 16000,
  silenceThreshold: 0.012,
  // 700 ms: comfortably longer than the ~200-400 ms gap inside "three point two", so a measurement
  // is not split, while still feeling responsive at a real sentence break.
  silenceHoldMs: 700,
  maxSegmentMs: 12000,
  minSegmentMs: 320,
  frameMs: 30,
} as const;

/** Root-mean-square level of a frame. */
export function frameRms(samples: Float32Array, from: number, to: number): number {
  let sum = 0;
  for (let i = from; i < to; i++) sum += samples[i] * samples[i];
  const n = Math.max(1, to - from);
  return Math.sqrt(sum / n);
}

/**
 * Incremental segmenter. Feed it audio as it arrives; it returns the segments that have *completed*
 * so far. Retains any trailing partial speech until a pause arrives or {@link flush} is called.
 */
export class SpeechSegmenter {
  private readonly opts: Required<SegmenterOptions>;

  // A growable Float32Array with an explicit length, NOT a number[].
  //
  // This runs on every captured frame during live dictation on modest hardware, so it must be
  // O(samples) overall. An earlier version kept a number[] and rebuilt a Float32Array to measure
  // each 30 ms frame, which is O(n²) in the length of the utterance — fine in a unit test,
  // progressively worse the longer a radiologist speaks.
  private buf: Float32Array = new Float32Array(16000);
  private len = 0;
  /** Absolute index (in session samples) of buf[0]. */
  private pendingStart = 0;
  private consumed = 0;
  private silentRun = 0;
  private sawSpeech = false;

  constructor(options: SegmenterOptions = {}) {
    this.opts = { ...DEFAULTS, ...options };
  }

  /** Ensure capacity for `extra` more samples, growing geometrically. */
  private reserve(extra: number): void {
    if (this.len + extra <= this.buf.length) return;
    let cap = this.buf.length || 1;
    while (cap < this.len + extra) cap *= 2;
    const next = new Float32Array(cap);
    next.set(this.buf.subarray(0, this.len));
    this.buf = next;
  }

  /** Drop the first `count` samples, keeping the rest at the front. */
  private discard(count: number): void {
    if (count <= 0) return;
    const keep = this.len - count;
    if (keep > 0) this.buf.copyWithin(0, count, this.len);
    this.len = Math.max(0, keep);
    this.pendingStart += count;
  }

  private get frameSize(): number {
    return Math.max(1, Math.round((this.opts.sampleRate * this.opts.frameMs) / 1000));
  }

  private ms(samples: number): number {
    return (samples / this.opts.sampleRate) * 1000;
  }

  /**
   * Append newly captured samples and return any segments that closed.
   *
   * Frames are processed whole; a trailing partial frame stays buffered until enough audio arrives,
   * so results do not depend on how the caller happens to slice its callbacks.
   */
  push(chunk: Float32Array): SpeechSegment[] {
    this.reserve(chunk.length);
    this.buf.set(chunk, this.len);
    this.len += chunk.length;
    return this.drain(false);
  }

  /** Close out whatever remains — call on push-to-talk release. */
  flush(): SpeechSegment[] {
    return this.drain(true);
  }

  private drain(final: boolean): SpeechSegment[] {
    const out: SpeechSegment[] = [];
    const frame = this.frameSize;

    while (this.consumed + frame <= this.len) {
      // Measured in place — no per-frame copy of the whole buffer.
      const rms = frameRms(this.buf, this.consumed, this.consumed + frame);
      const speech = rms >= this.opts.silenceThreshold;
      this.consumed += frame;

      if (speech) {
        this.sawSpeech = true;
        this.silentRun = 0;
      } else if (this.sawSpeech) {
        this.silentRun += frame;
      } else {
        // Leading silence: drop it so a segment never starts with dead air.
        this.discard(frame);
        this.consumed -= frame;
        continue;
      }

      const closedBySilence = this.sawSpeech && this.ms(this.silentRun) >= this.opts.silenceHoldMs;
      const closedByLength = this.ms(this.consumed) >= this.opts.maxSegmentMs;

      if (closedBySilence || closedByLength) {
        const seg = this.cut(closedBySilence ? 'silence' : 'max-duration');
        if (seg) out.push(seg);
      }
    }

    if (final) {
      const seg = this.cut('flush');
      if (seg) out.push(seg);
      this.reset();
    }
    return out;
  }

  /** Emit the buffered speech as a segment, dropping it if it is too short to be real speech. */
  private cut(reason: SpeechSegment['reason']): SpeechSegment | null {
    const take = reason === 'flush' ? this.len : this.consumed;
    if (take <= 0) return null;

    // Exclude the detected trailing silence from the emitted audio, but keep a little so a final
    // consonant is not clipped.
    const keepTail = Math.min(this.silentRun, Math.round(this.opts.sampleRate * 0.1));
    const speechLen = Math.max(0, Math.min(take, take - this.silentRun + keepTail));

    // Copied out: the caller keeps this past the next push, which reuses the internal buffer.
    const samples = this.buf.slice(0, speechLen);
    const startSample = this.pendingStart;

    this.discard(take);
    this.consumed = Math.max(0, this.consumed - take);
    this.silentRun = 0;
    this.sawSpeech = false;

    if (!samples.length || this.ms(samples.length) < this.opts.minSegmentMs) return null;
    return { samples, startSample, reason };
  }

  private reset(): void {
    this.len = 0;
    this.consumed = 0;
    this.silentRun = 0;
    this.sawSpeech = false;
  }
}

/**
 * Join interim segment previews into the text shown while dictating.
 *
 * Preview only — see the safety contract at the top of this file. The value returned here must
 * never be sent to the formatter or persisted as the transcript.
 */
export function joinPreview(previews: readonly string[]): string {
  return previews
    .map((p) => p.trim())
    .filter(Boolean)
    .join(' ')
    .replace(/\s+/g, ' ')
    .trim();
}
