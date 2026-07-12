'use client';

/**
 * Mobile companion — the entire mobile app.
 *
 * The phone pairs to a *live desktop session* by scanning the desktop QR (which
 * carries a short-lived companion bearer, so scanning authenticates AND joins —
 * no phone login), then acts as a wireless dictation microphone + remote for the
 * report open on that desktop. Spoken text streams over the companion relay
 * ({@link connectCompanion}); PARTIAL results land live at the desktop caret and
 * finals commit into the focused section. There is NO standalone reporting here.
 *
 * Dictation uses the native `@capacitor-community/speech-recognition` engine on
 * the phone (the browser Web Speech API does not work in the Android WebView) via
 * {@link startDictation}. The mic is a simple ON/OFF toggle. Locked mobile
 * classes: `.rp-mobile`, `.rp-mic-btn`, `.rp-transcript`, `.rp-page-title`,
 * `.rp-page-sub`, `.banner`, `.primary`, `.ghost`, `.subtle`, `.rp-input`.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { api, setActiveAuthToken, setCompanionBase } from '@/lib/api';
import { setAuthToken } from '@/lib/secureAuth';
import {
  connectCompanion,
  type CompanionConnection,
  type CompanionCommand,
} from '@/lib/companion';
import { decodeCompanionPairing, type CompanionPairingPayload } from '@/lib/companionPairing';
import { nativeScanAvailable, webScanAvailable, scanNative, scanWebcam } from '@/lib/companionScan';
import { startDictation, dictationAvailable, type DictationController } from '@/lib/companionSpeech';

function deviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad phone';
  const ua = navigator.userAgent;
  if (/iphone/i.test(ua)) return 'iPhone';
  if (/ipad/i.test(ua)) return 'iPad';
  if (/android/i.test(ua)) return 'Android phone';
  return 'RadioPad companion';
}

// Remote controls a radiologist reaches for while dictating hands-free: navigate
// between sections, jump straight to the two they live in (Findings / Impression),
// break a line, and undo. "Generate impression (AI)" is rendered separately as a
// bigger, distinct action below the grid.
const REMOTE_COMMANDS: Array<{ command: CompanionCommand; label: string }> = [
  { command: 'prev_section', label: '‹ Prev' },
  { command: 'next_section', label: 'Next ›' },
  { command: 'jump_findings', label: 'Findings' },
  { command: 'jump_impression', label: 'Impression' },
  { command: 'new_line', label: '↵ New line' },
  { command: 'undo', label: '⤺ Undo' },
];

type Phase = 'pair' | 'connecting' | 'live' | 'ended';

export default function MobileCompanionPage() {
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
  const dictationRef = useRef<DictationController | null>(null);
  const micBusyRef = useRef(false);
  // Intent flag: true only while the user wants the mic on. Cleared synchronously
  // by stopDictation/teardown so a `startDictation` that resolves AFTER the session
  // ended can immediately shut its engine down instead of leaving a hot mic.
  const micWantedRef = useRef(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const scanAbortRef = useRef<AbortController | null>(null);

  const stopScan = useCallback(() => {
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
    setScanning(false);
  }, []);

  const stopDictation = useCallback(() => {
    micWantedRef.current = false;
    const c = dictationRef.current;
    dictationRef.current = null;
    void c?.stop().catch(() => undefined);
    setRecording(false);
    setTranscript('');
  }, []);

  const teardown = useCallback(() => {
    stopDictation();
    connRef.current?.close();
    connRef.current = null;
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
  }, [stopDictation]);

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
      // critically, stop dictation so the phone mic can never stay live after the
      // UI says the session ended.
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
   * Adopt a scanned/pasted pairing payload: point at the advertised relay, take on
   * its short-lived bearer (memory + secure store so a reload survives), then join
   * by code. The bearer is what makes the pair call authenticate.
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
    const controller = new AbortController();
    scanAbortRef.current = controller;
    setScanning(true);
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

  /**
   * Toggle the mic. The user tap is authoritative: turning ON streams partials
   * (real-time) + finals to the desktop and tells it the mic is live; turning OFF
   * stops the engine. Android's recognizer auto-restarts between utterances
   * internally (see companionSpeech) — the desktop indicator follows the tap, not
   * those internal restarts, so it never flickers.
   */
  const toggleMic = useCallback(async () => {
    // Guard against a rapid double-tap racing two engine starts before the first
    // `await startDictation` resolves (dictationRef is still null in that window).
    if (micBusyRef.current) return;
    micBusyRef.current = true;
    try {
      if (dictationRef.current) {
        connRef.current?.sendCommand('ptt_stop');
        stopDictation();
        return;
      }
      if (!dictationAvailable()) {
        setError('Speech recognition isn’t available on this device.');
        return;
      }
      setError(null);
      setRecording(true);
      micWantedRef.current = true;
      connRef.current?.sendCommand('ptt_start');
      try {
        const controller = await startDictation({
          onPartial: (text) => {
            setTranscript(text);
            connRef.current?.sendDictation(text, false);
          },
          onFinal: (text) => {
            setTranscript('');
            if (text) connRef.current?.sendDictation(text, true);
          },
          onState: (listening) => {
            // Only a TERMINAL stop is surfaced (companionSpeech hides the Android
            // engine's internal restart cycles). When it fires — user stop, lost
            // permission, or the engine gave up — make sure neither the phone nor
            // the desktop is left showing a live mic.
            if (listening) return;
            dictationRef.current = null;
            micWantedRef.current = false;
            setRecording(false);
            setTranscript('');
            connRef.current?.sendCommand('ptt_stop');
          },
          onError: (message) => setError(message),
        });
        if (!micWantedRef.current) {
          // The session ended (teardown) while we were starting — never leave the
          // engine running. Shut the just-started controller down immediately.
          void controller.stop().catch(() => undefined);
          setRecording(false);
        } else {
          dictationRef.current = controller;
        }
      } catch (e) {
        micWantedRef.current = false;
        setRecording(false);
        connRef.current?.sendCommand('ptt_stop');
        setError(e instanceof Error ? e.message : 'Could not start the microphone.');
      }
    } finally {
      micBusyRef.current = false;
    }
  }, [stopDictation]);

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
  const canDictate = dictationAvailable();
  return (
    <div className="rp-mobile">
      <h1 className="rp-page-title">Dictating to {hostName || 'desktop'}</h1>
      <p className="rp-page-sub">
        {section ? <>Active section: <strong>{section}</strong></> : 'Tap the mic and speak — text lands on the desktop live.'}
      </p>

      {error && <div className="banner warn" role="alert">{error}</div>}

      <button
        className={`rp-mic-btn${recording ? ' recording is-live' : ''}`}
        type="button"
        aria-pressed={recording}
        onClick={toggleMic}
        disabled={!canDictate}
      >
        {recording ? 'Listening — tap to stop' : 'Tap to dictate'}
      </button>

      {!canDictate && (
        <div className="banner warn" role="alert">
          Speech recognition isn’t available on this device.
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

      <button
        className="primary-ghost"
        type="button"
        onClick={() => sendCommand('generate_impression')}
        style={{ width: '100%' }}
      >
        ✨ Generate impression (AI)
      </button>

      <button className="subtle" type="button" onClick={() => { teardown(); setPhase('ended'); }}>
        End session
      </button>
    </div>
  );
}
