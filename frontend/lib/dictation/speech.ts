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
