// P0.3 — live preview of a dictation while it is still being spoken.
//
// Taps the SAME MediaStream the HQ recorder uses, cuts it at natural pauses via
// {@link SpeechSegmenter}, and decodes each completed segment so text appears during dictation
// rather than only on release.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────
// This is a PREVIEW. It never produces the transcript.
//
// The authoritative transcript stays the whole-buffer decode that MediaRecorder's `onstop` hands to
// transcribeAudio(). A segment boundary can land inside a spoken measurement — "three point" |
// "two centimetres" — and decode to a number nobody said, which is precisely what §5.2's token lock
// and §5.3's validation-diff exist to catch. Preview text therefore only ever reaches a display
// slot; it is never inserted into the editor, sent to the formatter, or persisted.
// ─────────────────────────────────────────────────────────────────────────────────────────────

import { SpeechSegmenter, joinPreview } from './chunkedCapture';
import { encodeWavPcm16 } from './wavEncode';

/** The rate the on-device engines expect. */
export const TARGET_SAMPLE_RATE = 16000;

/**
 * Linear-interpolating downsample to {@link TARGET_SAMPLE_RATE}.
 *
 * Browsers commonly hand us 44.1/48 kHz; the engines want 16 kHz mono. Deliberately simple and
 * dependency-free — this feeds the PREVIEW only, so a little aliasing costs a slightly worse
 * preview and nothing clinical. The authoritative path resamples properly via `blobToWav16kMono`.
 */
export function downsampleTo16k(input: Float32Array, inputRate: number): Float32Array {
  if (inputRate === TARGET_SAMPLE_RATE || input.length === 0) return input;
  if (inputRate < TARGET_SAMPLE_RATE) return input; // never upsample; the engine tolerates it

  const ratio = inputRate / TARGET_SAMPLE_RATE;
  const outLength = Math.floor(input.length / ratio);
  const out = new Float32Array(outLength);
  for (let i = 0; i < outLength; i++) {
    const pos = i * ratio;
    const i0 = Math.floor(pos);
    const i1 = Math.min(i0 + 1, input.length - 1);
    const frac = pos - i0;
    out[i] = input[i0] * (1 - frac) + input[i1] * frac;
  }
  return out;
}

export interface InterimDecodeOptions {
  /** Decode one completed segment. Return '' (or reject) to contribute nothing. */
  transcribeSegment: (wav: Blob) => Promise<string>;
  /** Called with the joined preview text whenever it changes. */
  onPreview: (text: string) => void;
  /** Surfaced for logging; a failed preview is never fatal to the dictation. */
  onError?: (e: unknown) => void;
}

/**
 * Start previewing `stream`. Returns a disposer; safe to call more than once.
 *
 * Returns a no-op disposer when the Web Audio API is unavailable — a browser without it must still
 * be able to dictate, just without the live preview.
 */
export function startInterimDecode(
  stream: MediaStream,
  opts: InterimDecodeOptions,
): () => void {
  const Ctor =
    typeof window === 'undefined'
      ? undefined
      : window.AudioContext ??
        (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctor) return () => {};

  let stopped = false;
  const ctx = new Ctor();
  const source = ctx.createMediaStreamSource(stream);
  // ScriptProcessor is deprecated but is the only tap available without shipping a worklet file,
  // and it is well supported in the WebView2/Chromium the desktop runs on.
  const node = ctx.createScriptProcessor(4096, 1, 1);
  const segmenter = new SpeechSegmenter({ sampleRate: TARGET_SAMPLE_RATE });

  // Previews are indexed by segment start so an out-of-order decode cannot scramble the order.
  const previews = new Map<number, string>();
  const emit = () => {
    const ordered = [...previews.entries()].sort((a, b) => a[0] - b[0]).map(([, t]) => t);
    opts.onPreview(joinPreview(ordered));
  };

  const decode = (samples: Float32Array, startSample: number) => {
    // Reserve the slot immediately so ordering holds even if this decode resolves last.
    previews.set(startSample, '');
    const wav = new Blob([encodeWavPcm16(samples, TARGET_SAMPLE_RATE)], { type: 'audio/wav' });
    opts
      .transcribeSegment(wav)
      .then((text) => {
        if (stopped) return;
        previews.set(startSample, text ?? '');
        emit();
      })
      .catch((e) => {
        if (stopped) return;
        previews.delete(startSample);
        opts.onError?.(e);
      });
  };

  node.onaudioprocess = (ev) => {
    if (stopped) return;
    const input = ev.inputBuffer.getChannelData(0);
    // Copy: the browser reuses this buffer between callbacks.
    const samples = downsampleTo16k(Float32Array.from(input), ctx.sampleRate);
    for (const seg of segmenter.push(samples)) decode(seg.samples, seg.startSample);
  };

  source.connect(node);
  // Routed to a muted gain node rather than the speakers: ScriptProcessor only runs while
  // connected to a destination, but feeding the mic to the output would cause audible feedback.
  const mute = ctx.createGain();
  mute.gain.value = 0;
  node.connect(mute);
  mute.connect(ctx.destination);

  return () => {
    if (stopped) return;
    stopped = true;
    try {
      // Decode any speech that never got a closing pause, so a short utterance still previews.
      for (const seg of segmenter.flush()) decode(seg.samples, seg.startSample);
      node.onaudioprocess = null;
      node.disconnect();
      mute.disconnect();
      source.disconnect();
      void ctx.close();
    } catch (e) {
      opts.onError?.(e);
    }
  };
}
