// Phase 1 (local STT) — convert a recorded audio Blob into 16 kHz mono 16-bit
// PCM WAV, entirely in the browser. The desktop shell uses this before uploading
// dictation audio so the on-device STT engine (sherpa-onnx / Parakeet) receives
// exactly the format it needs, and the server decodes WAV in-process — no ffmpeg.
//
// The WebView2 (Chromium) engine ships the Opus decoder, so `decodeAudioData`
// turns the MediaRecorder webm/opus blob into PCM; an OfflineAudioContext then
// down-mixes to mono and resamples to 16 kHz.

const TARGET_SAMPLE_RATE = 16000;

/**
 * Encode mono float samples ([-1, 1]) as a 16-bit PCM WAV. Pure + synchronous
 * (no Web Audio), so it is unit-testable outside a browser.
 */
export function encodeWavPcm16(samples: Float32Array, sampleRate: number): ArrayBuffer {
  const numFrames = samples.length;
  const dataSize = numFrames * 2;
  const buffer = new ArrayBuffer(44 + dataSize);
  const view = new DataView(buffer);

  const writeStr = (off: number, s: string) => {
    for (let i = 0; i < s.length; i++) view.setUint8(off + i, s.charCodeAt(i));
  };

  writeStr(0, 'RIFF');
  view.setUint32(4, 36 + dataSize, true);
  writeStr(8, 'WAVE');
  writeStr(12, 'fmt ');
  view.setUint32(16, 16, true); // fmt chunk size
  view.setUint16(20, 1, true); // PCM
  view.setUint16(22, 1, true); // mono
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true); // byte rate (mono * 2 bytes)
  view.setUint16(32, 2, true); // block align
  view.setUint16(34, 16, true); // bits per sample
  writeStr(36, 'data');
  view.setUint32(40, dataSize, true);

  let off = 44;
  for (let i = 0; i < numFrames; i++) {
    let s = Math.max(-1, Math.min(1, samples[i]));
    s = s < 0 ? s * 0x8000 : s * 0x7fff;
    view.setInt16(off, s | 0, true);
    off += 2;
  }
  return buffer;
}

type AudioCtor = typeof AudioContext;
type OfflineCtor = typeof OfflineAudioContext;

function getAudioContextCtor(): AudioCtor | undefined {
  if (typeof window === 'undefined') return undefined;
  const w = window as unknown as { AudioContext?: AudioCtor; webkitAudioContext?: AudioCtor };
  return w.AudioContext ?? w.webkitAudioContext;
}

function getOfflineCtor(): OfflineCtor | undefined {
  if (typeof window === 'undefined') return undefined;
  const w = window as unknown as { OfflineAudioContext?: OfflineCtor; webkitOfflineAudioContext?: OfflineCtor };
  return w.OfflineAudioContext ?? w.webkitOfflineAudioContext;
}

/**
 * Decode any recorded audio Blob and re-encode it as 16 kHz mono WAV. Throws if
 * the Web Audio API is unavailable (callers should fall back to the original).
 */
export async function blobToWav16kMono(blob: Blob): Promise<Blob> {
  const AC = getAudioContextCtor();
  const OAC = getOfflineCtor();
  if (!AC || !OAC) throw new Error('Web Audio API unavailable');

  const arrayBuf = await blob.arrayBuffer();

  const decodeCtx = new AC();
  let decoded: AudioBuffer;
  try {
    // slice(0) — decodeAudioData detaches the buffer it is given.
    decoded = await decodeCtx.decodeAudioData(arrayBuf.slice(0));
  } finally {
    void decodeCtx.close?.();
  }

  const frames = Math.max(1, Math.ceil(decoded.duration * TARGET_SAMPLE_RATE));
  // A 1-channel OfflineAudioContext down-mixes the source and renders at 16 kHz.
  const offline = new OAC(1, frames, TARGET_SAMPLE_RATE);
  const src = offline.createBufferSource();
  src.buffer = decoded;
  src.connect(offline.destination);
  src.start();
  const rendered = await offline.startRendering();

  const wav = encodeWavPcm16(rendered.getChannelData(0), TARGET_SAMPLE_RATE);
  return new Blob([wav], { type: 'audio/wav' });
}
