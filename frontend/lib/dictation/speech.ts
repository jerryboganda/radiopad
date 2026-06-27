// Thin, framework-free helpers around the browser Web Speech API. Kept separate
// from the React overlay so the pure parsing logic is unit-testable and so the
// constructor lookup reads `window` lazily (tests can stub it).
//
// NOTE: Web Speech only works in Chromium/Chrome/Edge browsers. It is NOT
// available inside the Tauri desktop WebView2 shell — there the overlay shows a
// disabled state until the audio engine (Phase B/C) lands.

export interface SpeechAlternativeLike {
  transcript: string;
}

export interface SpeechResultItemLike extends ArrayLike<SpeechAlternativeLike> {
  isFinal: boolean;
}

export interface SpeechResultEventLike {
  resultIndex: number;
  results: ArrayLike<SpeechResultItemLike>;
}

export interface SpeechRecognitionLike {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult: ((event: SpeechResultEventLike) => void) | null;
  onerror: ((event: { error?: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

export type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

export function getSpeechRecognitionCtor(): SpeechRecognitionCtor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

/** True when a Web Speech recognizer constructor is present in this runtime. */
export function isWebSpeechPresent(): boolean {
  return getSpeechRecognitionCtor() !== null;
}

/**
 * Actively probe whether Web Speech recognition WORKS in this runtime (the Edge /
 * WebView2 case): construct a recognizer, start it, and resolve on the first signal.
 * Modern Windows WebView2 backs the API with Microsoft's speech service and works;
 * older runtimes / non-Edge embeds throw a `network` (or `not-allowed`) error on
 * start. We stop immediately so the probe is unobtrusive. `not-allowed` means the
 * API is present but mic permission was denied — that is reported as "available"
 * (the engine works; the user just needs to grant the mic), distinct from the hard
 * `network`/missing failures that mean it cannot run here.
 */
export function probeWebSpeechAvailable(timeoutMs = 4000): Promise<{ ok: boolean; error?: string }> {
  return new Promise((resolve) => {
    const Ctor = getSpeechRecognitionCtor();
    if (!Ctor) {
      resolve({ ok: false, error: 'not-present' });
      return;
    }
    let settled = false;
    let rec: SpeechRecognitionLike | null = null;
    const done = (result: { ok: boolean; error?: string }) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try {
        rec?.stop();
      } catch {
        /* ignore */
      }
      resolve(result);
    };
    const timer = setTimeout(() => done({ ok: false, error: 'timeout' }), timeoutMs);
    try {
      rec = new Ctor();
      rec.lang = 'en-US';
      rec.interimResults = false;
      rec.continuous = false;
      // A started recognizer (onstart fires before the first result) proves the
      // engine is reachable in this runtime.
      const recWithStart = rec as SpeechRecognitionLike & { onstart?: (() => void) | null };
      recWithStart.onstart = () => done({ ok: true });
      rec.onresult = () => done({ ok: true });
      rec.onerror = (e) => {
        const err = e?.error ?? 'error';
        // Mic permission can be granted later — the engine itself is available.
        done(err === 'not-allowed' || err === 'no-speech' ? { ok: true } : { ok: false, error: err });
      };
      rec.onend = () => done({ ok: settled ? true : false, error: settled ? undefined : 'ended' });
      rec.start();
    } catch (e) {
      done({ ok: false, error: (e as Error)?.message ?? 'start-failed' });
    }
  });
}

/** Split a recognition event into the newly-final and still-interim text. */
export function parseSpeechResults(
  event: SpeechResultEventLike,
  fromIndex: number = event.resultIndex,
): { finalText: string; interimText: string } {
  let finalText = '';
  let interimText = '';
  for (let i = fromIndex; i < event.results.length; i += 1) {
    const item = event.results[i];
    const alternative = item && item[0];
    if (!alternative) continue;
    if (item.isFinal) finalText += alternative.transcript;
    else interimText += alternative.transcript;
  }
  return { finalText: finalText.trim(), interimText: interimText.trim() };
}
