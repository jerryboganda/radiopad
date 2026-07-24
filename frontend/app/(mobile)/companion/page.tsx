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
import {
  ArrowRight,
  ChevronRight,
  HelpCircle,
  Link2,
  QrCode,
  ShieldCheck,
} from 'lucide-react';
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
import CompanionSplash from '@/components/companion/CompanionSplash';
import ThemeToggle from '@/components/ui/ThemeToggle';

/** Decorative phone-scanning-QR mark for the pairing hero. Purely
 * illustrative (aria-hidden by its caller) — hand-built SVG in the same
 * isometric-plate style as AuthScaffold's ShowcaseMark, sized for a
 * light card background instead of the dark auth aside. */
function PairIllustration() {
  return (
    <svg viewBox="0 0 300 200" fill="none" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="comp-illus-plate" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#5cb0ff" />
          <stop offset="1" stopColor="#1f6fb8" />
        </linearGradient>
      </defs>
      {/* laptop, back layer */}
      <rect x="18" y="60" width="150" height="98" rx="10" fill="var(--bg-panel)" stroke="var(--border)" strokeWidth="1.5" />
      <rect x="30" y="72" width="126" height="74" rx="4" fill="var(--bg-subtle)" />
      <rect x="44" y="86" width="98" height="98" rx="10" fill="url(#comp-illus-plate)" opacity="0.14" />
      {[0, 1, 2].map((i) => (
        <rect key={i} x={58 + i * 4} y={100 + i * 4} width={70 - i * 8} height={70 - i * 8} rx={4} fill="none" stroke="url(#comp-illus-plate)" strokeWidth="1.4" opacity={0.35 + i * 0.15} />
      ))}
      <rect x="10" y="158" width="166" height="8" rx="3" fill="var(--border)" />
      {/* phone, front layer */}
      <g transform="translate(150 20)">
        <rect x="0" y="0" width="112" height="168" rx="20" fill="var(--bg-panel)" stroke="var(--border)" strokeWidth="1.5" />
        <rect x="7" y="9" width="98" height="150" rx="12" fill="var(--bg-subtle)" />
        <rect x="24" y="26" width="64" height="64" rx="8" fill="#ffffff" stroke="url(#comp-illus-plate)" strokeWidth="2" />
        {[16, 30, 44, 58, 72].map((x, i) => (
          <rect key={x} x={x} y={40} width="4" height={i % 2 === 0 ? 36 : 24} fill="url(#comp-illus-plate)" opacity="0.85" />
        ))}
        <rect x="34" y="104" width="44" height="6" rx="3" fill="var(--border)" />
        <rect x="34" y="116" width="30" height="6" rx="3" fill="var(--border)" />
        <circle cx="56" cy="146" r="9" fill="none" stroke="var(--accent)" strokeWidth="2" />
      </g>
      {/* connecting signal arcs */}
      <path d="M120 46 Q136 40 148 46" stroke="var(--accent)" strokeWidth="2" strokeLinecap="round" opacity="0.55" />
      <path d="M116 56 Q136 46 152 56" stroke="var(--accent)" strokeWidth="2" strokeLinecap="round" opacity="0.35" />
    </svg>
  );
}

function CompanionTopbar() {
  return (
    <div className="rp-comp-topbar">
      <div className="rp-comp-brand">
        <span className="brand-mark" aria-hidden><span className="brand-mark-letter">R</span></span>
        <span className="rp-comp-brand-text">
          <span className="rp-comp-brand-name">RadioPad</span>
          <span className="rp-comp-brand-kicker">AI-assisted radiology reporting</span>
        </span>
      </div>
      <ThemeToggle />
    </div>
  );
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
  const [showHelp, setShowHelp] = useState(false);
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
      <div className="rp-comp-screen">
        <CompanionSplash />
        <CompanionTopbar />
        <div className="rp-comp-body rp-stagger">
          {scanning ? (
            <>
              <div className="rp-comp-hero">
                <h1 className="rp-comp-title">Point at the QR</h1>
                <p className="rp-comp-sub">Center the desktop&rsquo;s pairing QR code in the frame.</p>
              </div>
              <div className="rp-comp-scanwrap">
                {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
                <video ref={videoRef} playsInline muted />
                <div className="rp-comp-scan-frame" aria-hidden>
                  <span /><span /><span /><span />
                  <div className="rp-comp-scan-laser" />
                </div>
              </div>
              {error && <div className="banner warn" role="alert" style={{ marginTop: 14 }}>{error}</div>}
              <div className="rp-comp-actions" style={{ marginTop: 16 }}>
                <button className="ghost" type="button" onClick={stopScan}>Cancel</button>
              </div>
            </>
          ) : (
            <>
              <div className="rp-comp-hero">
                <h1 className="rp-comp-title">
                  Connect to
                  <span className="rp-comp-title-accent">your desktop</span>
                </h1>
                <p className="rp-comp-sub">
                  Pair with the RadioPad desktop app to turn your phone into a wireless dictation
                  microphone for fast, accurate reporting.
                </p>
              </div>

              <div className="rp-comp-illus" aria-hidden><PairIllustration /></div>

              <div className="rp-comp-card">
                <span className="rp-comp-card-icon"><Link2 size={20} aria-hidden /></span>
                <span className="rp-comp-card-text">
                  <span className="rp-comp-card-title">Secure pairing</span>
                  <p className="rp-comp-card-sub">Scan the QR code shown in RadioPad on your desktop. No sign-in required.</p>
                  <span className="rp-comp-badge"><ShieldCheck size={12} aria-hidden /> Encrypted &amp; private</span>
                </span>
              </div>

              {error && <div className="banner warn" role="alert" style={{ marginBottom: 14 }}>{error}</div>}

              <div className="rp-comp-actions">
                <button className="primary rp-auth-submit" type="button" onClick={startScan} disabled={busy}>
                  <QrCode className="rp-auth-submit-lead" size={18} aria-hidden />
                  <span>{phase === 'connecting' ? 'Connecting…' : pairing ? 'Pairing…' : 'Scan QR to pair'}</span>
                  <ArrowRight className="rp-auth-submit-trail" size={18} aria-hidden />
                </button>

                <button
                  type="button"
                  className="rp-auth-method"
                  onClick={() => { setShowPaste((v) => !v); setError(null); }}
                  disabled={busy}
                >
                  <span className="rp-auth-method-icon tone-blue"><Link2 size={19} aria-hidden /></span>
                  <span className="rp-auth-method-text">
                    <span className="rp-auth-method-title">Paste pairing link</span>
                    <span className="rp-auth-method-sub">Can&rsquo;t scan? Enter the link from your desktop</span>
                  </span>
                  <ChevronRight className="rp-auth-method-chev" size={18} aria-hidden />
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

                <button type="button" className="rp-auth-method" onClick={() => setShowHelp((v) => !v)}>
                  <span className="rp-auth-method-icon tone-purple"><HelpCircle size={19} aria-hidden /></span>
                  <span className="rp-auth-method-text">
                    <span className="rp-auth-method-title">Need help?</span>
                    <span className="rp-auth-method-sub">How pairing works</span>
                  </span>
                  <ChevronRight className="rp-auth-method-chev" size={18} aria-hidden />
                </button>
                {showHelp && (
                  <p className="rp-comp-sub">
                    Open a report on your RadioPad desktop, choose <strong>Pair phone</strong>, then scan
                    the QR shown there. Your phone becomes a wireless dictation mic for that report.
                  </p>
                )}
              </div>

              <div className="rp-comp-footer"><MobileUpdateCheck /></div>
            </>
          )}
        </div>
      </div>
    );
  }

  if (phase === 'ended') {
    return (
      <div className="rp-comp-screen">
        <CompanionSplash />
        <CompanionTopbar />
        <div className="rp-comp-body rp-stagger">
          <div className="rp-comp-hero">
            <h1 className="rp-comp-title">Session ended</h1>
            <p className="rp-comp-sub">The desktop session closed. Scan again to keep dictating.</p>
          </div>
          <div className="rp-comp-actions">
            <button
              className="primary rp-auth-submit"
              type="button"
              onClick={() => { setPhase('pair'); setPasteText(''); setError(null); }}
            >
              <QrCode className="rp-auth-submit-lead" size={18} aria-hidden />
              <span>Pair again</span>
              <ArrowRight className="rp-auth-submit-trail" size={18} aria-hidden />
            </button>
          </div>
          <div className="rp-comp-footer"><MobileUpdateCheck /></div>
        </div>
      </div>
    );
  }

  // phase === 'live'
  return (
    <div className="rp-comp-screen">
      <CompanionTopbar />
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
    </div>
  );
}
