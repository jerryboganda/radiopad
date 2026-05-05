'use client';

import { useEffect, useRef, useState } from 'react';

/**
 * PRD §17.5 — Web Speech API dictation. The browser handles audio capture +
 * transcription locally via `SpeechRecognition`; the resulting text is fed
 * into the parent through `onTranscript`. No audio leaves the device.
 *
 * Falls back to a disabled button if the browser doesn't expose the
 * SpeechRecognition API (e.g. Firefox, Safari on Linux). Desktop dictation
 * uses Whisper-local through the Tauri sidecar (see `desktop/whisper.md`)
 * so the UI is identical there.
 */
export function DictateButton(props: { onTranscript: (text: string) => void }) {
  const [supported, setSupported] = useState(false);
  const [listening, setListening] = useState(false);
  const recRef = useRef<unknown>(null);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const Ctor = (window as unknown as {
      SpeechRecognition?: new () => unknown;
      webkitSpeechRecognition?: new () => unknown;
    });
    setSupported(!!(Ctor.SpeechRecognition || Ctor.webkitSpeechRecognition));
  }, []);

  function start() {
    if (typeof window === 'undefined') return;
    const Ctor = (window as unknown as {
      SpeechRecognition?: new () => unknown;
      webkitSpeechRecognition?: new () => unknown;
    });
    const Impl = Ctor.SpeechRecognition || Ctor.webkitSpeechRecognition;
    if (!Impl) return;
    const r = new Impl() as {
      continuous: boolean;
      interimResults: boolean;
      lang: string;
      onresult: (e: { results: ArrayLike<{ 0: { transcript: string }; isFinal: boolean }> }) => void;
      onend: () => void;
      start: () => void;
      stop: () => void;
    };
    r.continuous = true;
    r.interimResults = false;
    r.lang = 'en-US';
    r.onresult = (e) => {
      const finals = Array.from(e.results)
        .filter((res) => res.isFinal)
        .map((res) => res[0].transcript)
        .join(' ');
      if (finals) props.onTranscript(finals);
    };
    r.onend = () => setListening(false);
    r.start();
    recRef.current = r;
    setListening(true);
  }

  function stop() {
    const r = recRef.current as { stop: () => void } | null;
    r?.stop();
    setListening(false);
  }

  return (
    <button
      type="button"
      className={listening ? 'primary' : 'subtle'}
      disabled={!supported}
      onClick={() => (listening ? stop() : start())}
      title={supported ? 'Toggle dictation' : 'Web Speech API not supported in this browser'}
    >
      {listening ? 'Stop dictation' : 'Dictate'}
    </button>
  );
}
