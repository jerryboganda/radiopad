'use client';

// Global floating dictation overlay. Replaces the old full-screen
// `/mobile/dictate` navigation in the report editor: a persistent mic icon the
// radiologist toggles on/off. While ON, each finalised utterance is
// auto-formatted and inserted at the caret of whichever text field they last
// focused — so they can click between Findings / Impression / etc. and keep
// dictating without leaving the editor.
//
// Engine = browser Web Speech API (works in Chromium/Chrome/Edge). It is NOT
// available in the Tauri desktop WebView2 shell, where the mic shows a disabled
// state until the UBAG audio path (Phase B/C) lands. The "Fix" button asks the
// host page to clean the dictation into medical phrasing via UBAG.

import { useCallback, useEffect, useRef, useState } from 'react';
import { startFocusTracking, getLastFocusedEditable } from '@/lib/dictation/focusTracker';
import { insertAtCursor } from '@/lib/dictation/insertText';
import { formatDictation } from '@/lib/dictation/medicalFormat';
import {
  getSpeechRecognitionCtor,
  parseSpeechResults,
  type SpeechRecognitionLike,
} from '@/lib/dictation/speech';

const DICTATE_EVENT = 'radiopad:dictate';
const CLEANUP_EVENT = 'radiopad:dictation-cleanup';

export default function DictationOverlay() {
  const [supported, setSupported] = useState(false);
  const [listening, setListening] = useState(false);
  const [interim, setInterim] = useState('');
  const recognizerRef = useRef<SpeechRecognitionLike | null>(null);
  const listeningRef = useRef(false);
  listeningRef.current = listening;

  useEffect(() => {
    setSupported(getSpeechRecognitionCtor() !== null);
    const stopTracking = startFocusTracking();
    return () => {
      stopTracking();
      recognizerRef.current?.stop();
    };
  }, []);

  const stop = useCallback(() => {
    recognizerRef.current?.stop();
    recognizerRef.current = null;
    setListening(false);
    setInterim('');
  }, []);

  const start = useCallback(() => {
    const Ctor = getSpeechRecognitionCtor();
    if (!Ctor) return;
    const rec = new Ctor();
    rec.lang = 'en-US';
    rec.continuous = true;
    rec.interimResults = true;
    rec.onresult = (event) => {
      const { finalText, interimText } = parseSpeechResults(event);
      setInterim(interimText);
      if (finalText) {
        const target = getLastFocusedEditable();
        if (target) insertAtCursor(target, formatDictation(finalText));
        setInterim('');
      }
    };
    rec.onerror = () => setListening(false);
    rec.onend = () => {
      setListening(false);
      setInterim('');
    };
    recognizerRef.current = rec;
    try {
      rec.start();
      setListening(true);
    } catch {
      setListening(false);
    }
  }, []);

  const toggle = useCallback(() => {
    if (listeningRef.current) stop();
    else start();
  }, [start, stop]);

  // Desktop hotkey (Ctrl+Shift+D) and the editor's Dictate button both dispatch
  // `radiopad:dictate`; this is the single owner that toggles the overlay.
  useEffect(() => {
    const handler = () => toggle();
    window.addEventListener(DICTATE_EVENT, handler);
    return () => window.removeEventListener(DICTATE_EVENT, handler);
  }, [toggle]);

  const title = !supported
    ? 'Dictation needs the audio engine — unavailable in this build'
    : listening
      ? 'Stop dictation'
      : 'Start dictation';

  return (
    <div className="rp-dictation" data-testid="dictation-overlay">
      {listening && interim && (
        <div className="rp-dictation-interim" data-testid="dictation-interim">
          {interim}
        </div>
      )}
      <div className="rp-dictation-controls">
        <button
          type="button"
          data-testid="dictation-fab"
          className={`rp-dictation-fab${listening ? ' recording' : ''}`}
          aria-pressed={listening}
          aria-label={title}
          title={title}
          disabled={!supported}
          onMouseDown={(e) => e.preventDefault()}
          onClick={toggle}
        >
          <span className="rp-dictation-dot" aria-hidden="true" />
          <span className="rp-dictation-label">{listening ? 'Listening…' : 'Dictate'}</span>
        </button>
        <button
          type="button"
          data-testid="dictation-fix"
          className="rp-dictation-fix subtle"
          title="Clean up the dictation into medical phrasing (UBAG)"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => window.dispatchEvent(new CustomEvent(CLEANUP_EVENT))}
        >
          Fix
        </button>
      </div>
    </div>
  );
}
