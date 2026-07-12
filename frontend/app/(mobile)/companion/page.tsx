'use client';

/**
 * Mobile companion — the entire mobile app.
 *
 * The phone pairs to a *live desktop session* and then acts as a wireless
 * dictation microphone + remote for the report open on that desktop. Pairing is
 * by **scanning the QR the desktop shows**: that QR carries a short-lived
 * companion bearer, so scanning it BOTH authenticates the phone (as the same
 * radiologist) AND joins the session — there is no separate phone login. (Before
 * this, the phone had no way to authenticate, so every pair attempt 401'd and
 * surfaced as "Pairing failed".) Spoken text is streamed over the companion
 * relay ({@link connectCompanion}) to the desktop, which inserts it into the
 * focused section. There is NO standalone reporting here — no editing, no signing.
 *
 * Uses the Web Speech API where available (with the native
 * `@capacitor-community/speech-recognition` plugin as the on-device path). Locked
 * mobile classes: `.rp-mobile`, `.rp-mic-btn`, `.rp-transcript`, `.rp-page-title`,
 * `.rp-page-sub`, `.banner`, `.primary`, `.ghost`, `.subtle`, `.rp-input`.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, setActiveAuthToken, setCompanionBase } from '@/lib/api';
import { setAuthToken } from '@/lib/secureAuth';
import {
  connectCompanion,
  type CompanionConnection,
  type CompanionCommand,
} from '@/lib/companion';
import { decodeCompanionPairing, type CompanionPairingPayload } from '@/lib/companionPairing';
import { nativeScanAvailable, webScanAvailable, scanNative, scanWebcam } from '@/lib/companionScan';

type SpeechRecognitionLike = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult:
    | ((event: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal?: boolean }> }) => void)
    | null;
  onerror: ((event: { error?: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
};
type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

function getSpeechRecognitionCtor(): SpeechRecognitionCtor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function deviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad phone';
  const ua = navigator.userAgent;
  if (/iphone/i.test(ua)) return 'iPhone';
  if (/ipad/i.test(ua)) return 'iPad';
  if (/android/i.test(ua)) return 'Android phone';
  return 'RadioPad companion';
}

const REMOTE_COMMANDS: Array<{ command: CompanionCommand; label: string }> = [
  { command: 'prev_section', label: '‹ Prev section' },
  { command: 'next_section', label: 'Next section ›' },
  { command: 'insert', label: 'Insert' },
  { command: 'undo', label: 'Undo' },
  { command: 'read_back', label: 'Read back' },
];

type Phase = 'pair' | 'connecting' | 'live' | 'ended';

export default function MobileCompanionPage() {
  const Ctor = useMemo(getSpeechRecognitionCtor, []);
  const [phase, setPhase] = useState<Phase>('pair');
  const [pairing, setPairing] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [showPaste, setShowPaste] = useState(false);
  const [pasteText, setPasteText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [hostName, setHostName] = useState<string>('');
  const [section, setSection] = useState<string>('');
  const [recording, setRecording] = useState(false);
  const [transcript, setTranscript] = useState('');

  const connRef = useRef<CompanionConnection | null>(null);
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const scanAbortRef = useRef<AbortController | null>(null);

  const stopScan = useCallback(() => {
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
    setScanning(false);
  }, []);

  const teardown = useCallback(() => {
    try { recognitionRef.current?.stop(); } catch { /* noop */ }
    recognitionRef.current = null;
    connRef.current?.close();
    connRef.current = null;
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
  }, []);

  useEffect(() => () => teardown(), [teardown]);

  /** Map a pair-call failure to a message the radiologist can act on. */
  const describePairError = useCallback((e: unknown): string => {
    const ex = e as { status?: number; kind?: string; body?: { error?: string; detail?: string } };
    if (ex.kind === 'network') return 'Could not reach RadioPad. Check your connection and try again.';
    if (ex.status === 401) return 'This pairing expired. On your desktop choose “Start pairing” again, then re-scan.';
    if (ex.status === 404) return 'That pairing code is invalid or has expired. Re-scan the desktop QR.';
    return ex.body?.error ?? ex.body?.detail ?? 'Pairing failed. Re-scan the desktop QR.';
  }, []);

  /** Open the relay once the REST pair succeeds. */
  const connectAfterPair = useCallback((sessionId: string, host: string) => {
    setHostName(host);
    setPhase('connecting');
    const conn = connectCompanion({
      sessionId,
      role: 'companion',
      onOpen: () => setPhase('live'),
      // On an INVOLUNTARY end (relay drop or desktop unpair) tear everything down —
      // critically, stop the running SpeechRecognition so the phone microphone can
      // never stay live after the UI says the session ended.
      onClose: () => { teardown(); setPhase('ended'); },
      onMessage: (msg) => {
        if (msg.type === 'section_context') {
          setSection(msg.sectionTitle || msg.sectionKey || '');
        } else if (msg.type === 'session_ended') {
          teardown();
          setPhase('ended');
        }
      },
      onError: () => setError('Connection interrupted. Re-pair to continue.'),
    });
    connRef.current = conn;
  }, [teardown]);

  /**
   * Adopt a scanned/pasted pairing payload: point at the advertised relay, take
   * on its short-lived bearer (in memory + secure store so a reload survives),
   * then join the session by its code. The bearer is what makes the pair call
   * authenticate — without it the backend 401s (the original bug).
   */
  const pairFromPayload = useCallback(async (payload: CompanionPairingPayload) => {
    stopScan();
    setShowPaste(false);
    setPairing(true);
    setError(null);
    try {
      if (payload.base) setCompanionBase(payload.base);
      setActiveAuthToken(payload.token);
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem('radiopad.tenant', payload.tenant);
        localStorage.setItem('radiopad.user', payload.user);
      }
      // Best-effort persist so the token survives a webview reload within its life.
      void setAuthToken(payload.token).catch(() => undefined);

      const res = await api.companion.pair(payload.code, deviceName());
      connectAfterPair(res.sessionId, res.hostDeviceName);
    } catch (e) {
      setActiveAuthToken(null);
      setError(describePairError(e));
      setPhase('pair');
    } finally {
      setPairing(false);
    }
  }, [connectAfterPair, describePairError, stopScan]);

  /** Scan the desktop QR — native camera on the phone, webcam in a browser. */
  const startScan = useCallback(async () => {
    setError(null);
    setShowPaste(false);
    if (nativeScanAvailable()) {
      setPairing(true);
      try {
        const payload = await scanNative();
        if (payload) {
          await pairFromPayload(payload);
        } else {
          setError('No RadioPad pairing code found. Point the camera at the QR on your desktop.');
        }
      } catch {
        setError('Could not open the camera. Grant camera access, or use “Paste pairing link”.');
      } finally {
        setPairing(false);
      }
      return;
    }
    if (!webScanAvailable()) {
      setShowPaste(true);
      setError('This device can’t scan. Use “Paste pairing link” instead.');
      return;
    }
    // Web/browser: live-preview webcam scan.
    const controller = new AbortController();
    scanAbortRef.current = controller;
    setScanning(true);
    // Defer so the <video> element is mounted before we attach the stream.
    setTimeout(async () => {
      const video = videoRef.current;
      if (!video) { setScanning(false); return; }
      try {
        const payload = await scanWebcam(video, controller.signal);
        if (payload) await pairFromPayload(payload);
        else if (!controller.signal.aborted) setScanning(false);
      } catch {
        setScanning(false);
        setError('Could not open the camera. Grant camera access, or use “Paste pairing link”.');
      }
    }, 0);
  }, [pairFromPayload]);

  /** Manual fallback: paste the pairing link/text shown under the desktop QR. */
  const pairFromPaste = useCallback(async () => {
    const payload = decodeCompanionPairing(pasteText);
    if (!payload) {
      setError('That doesn’t look like a RadioPad pairing link. Copy it from the desktop and try again.');
      return;
    }
    await pairFromPayload(payload);
  }, [pasteText, pairFromPayload]);

  const startRecording = useCallback(() => {
    if (!Ctor || !connRef.current) return;
    const rec = new Ctor();
    rec.lang = 'en-US';
    rec.continuous = true;
    rec.interimResults = true;
    rec.onresult = (event) => {
      let interim = '';
      for (let i = event.resultIndex; i < event.results.length; i += 1) {
        const result = event.results[i];
        const text = result[0]?.transcript ?? '';
        const isFinal = Boolean((result as { isFinal?: boolean }).isFinal);
        if (isFinal) {
          connRef.current?.sendDictation(text, true);
        } else {
          interim += text;
        }
      }
      if (interim) {
        setTranscript(interim);
        connRef.current?.sendDictation(interim, false);
      } else {
        setTranscript('');
      }
    };
    rec.onerror = (ev) => setError(`Microphone error: ${ev.error ?? 'unknown'}`);
    rec.onend = () => setRecording(false);
    recognitionRef.current = rec;
    try {
      rec.start();
      setRecording(true);
      connRef.current.sendCommand('ptt_start');
    } catch {
      setError('Could not start the microphone.');
    }
  }, [Ctor]);

  const stopRecording = useCallback(() => {
    try { recognitionRef.current?.stop(); } catch { /* noop */ }
    recognitionRef.current = null;
    setRecording(false);
    setTranscript('');
    connRef.current?.sendCommand('ptt_stop');
  }, []);

  const sendCommand = useCallback((command: CompanionCommand) => {
    connRef.current?.sendCommand(command);
  }, []);

  if (phase === 'pair' || phase === 'connecting') {
    const busy = pairing || phase === 'connecting';
    return (
      <div className="rp-mobile">
        <h1 className="rp-page-title">Pair with desktop</h1>
        <p className="rp-page-sub">
          Open a report on your RadioPad desktop, choose <strong>Pair phone</strong>, then
          <strong> scan the QR</strong> shown there. Your phone becomes a wireless dictation mic for
          that report — no sign-in needed.
        </p>
        {error && <div className="banner warn" role="alert">{error}</div>}

        {scanning ? (
          <>
            {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
            <video
              ref={videoRef}
              playsInline
              muted
              style={{ width: '100%', maxHeight: '60vh', borderRadius: 12, background: '#000', objectFit: 'cover' }}
            />
            <p className="rp-page-sub" style={{ textAlign: 'center' }}>Point the camera at the desktop QR…</p>
            <button className="ghost" type="button" onClick={stopScan}>Cancel</button>
          </>
        ) : (
          <>
            <button className="primary" type="button" onClick={startScan} disabled={busy}>
              {phase === 'connecting' ? 'Connecting…' : pairing ? 'Pairing…' : 'Scan QR to pair'}
            </button>

            <button
              className="subtle"
              type="button"
              onClick={() => { setShowPaste((v) => !v); setError(null); }}
              disabled={busy}
            >
              Can’t scan? Paste pairing link
            </button>

            {showPaste && (
              <>
                <textarea
                  className="rp-input"
                  rows={3}
                  placeholder="Paste the pairing link shown under the desktop QR"
                  aria-label="Pairing link"
                  value={pasteText}
                  onChange={(e) => setPasteText(e.target.value)}
                  disabled={busy}
                  style={{ width: '100%', fontFamily: 'monospace', fontSize: '0.85rem' }}
                />
                <button className="primary" type="button" onClick={pairFromPaste} disabled={busy || !pasteText.trim()}>
                  Pair from link
                </button>
              </>
            )}
          </>
        )}
      </div>
    );
  }

  if (phase === 'ended') {
    return (
      <div className="rp-mobile">
        <h1 className="rp-page-title">Session ended</h1>
        <p className="rp-page-sub">The desktop session closed. Scan again to keep dictating.</p>
        <button className="primary" type="button" onClick={() => { setPhase('pair'); setPasteText(''); setError(null); }}>
          Pair again
        </button>
      </div>
    );
  }

  // phase === 'live'
  return (
    <div className="rp-mobile">
      <h1 className="rp-page-title">Dictating to {hostName || 'desktop'}</h1>
      <p className="rp-page-sub">
        {section ? <>Active section: <strong>{section}</strong></> : 'Hold the mic and speak — text lands on the desktop.'}
      </p>

      <button
        className="rp-mic-btn"
        type="button"
        aria-pressed={recording}
        onPointerDown={startRecording}
        onPointerUp={stopRecording}
        onPointerLeave={() => recording && stopRecording()}
        disabled={!Ctor}
      >
        {recording ? 'Listening… release to send' : 'Hold to dictate'}
      </button>

      {!Ctor && (
        <div className="banner warn" role="alert">
          Speech recognition isn’t available on this device’s browser.
        </div>
      )}

      {transcript && <div className="rp-transcript" aria-live="polite">{transcript}</div>}

      <div className="rp-companion-remote" role="group" aria-label="Remote controls">
        {REMOTE_COMMANDS.map((c) => (
          <button key={c.command} className="ghost" type="button" onClick={() => sendCommand(c.command)}>
            {c.label}
          </button>
        ))}
      </div>

      <button className="subtle" type="button" onClick={() => { teardown(); setPhase('ended'); }}>
        End session
      </button>
    </div>
  );
}
