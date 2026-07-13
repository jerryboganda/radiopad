'use client';

/**
 * Mobile companion — the entire mobile app.
 *
 * The phone pairs to a live desktop session by scanning the desktop QR, then acts
 * as a wireless MICROPHONE: it captures voice and streams the raw audio directly
 * to the desktop over the local network (WebRTC data channel — audio never touches
 * the cloud), and the DESKTOP transcribes it with its on-device engine and inserts
 * the text. (The old on-phone Android speech recognizer is gone — its few-second
 * endpointing made dictation choppy.) There is NO standalone reporting here.
 *
 * Locked mobile classes: `.rp-mobile`, `.rp-mic-btn`, `.rp-page-title`,
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
import { startAudioCapture, audioCaptureAvailable, describeCaptureError, type AudioCaptureController } from '@/lib/companionAudioCapture';
import { ensureMicPermission } from '@/lib/companionSpeech';
import { createRtcPeer, type RtcPeer } from '@/lib/companionRtc';
import MobileUpdateCheck from '@/components/companion/MobileUpdateCheck';

function deviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad phone';
  const ua = navigator.userAgent;
  if (/iphone/i.test(ua)) return 'iPhone';
  if (/ipad/i.test(ua)) return 'iPad';
  if (/android/i.test(ua)) return 'Android phone';
  return 'RadioPad companion';
}

const REMOTE_COMMANDS: Array<{ command: CompanionCommand; label: string }> = [
  { command: 'prev_section', label: '‹ Prev' },
  { command: 'next_section', label: 'Next ›' },
  { command: 'jump_findings', label: 'Findings' },
  { command: 'jump_impression', label: 'Impression' },
  { command: 'new_line', label: '↵ New line' },
  { command: 'undo', label: '⤺ Undo' },
];

type Phase = 'pair' | 'connecting' | 'live' | 'ended';
type LinkState = 'connecting' | 'connected' | 'failed';

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
  const [speaking, setSpeaking] = useState(false);
  const [link, setLink] = useState<LinkState>('connecting');

  const connRef = useRef<CompanionConnection | null>(null);
  const rtcRef = useRef<RtcPeer | null>(null);
  const captureRef = useRef<AudioCaptureController | null>(null);
  const micBusyRef = useRef(false);
  const micWantedRef = useRef(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const scanAbortRef = useRef<AbortController | null>(null);

  const stopScan = useCallback(() => {
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
    setScanning(false);
  }, []);

  const stopCapture = useCallback(() => {
    micWantedRef.current = false;
    const c = captureRef.current;
    captureRef.current = null;
    void c?.stop().catch(() => undefined);
    setRecording(false);
    setSpeaking(false);
  }, []);

  const teardown = useCallback(() => {
    stopCapture();
    rtcRef.current?.close();
    rtcRef.current = null;
    connRef.current?.close();
    connRef.current = null;
    scanAbortRef.current?.abort();
    scanAbortRef.current = null;
  }, [stopCapture]);

  useEffect(() => () => teardown(), [teardown]);

  const describePairError = useCallback((e: unknown): string => {
    const ex = e as { status?: number; kind?: string; body?: { error?: string; detail?: string } };
    if (ex.kind === 'network') return 'Could not reach RadioPad. Check your connection and try again.';
    if (ex.status === 401) return 'This pairing expired. On your desktop choose “Start pairing” again, then re-scan.';
    if (ex.status === 404) return 'That pairing code is invalid or has expired. Re-scan the desktop QR.';
    return ex.body?.error ?? ex.body?.detail ?? 'Pairing failed. Re-scan the desktop QR.';
  }, []);

  /**
   * Build a FRESH WebRTC peer to answer a desktop offer. Always recreated (never
   * reused) so a desktop "Retry connection" — a brand-new offer — gets a clean peer
   * AND resets the per-connection send seq in lockstep with the desktop's fresh
   * receiver. On failure the mic is stopped so it can never stay hot after the
   * link drops (the mic button is hidden while disconnected).
   */
  const createFreshRtcPeer = useCallback((): RtcPeer => {
    rtcRef.current?.close();
    setLink('connecting');
    const peer = createRtcPeer({
      role: 'companion',
      sendSignal: (s) => connRef.current?.sendSignal(s),
      onState: (state) => {
        if (state === 'connected') setLink('connected');
        else if (state === 'failed') { setLink('failed'); stopCapture(); }
      },
      onFailed: () => { setLink('failed'); stopCapture(); },
    });
    rtcRef.current = peer;
    return peer;
  }, [stopCapture]);

  /** Open the relay once the REST pair succeeds. */
  const connectAfterPair = useCallback((sessionId: string, host: string) => {
    setHostName(host);
    setPhase('connecting');
    setLink('connecting');
    const conn = connectCompanion({
      sessionId,
      role: 'companion',
      onOpen: () => setPhase('live'),
      onClose: () => { teardown(); setPhase('ended'); },
      onMessage: (msg) => {
        if (msg.type === 'section_context') {
          setSection(msg.sectionTitle || msg.sectionKey || '');
        } else if (msg.type === 'rtc_offer') {
          // A (re)offer from the desktop — always answer with a fresh peer.
          void createFreshRtcPeer().handleSignal(msg);
        } else if (msg.type === 'rtc_answer' || msg.type === 'rtc_ice' || msg.type === 'rtc_bye') {
          void rtcRef.current?.handleSignal(msg);
        } else if (msg.type === 'peer_left' || msg.type === 'session_ended') {
          // Desktop dropped or the session ended — stop the mic and close down.
          teardown();
          setPhase('ended');
        }
      },
      onError: () => setError('Connection interrupted. Re-pair to continue.'),
    });
    connRef.current = conn;
  }, [teardown, createFreshRtcPeer]);

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

  const startScan = useCallback(async () => {
    setError(null);
    setShowPaste(false);
    if (nativeScanAvailable()) {
      setPairing(true);
      try {
        const payload = await scanNative();
        if (payload) await pairFromPayload(payload);
        else setError('No RadioPad pairing code found. Point the camera at the QR on your desktop.');
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

  const pairFromPaste = useCallback(async () => {
    const payload = decodeCompanionPairing(pasteText);
    if (!payload) {
      setError('That doesn’t look like a RadioPad pairing link. Copy it from the desktop and try again.');
      return;
    }
    await pairFromPayload(payload);
  }, [pasteText, pairFromPayload]);

  /**
   * Toggle the mic. ON captures continuously and streams per-phrase audio segments
   * to the desktop over the LAN data channel; the desktop transcribes + inserts.
   */
  const toggleMic = useCallback(async () => {
    if (micBusyRef.current) return;
    micBusyRef.current = true;
    try {
      if (captureRef.current) {
        connRef.current?.sendCommand('ptt_stop');
        stopCapture();
        return;
      }
      if (link !== 'connected') {
        setError('Still connecting to your desktop over Wi-Fi…');
        return;
      }
      if (!audioCaptureAvailable()) {
        setError('This device can’t capture audio.');
        return;
      }
      // Mark intent BEFORE the permission prompt so a session that ends while the
      // OS dialog is up (teardown clears micWantedRef) can't leave the mic hot.
      setError(null);
      setRecording(true);
      micWantedRef.current = true;
      const granted = await ensureMicPermission();
      if (!micWantedRef.current) { setRecording(false); return; } // ended during prompt
      if (!granted) {
        micWantedRef.current = false;
        setRecording(false);
        setError('Microphone permission was denied. Enable it in Settings to dictate.');
        return;
      }
      connRef.current?.sendCommand('ptt_start');
      try {
        const controller = await startAudioCapture({
          onSegment: (blob) => { void rtcRef.current?.sendSegment(blob); },
          onSpeaking: (s) => setSpeaking(s),
          onError: (message) => setError(message),
        });
        if (!micWantedRef.current) {
          void controller.stop().catch(() => undefined);
          setRecording(false);
        } else {
          captureRef.current = controller;
        }
      } catch (e) {
        micWantedRef.current = false;
        setRecording(false);
        connRef.current?.sendCommand('ptt_stop');
        setError(describeCaptureError(e));
      }
    } finally {
      micBusyRef.current = false;
    }
  }, [link, stopCapture]);

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

        {!scanning && <MobileUpdateCheck />}
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
        <MobileUpdateCheck />
      </div>
    );
  }

  // phase === 'live'
  return (
    <div className="rp-mobile">
      <h1 className="rp-page-title">Dictating to {hostName || 'desktop'}</h1>
      <p className="rp-page-sub">
        {section ? <>Active section: <strong>{section}</strong></> : 'Tap the mic and speak — your voice is transcribed on the desktop.'}
      </p>

      {error && <div className="banner warn" role="alert">{error}</div>}

      {link !== 'connected' ? (
        <div className="banner" role="status">
          {link === 'failed' ? (
            <>Couldn’t reach your desktop. Make sure this phone and the desktop are on the <strong>same Wi‑Fi network</strong>, then tap <strong>Retry</strong> on the desktop’s pairing panel.</>
          ) : (
            <>Connecting to your desktop over Wi‑Fi…</>
          )}
        </div>
      ) : (
        <>
          <button
            className={`rp-mic-btn${recording ? ' recording is-live' : ''}`}
            type="button"
            aria-pressed={recording}
            onClick={toggleMic}
          >
            {recording ? (speaking ? 'Listening…' : 'Mic on — tap to stop') : 'Tap to dictate'}
          </button>

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
        </>
      )}

      <button className="subtle" type="button" onClick={() => { teardown(); setPhase('ended'); }}>
        End session
      </button>
    </div>
  );
}
