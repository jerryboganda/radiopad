'use client';

/**
 * Continuous dictation for the mobile companion.
 *
 * The old companion used ONLY the browser Web Speech API — which does not work
 * inside the Capacitor Android WebView, so "Hold to dictate" did nothing on a
 * real phone. This picks the right engine at runtime:
 *   - NATIVE (Capacitor Android/iOS): `@capacitor-community/speech-recognition`
 *     with `partialResults` — the on-device recognizer that actually works in the
 *     app, and the only one that streams partials for real-time desktop display.
 *   - WEB (next dev / browser): the Web Speech API, for local testing.
 *
 * Both are exposed as one toggle-able controller emitting the same hooks:
 *   onPartial(text)  running (interim) text of the current utterance
 *   onFinal(text)    a completed utterance, ready to commit on the desktop
 *   onState(on)      listening started / stopped
 *
 * Android's SpeechRecognizer stops itself after a pause, so while the mic toggle
 * is ON we transparently restart it — the radiologist gets one continuous session
 * from a single tap until they tap again.
 */

import { isNativeCapacitorPlatform } from './nativeRuntime';

export interface DictationHooks {
  onPartial: (text: string) => void;
  onFinal: (text: string) => void;
  onState: (listening: boolean) => void;
  onError: (message: string) => void;
}

export interface DictationController {
  stop: () => Promise<void>;
}

type WebSpeechRecognition = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult: ((event: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal?: boolean }> }) => void) | null;
  onerror: ((event: { error?: string }) => void) | null;
  onend: (() => void) | null;
  onstart: (() => void) | null;
  start(): void;
  stop(): void;
};
type WebSpeechCtor = new () => WebSpeechRecognition;

function webCtor(): WebSpeechCtor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as { SpeechRecognition?: WebSpeechCtor; webkitSpeechRecognition?: WebSpeechCtor };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

/** True when SOME dictation engine is usable (native plugin or Web Speech). */
export function dictationAvailable(): boolean {
  return isNativeCapacitorPlatform() || webCtor() != null;
}

/** Start dictating. Resolves once listening has begun (or rejects on a hard failure). */
export async function startDictation(hooks: DictationHooks): Promise<DictationController> {
  return isNativeCapacitorPlatform() ? startNative(hooks) : startWeb(hooks);
}

async function startNative(hooks: DictationHooks): Promise<DictationController> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const mod: any = await import('@capacitor-community/speech-recognition');
  const SR = mod.SpeechRecognition;

  const avail = await SR.available().catch(() => ({ available: false }));
  if (!avail?.available) throw new Error('Speech recognition is not available on this device.');

  let perm = await SR.checkPermissions().catch(() => null);
  if (perm?.speechRecognition !== 'granted') {
    perm = await SR.requestPermissions().catch(() => null);
  }
  if (perm?.speechRecognition !== 'granted') {
    throw new Error('Microphone permission was denied. Enable it in Settings to dictate.');
  }

  let active = true;
  let lastPartial = '';
  // onState fires exactly once each: true when listening truly begins, false when
  // it truly ends (user stop OR an unrecoverable restart failure). The Android
  // recognizer's internal stop→restart cycles between utterances are NOT surfaced,
  // so the UI never flickers — and a dead engine never leaves it stuck on "live".
  let startedEmitted = false;
  let stoppedEmitted = false;
  const emitStopped = () => { if (!stoppedEmitted) { stoppedEmitted = true; hooks.onState(false); } };

  const startEngine = async () => {
    await SR.start({ language: 'en-US', partialResults: true, popup: false });
  };

  const partialH = await SR.addListener('partialResults', (data: { matches?: string[] }) => {
    const t = (data?.matches && data.matches[0]) || '';
    lastPartial = t;
    hooks.onPartial(t);
  });

  const stateH = await SR.addListener('listeningState', async (data: { status?: string }) => {
    if (data?.status === 'started') {
      if (!startedEmitted) { startedEmitted = true; hooks.onState(true); }
      return;
    }
    // 'stopped' — Android ends the recognizer after a pause. Commit the utterance,
    // then restart while the toggle is still on so dictation stays continuous.
    if (lastPartial) { hooks.onFinal(lastPartial); lastPartial = ''; }
    if (!active) { emitStopped(); return; }
    try {
      await startEngine();
    } catch {
      active = false;
      await partialH.remove().catch(() => undefined);
      await stateH.remove().catch(() => undefined);
      hooks.onError('Dictation stopped. Tap the mic to resume.');
      emitStopped();
    }
  });

  try {
    await startEngine();
  } catch (e) {
    await partialH.remove().catch(() => undefined);
    await stateH.remove().catch(() => undefined);
    throw e instanceof Error ? e : new Error('Could not start the microphone.');
  }

  return {
    async stop() {
      active = false;
      try { await SR.stop(); } catch { /* already stopped */ }
      await partialH.remove().catch(() => undefined);
      await stateH.remove().catch(() => undefined);
      if (lastPartial) { hooks.onFinal(lastPartial); lastPartial = ''; }
      emitStopped();
    },
  };
}

async function startWeb(hooks: DictationHooks): Promise<DictationController> {
  const Ctor = webCtor();
  if (!Ctor) throw new Error('Speech recognition is not available in this browser.');

  let active = true;
  let startedEmitted = false;
  let stoppedEmitted = false;
  const emitStopped = () => { if (!stoppedEmitted) { stoppedEmitted = true; hooks.onState(false); } };
  // Terminal errors must NOT be retried, or onend→start→same error spins forever.
  const TERMINAL = new Set(['not-allowed', 'service-not-allowed', 'audio-capture', 'language-not-supported']);

  const rec = new Ctor();
  rec.lang = 'en-US';
  rec.continuous = true;
  rec.interimResults = true;

  rec.onstart = () => { if (!startedEmitted) { startedEmitted = true; hooks.onState(true); } };
  rec.onresult = (event) => {
    let interim = '';
    for (let i = event.resultIndex; i < event.results.length; i += 1) {
      const result = event.results[i];
      const text = result[0]?.transcript ?? '';
      if ((result as { isFinal?: boolean }).isFinal) hooks.onFinal(text);
      else interim += text;
    }
    hooks.onPartial(interim);
  };
  rec.onerror = (ev) => {
    const err = ev.error ?? '';
    if (TERMINAL.has(err)) {
      active = false; // stop the restart loop; onend will emit the terminal stop
      hooks.onError(err === 'not-allowed' || err === 'service-not-allowed'
        ? 'Microphone permission was denied. Enable it to dictate.'
        : `Microphone error: ${err}`);
    } else if (err && err !== 'no-speech' && err !== 'aborted') {
      hooks.onError(`Microphone error: ${err}`);
    }
  };
  rec.onend = () => {
    // Chrome ends after silence; keep it continuous until the toggle is off.
    if (active) { try { rec.start(); } catch { active = false; emitStopped(); } }
    else emitStopped();
  };

  try {
    rec.start();
  } catch (e) {
    throw e instanceof Error ? e : new Error('Could not start the microphone.');
  }

  return {
    async stop() {
      active = false;
      try { rec.stop(); } catch { /* already stopped */ }
      emitStopped();
    },
  };
}
