'use client';

/**
 * Phone-side voice capture for the companion dictation.
 *
 * The phone is a pure microphone: it captures audio continuously and cuts it
 * into per-phrase segments at natural speech pauses (voice-activity detection),
 * then hands each self-contained webm segment to the caller to stream to the
 * desktop (which transcribes it with the real on-device engine). This replaces
 * Android's native SpeechRecognizer, whose few-second endpointing made continuous
 * dictation choppy.
 *
 * Why cut MediaRecorder at each pause rather than run one continuous stream: only
 * the FIRST webm chunk carries the container header, so a continuously-sliced
 * stream can't be split into independently-decodable blobs. Stopping the recorder
 * at a detected pause yields a complete, header-bearing, decodable segment; the
 * stop→start gap falls inside the silence, so no speech is lost.
 */

const SPEECH_RMS = 0.015; // RMS above this = speech (silence is ~0.001–0.005)
const HANGOVER_MS = 800; // trailing silence that ends a phrase
const MIN_SPEECH_MS = 300; // ignore sub-blips (clicks, coughs)
// Safety cut for an unbroken monologue. Kept long so it rarely fires mid-speech
// (the stop→restart gap only loses audio when it cuts mid-word — natural pauses
// almost always arrive first at conversational cadence).
const MAX_SEGMENT_MS = 20_000;
const POLL_MS = 40;

export interface AudioCaptureHooks {
  /** One self-contained webm segment per phrase, in capture order. */
  onSegment: (blob: Blob) => void;
  /** True when the user starts speaking, false at the phrase pause (drives the mic dot). */
  onSpeaking: (speaking: boolean) => void;
  onError: (message: string) => void;
}

export interface AudioCaptureController {
  stop: () => Promise<void>;
}

/** True when this device can capture audio for streaming (getUserMedia + MediaRecorder). */
export function audioCaptureAvailable(): boolean {
  return typeof navigator !== 'undefined'
    && !!navigator.mediaDevices?.getUserMedia
    && typeof MediaRecorder !== 'undefined';
}

function pickMimeType(): string | undefined {
  const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4'];
  for (const m of candidates) {
    try { if (MediaRecorder.isTypeSupported?.(m)) return m; } catch { /* ignore */ }
  }
  return undefined; // let MediaRecorder choose its default
}

export async function startAudioCapture(hooks: AudioCaptureHooks): Promise<AudioCaptureController> {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    video: false,
  });

  const mimeType = pickMimeType();
  const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
  const blobMime = recorder.mimeType || mimeType || 'audio/webm';

  const audioCtx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)();
  // The context was created AFTER the async getUserMedia, so some WebViews start
  // it 'suspended' — resume it or the analyser reads silence and VAD never fires.
  if (audioCtx.state === 'suspended') { await audioCtx.resume().catch(() => undefined); }
  const source = audioCtx.createMediaStreamSource(stream);
  const analyser = audioCtx.createAnalyser();
  analyser.fftSize = 2048;
  source.connect(analyser);
  const timeBuf = new Float32Array(analyser.fftSize);

  let active = true;
  let chunks: BlobPart[] = [];
  let hadSpeech = false;
  let speaking = false;
  let lastSpeechAt = 0;
  let speechStartedAt = 0;
  let recorderStartedAt = 0;

  recorder.ondataavailable = (ev) => {
    if (ev.data && ev.data.size > 0) chunks.push(ev.data);
  };
  recorder.onstop = () => {
    const parts = chunks;
    chunks = [];
    const longEnough = speechStartedAt > 0 && lastSpeechAt - speechStartedAt >= MIN_SPEECH_MS;
    if (hadSpeech && longEnough && parts.length > 0) {
      hooks.onSegment(new Blob(parts, { type: blobMime }));
    }
    hadSpeech = false;
    speechStartedAt = 0;
    if (active) {
      try { recorder.start(); recorderStartedAt = now(); } catch { /* stream gone */ }
    }
  };
  recorder.onerror = () => hooks.onError('Recording error — tap the mic to resume.');

  function now(): number {
    return typeof performance !== 'undefined' ? performance.now() : Date.now();
  }

  function cut() {
    if (recorder.state === 'recording') {
      try { recorder.stop(); } catch { /* already stopping */ }
    }
  }

  const poll = setInterval(() => {
    if (!active) return;
    analyser.getFloatTimeDomainData(timeBuf);
    let sum = 0;
    for (let i = 0; i < timeBuf.length; i += 1) sum += timeBuf[i] * timeBuf[i];
    const rms = Math.sqrt(sum / timeBuf.length);
    const t = now();

    if (rms > SPEECH_RMS) {
      lastSpeechAt = t;
      if (!hadSpeech) { hadSpeech = true; speechStartedAt = t; }
      if (!speaking) { speaking = true; hooks.onSpeaking(true); }
    } else if (speaking && t - lastSpeechAt > HANGOVER_MS) {
      // End of phrase: emit the segment and immediately re-arm for the next.
      speaking = false;
      hooks.onSpeaking(false);
      cut();
    }

    // Safety valve: a long unbroken monologue still gets flushed periodically.
    if (hadSpeech && recorderStartedAt > 0 && t - recorderStartedAt > MAX_SEGMENT_MS) {
      cut();
    }
  }, POLL_MS);

  try {
    recorder.start();
    recorderStartedAt = now();
  } catch (e) {
    clearInterval(poll);
    stream.getTracks().forEach((tk) => tk.stop());
    void audioCtx.close().catch(() => undefined);
    throw e instanceof Error ? e : new Error('Could not start the microphone.');
  }

  return {
    async stop() {
      active = false;
      clearInterval(poll);
      if (recorder.state === 'recording') {
        try { recorder.stop(); } catch { /* noop */ }
      }
      if (speaking) { speaking = false; hooks.onSpeaking(false); }
      stream.getTracks().forEach((tk) => tk.stop());
      try { source.disconnect(); } catch { /* noop */ }
      await audioCtx.close().catch(() => undefined);
    },
  };
}
