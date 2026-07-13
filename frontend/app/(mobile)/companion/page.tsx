'use client';

/**
 * Mobile companion — the entire mobile app.
 *
 * The phone pairs to a live desktop session by scanning the desktop QR, then
 * dictates in one of two modes:
 * - "Wi-Fi mic": pure microphone — voice streams as raw audio directly to the
 *   desktop over the local network (WebRTC data channel — audio never touches
 *   the cloud) and the DESKTOP transcribes it with its on-device engine.
 * - "Keyboard voice": the phone keyboard's own voice typing (Gboard / iOS
 *   dictation) recognizes speech instantly ON the phone; the text streams live
 *   over the relay (works on any connection, no LAN link needed).
 * (The old on-phone Android SpeechRecognizer is gone — its few-second
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
import { createTypeDictationStreamer, type TypeDictationStreamer } from '@/lib/companionTypeDictation';
import { formatDictation } from '@/lib/dictation/medicalFormat';
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
/**
 * Two ways to dictate:
 * - 'voice': the phone is a pure mic — audio streams to the desktop over the
 *   LAN data channel, the desktop's on-device engine transcribes (private,
 *   same-Wi-Fi only).
 * - 'type': the phone KEYBOARD does the recognition (Gboard / iOS voice
 *   typing — instant + very accurate); text streams live over the relay, so it
 *   also works when the direct Wi-Fi link can't form.
 */
type InputMode = 'voice' | 'type';

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
  const [inputMode, setInputMode] = useState<InputMode>('voice');
  const [typedText, setTypedText] = useState('');
  const [lastInserted, setLastInserted] = useState('');

  const connRef = useRef<CompanionConnection | null>(null);
  const rtcRef = useRef<RtcPeer | null>(null);
  const captureRef = useRef<AudioCaptureController | null>(null);
  const typeStreamerRef = useRef<TypeDictationStreamer | null>(null);
  // Committed-prefix guard: a voice-typed word can land between the idle-commit
  // and React clearing the textarea; that change event still carries the
  // committed text as a prefix. Strip it or the phrase inserts twice.
  const justCommittedRef = useRef<{ raw: string; at: number } | null>(null);
  // True while the Android/iOS IME has an active composition — the idle-commit
  // must not clear the textarea mid-composition (value clobbering duplicates or
  // mangles the phrase on some IMEs).
  const composingRef = useRef(false);
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
    // Best-effort commit of words typed but not yet inserted (the send rides the
    // relay if it is still open; buffered/no-op otherwise), then drop the
    // streamer AND its UI state — stale text must not survive into a re-paired
    // session, where a fresh streamer would silently commit '' (dead Insert).
    typeStreamerRef.current?.commit();
    typeStreamerRef.current?.dispose();
    typeStreamerRef.current = null;
    setTypedText('');
    setLastInserted('');
    justCommittedRef.current = null;
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
          // Aborted mid-start (session ended / mode switched during the prompt):
          // ptt_start already went out, so close the desktop "Listening" state.
          void controller.stop().catch(() => undefined);
          setRecording(false);
          connRef.current?.sendCommand('ptt_stop');
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

  // ——— Type mode (phone keyboard voice typing → live text over the relay) ———

  const ensureTypeStreamer = useCallback((): TypeDictationStreamer => {
    if (!typeStreamerRef.current) {
      typeStreamerRef.current = createTypeDictationStreamer({
        send: (text, isFinal) => connRef.current?.sendDictation(text, isFinal),
        format: formatDictation,
        // Never auto-commit (→ clear the field) while the IME is composing.
        deferIdleCommit: () => composingRef.current,
        onCommitted: (formatted, raw) => {
          justCommittedRef.current = { raw, at: Date.now() };
          setTypedText('');
          setLastInserted(formatted);
        },
      });
    }
    return typeStreamerRef.current;
  }, []);

  const onTypedChange = useCallback((value: string) => {
    let next = value;
    const jc = justCommittedRef.current;
    if (jc && (Date.now() - jc.at >= 400 || !next.startsWith(jc.raw))) {
      // Window passed, or the IME state no longer relates to the committed text.
      justCommittedRef.current = null;
    } else if (jc) {
      // Keep the guard alive for the WHOLE window (multiple events can race the
      // clear) and strip exact matches too — the IME re-finalizing the committed
      // text with no new word must strip to '', not resurrect the phrase (which
      // would re-commit and double-insert it into the report).
      next = next.slice(jc.raw.length);
    }
    setTypedText(next);
    if (next.trim()) setLastInserted('');
    ensureTypeStreamer().onTextChange(next);
  }, [ensureTypeStreamer]);

  const insertTypedNow = useCallback(() => {
    // Re-seed from the live field first: after a session teardown recreated the
    // streamer, its internal text is empty while the textarea still shows words —
    // committing without the resync would silently insert nothing.
    const s = ensureTypeStreamer();
    s.onTextChange(typedText);
    s.commit();
  }, [ensureTypeStreamer, typedText]);

  const switchMode = useCallback((mode: InputMode) => {
    if (mode === inputMode) return;
    if (mode === 'type') {
      // Leaving voice mode: the mic must never stay hot in the background, and
      // ptt_stop goes unconditionally — ptt_start is sent BEFORE the async
      // capture start, so gating on captureRef would strand the desktop on
      // "Listening" if the user switches during the permission prompt.
      connRef.current?.sendCommand('ptt_stop');
      stopCapture();
    } else {
      // Leaving type mode: don't lose words that were typed but not yet inserted,
      // and reset the desktop's "Listening" indicator.
      typeStreamerRef.current?.commit();
      typeStreamerRef.current?.dispose();
      typeStreamerRef.current = null;
      setTypedText('');
      connRef.current?.sendCommand('ptt_stop');
    }
    setError(null);
    setInputMode(mode);
  }, [inputMode, stopCapture]);

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

      <div className="rp-companion-remote" role="group" aria-label="Dictation mode">
        <button
          className={inputMode === 'voice' ? 'primary' : 'ghost'}
          type="button"
          aria-pressed={inputMode === 'voice'}
          onClick={() => switchMode('voice')}
        >
          Wi‑Fi mic
        </button>
        <button
          className={inputMode === 'type' ? 'primary' : 'ghost'}
          type="button"
          aria-pressed={inputMode === 'type'}
          onClick={() => switchMode('type')}
        >
          Keyboard voice
        </button>
      </div>

      {inputMode === 'voice' ? (
        link !== 'connected' ? (
          <div className="banner" role="status">
            {link === 'failed' ? (
              <>Couldn’t reach your desktop over Wi‑Fi. Make sure both are on the <strong>same network</strong> and tap <strong>Retry</strong> on the desktop — or switch to <strong>Keyboard voice</strong> above, which works on any connection.</>
            ) : (
              <>Connecting to your desktop over Wi‑Fi…</>
            )}
          </div>
        ) : (
          <button
            className={`rp-mic-btn${recording ? ' recording is-live' : ''}`}
            type="button"
            aria-pressed={recording}
            onClick={toggleMic}
          >
            {recording ? (speaking ? 'Listening…' : 'Mic on — tap to stop') : 'Tap to dictate'}
          </button>
        )
      ) : (
        <>
          <p className="rp-page-sub">
            Tap the box, then press the <strong>mic key on your keyboard</strong> (Gboard voice
            typing). Words appear on the desktop as you speak; a short pause inserts them.
          </p>
          <textarea
            className="rp-input"
            rows={5}
            placeholder="Tap here, then press the mic on your keyboard…"
            aria-label="Dictation text"
            value={typedText}
            onChange={(e) => onTypedChange(e.target.value)}
            onFocus={() => sendCommand('ptt_start')}
            onBlur={() => {
              // Commit whatever is pending (existing streamer only — blur after a
              // session ended must not resurrect a fresh streamer) and close the
              // desktop "Listening" state.
              composingRef.current = false;
              typeStreamerRef.current?.commit();
              sendCommand('ptt_stop');
            }}
            onCompositionStart={() => { composingRef.current = true; }}
            onCompositionEnd={() => { composingRef.current = false; }}
            autoCapitalize="sentences"
            autoComplete="off"
            spellCheck
            style={{ width: '100%', fontSize: '1.05rem', lineHeight: 1.45 }}
          />
          <button
            className="primary"
            type="button"
            onClick={insertTypedNow}
            disabled={!typedText.trim()}
          >
            Insert into report now
          </button>
          {lastInserted && !typedText && (
            <p className="rp-page-sub" role="status" aria-live="polite">
              Inserted: “{lastInserted.length > 90 ? `${lastInserted.slice(0, 90)}…` : lastInserted}”
            </p>
          )}
        </>
      )}

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
