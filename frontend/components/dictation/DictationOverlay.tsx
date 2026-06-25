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
import { blobToWav16kMono } from '@/lib/dictation/wavEncode';
import {
  getSpeechRecognitionCtor,
  parseSpeechResults,
  type SpeechRecognitionLike,
} from '@/lib/dictation/speech';
import { api } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';

const DICTATE_EVENT = 'radiopad:dictate';
const CLEANUP_EVENT = 'radiopad:dictation-cleanup';

export default function DictationOverlay() {
  const [supported, setSupported] = useState(false);
  const [listening, setListening] = useState(false);
  const [interim, setInterim] = useState('');
  const recognizerRef = useRef<SpeechRecognitionLike | null>(null);
  const listeningRef = useRef(false);
  listeningRef.current = listening;

  // High-accuracy (HQ) path: record mic audio with MediaRecorder, send to the
  // backend transcribe endpoint (UBAG-routed), and insert the transcript. This
  // is the path that works in the desktop WebView2 shell, where Web Speech is
  // unavailable.
  const [transcribing, setTranscribing] = useState(false);
  const [audioRecording, setAudioRecording] = useState(false);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const audioStreamRef = useRef<MediaStream | null>(null);

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

  const transcribeAudio = useCallback(async (blob: Blob) => {
    const reportId = readQueryParam('id');
    const target = getLastFocusedEditable();
    if (!reportId || !target || blob.size === 0) return;
    setTranscribing(true);
    try {
      // Desktop runs an on-device STT engine that needs 16 kHz mono WAV; the
      // WebView2 Chromium engine has the Opus codec, so convert here and the
      // server decodes WAV in-process (no ffmpeg). The web keeps the original
      // recording (cloud transcription accepts it). If conversion fails, send the
      // original and let the backend fall back to the cloud path.
      let payload = blob;
      if (typeof window !== 'undefined' && '__TAURI__' in window) {
        try {
          payload = await blobToWav16kMono(blob);
        } catch {
          payload = blob;
        }
      }
      const res = await api.reports.transcribe(reportId, payload);
      if (res.transcript) insertAtCursor(target, formatDictation(res.transcript));
    } catch {
      // Any provider/policy error surfaces as a non-2xx; the editor handles the
      // error envelope. Insert nothing on failure.
    } finally {
      setTranscribing(false);
    }
  }, []);

  const startAudioCapture = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) return;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioStreamRef.current = stream;
      audioChunksRef.current = [];
      const rec = new MediaRecorder(stream, { mimeType: 'audio/webm' });
      rec.ondataavailable = (ev) => {
        if (ev.data && ev.data.size > 0) audioChunksRef.current.push(ev.data);
      };
      rec.onstop = () => {
        const blob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
        audioStreamRef.current?.getTracks().forEach((t) => t.stop());
        audioStreamRef.current = null;
        void transcribeAudio(blob);
      };
      mediaRecorderRef.current = rec;
      rec.start();
      setAudioRecording(true);
    } catch {
      setAudioRecording(false);
    }
  }, [transcribeAudio]);

  const stopAudioCapture = useCallback(() => {
    const rec = mediaRecorderRef.current;
    if (rec && rec.state !== 'inactive') rec.stop();
    mediaRecorderRef.current = null;
    setAudioRecording(false);
  }, []);

  const toggleAudio = useCallback(() => {
    if (audioRecording) stopAudioCapture();
    else void startAudioCapture();
  }, [audioRecording, startAudioCapture, stopAudioCapture]);

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
          data-testid="dictation-hq"
          className={`rp-dictation-fix subtle${audioRecording ? ' recording' : ''}`}
          aria-pressed={audioRecording}
          title="Record and transcribe with high accuracy (UBAG audio)"
          disabled={transcribing}
          onMouseDown={(e) => e.preventDefault()}
          onClick={toggleAudio}
        >
          {transcribing ? '…' : audioRecording ? 'Stop' : 'HQ'}
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
