'use client';

/**
 * Desktop companion host. Shown while a report is open: the radiologist taps
 * "Pair phone", RadioPad advertises a short-lived session (code + QR), and once
 * the phone joins it streams dictation here. Received text is inserted into the
 * focused section through the SAME registry the local dictation overlay uses
 * ({@link getLastFocusedSectionEditor}), so companion dictation is
 * indistinguishable from local dictation downstream — validation, cross-check,
 * `.ai-mark`, and manual signing all still apply. RadioPad never auto-signs.
 *
 * Desktop-only (mounted from ReportClient). The relay is the cloud host; the
 * phone reaches it by code.
 *
 * `open` is controlled by the caller (the composer ribbon's "Pair phone"
 * button) rather than owned internally, so the same toggle that opens this
 * panel also drives the ribbon button's `aria-expanded`/active state.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { api, companionBase } from '@/lib/api';
import { encodeCompanionPairing } from '@/lib/companionPairing';
import {
  connectCompanion,
  type CompanionConnection,
  type CompanionCommand,
} from '@/lib/companion';
import { createRtcPeer, type RtcPeer } from '@/lib/companionRtc';
import { createAudioReceiver, type AudioReceiver } from '@/lib/companionAudioReceiver';
import { blobToWav16kMono } from '@/lib/dictation/wavEncode';
import { formatDictation } from '@/lib/dictation/medicalFormat';
import { getSttMode } from '@/lib/dictation/sttMode';
import { raceTimeout } from '@/lib/asyncTimeout';
import { readQueryParam } from '@/lib/browserParams';
import {
  focusAdjacentSection,
  getLastFocusedSectionEditor,
  getSectionEditor,
  getSectionEditorsInOrder,
} from '@/lib/editor/sectionEditorRegistry';

type Phase = 'idle' | 'advertising' | 'paired' | 'error';
/** State of the direct phone↔desktop LAN audio link (WebRTC). */
type LinkState = 'idle' | 'connecting' | 'connected' | 'failed';

// A hung decode or engine call must become a skippable error, never a frozen
// "Transcribing…" (the FIFO stalls totally behind one unsettled await). The
// engine budget is generous because the first phrase after app start can pay
// the Parakeet cold-load; a phrase that trips it is skipped and the queue
// moves on (the engine usually finishes warming meanwhile).
const DECODE_TIMEOUT_MS = 20_000;
const TRANSCRIBE_TIMEOUT_MS = 60_000;
/** After this long on one phrase, escalate the status line to "warming up". */
const SLOW_TRANSCRIBE_HINT_MS = 8_000;

function hostDeviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad desktop';
  if (/mac/i.test(navigator.platform)) return 'Mac desktop';
  if (/win/i.test(navigator.platform)) return 'Windows desktop';
  return 'RadioPad desktop';
}

export default function CompanionHostPanel({ open }: { open: boolean }) {
  const [phase, setPhase] = useState<Phase>('idle');
  const [code, setCode] = useState<string>('');
  const [qr, setQr] = useState<string>('');
  const [companionName, setCompanionName] = useState<string>('');
  const [phoneListening, setPhoneListening] = useState(false);
  const [link, setLink] = useState<LinkState>('idle');
  const [transcribing, setTranscribing] = useState(false);
  const [slowTranscribe, setSlowTranscribe] = useState(false);
  // Increments at each phrase start. React batches the busy false→true flip
  // between consecutive queued phrases, so `transcribing` alone can stay true
  // across many fast phrases and the "warming up" hint would fire spuriously.
  const [phraseSeq, setPhraseSeq] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const connRef = useRef<CompanionConnection | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const rtcRef = useRef<RtcPeer | null>(null);
  const receiverRef = useRef<AudioReceiver | null>(null);
  // In-flight engine call, so Unpair/teardown can cancel it instead of leaving
  // a dangling fetch against the loopback sidecar.
  const transcribeAbortRef = useRef<AbortController | null>(null);
  // Section key currently showing the live (interim) preview, so we can clear it
  // even after focus has moved on to a different section.
  const interimTargetRef = useRef<string | null>(null);

  const sendSectionContext = useCallback(() => {
    const current = getLastFocusedSectionEditor();
    if (current) connRef.current?.sendSectionContext(current.sectionKey, current.sectionKey);
  }, []);

  // Drop the live preview wherever it currently is (the tracked section AND the
  // now-focused one, in case focus moved) and forget the anchor.
  const clearInterimEverywhere = useCallback(() => {
    const prev = interimTargetRef.current;
    if (prev) getSectionEditor(prev)?.clearInterim?.();
    getLastFocusedSectionEditor()?.clearInterim?.();
    interimTargetRef.current = null;
  }, []);

  // Tear down the direct LAN audio link + its transcription queue.
  const stopRtc = useCallback(() => {
    rtcRef.current?.close();
    rtcRef.current = null;
    receiverRef.current?.reset();
    receiverRef.current = null;
    transcribeAbortRef.current?.abort();
    transcribeAbortRef.current = null;
    setLink('idle');
    setTranscribing(false);
  }, []);

  // "Transcribing…" that runs long is almost always the engine cold-loading —
  // tell the radiologist that instead of looking frozen. Keyed to phraseSeq so
  // the 8s window restarts per phrase, not cumulatively across a busy queue.
  useEffect(() => {
    setSlowTranscribe(false);
    if (!transcribing) return undefined;
    const t = setTimeout(() => setSlowTranscribe(true), SLOW_TRANSCRIBE_HINT_MS);
    return () => clearTimeout(t);
  }, [transcribing, phraseSeq]);

  /**
   * Companion audio can only be transcribed by the sidecar engines (they accept
   * a WAV blob; the Edge Web Speech card is a live-mic recognizer and can't).
   * Probe readiness as soon as the phone link comes up so "model never
   * downloaded" surfaces as an actionable banner immediately — not as a hang
   * after the first phrase.
   */
  const checkEngineReady = useCallback(async () => {
    try {
      const peer = rtcRef.current;
      const res = await api.localModels.list();
      // Torn down / re-connected while probing — this result belongs to a dead
      // session; don't paint its banner over the new state.
      if (rtcRef.current !== peer || !peer) return;
      if (!res.enabled) return; // web/dev build — hosted transcribe path, nothing to probe
      const blobCapable = res.models.filter(
        (m) => m.kind === 'Stt' && !m.placeholder && m.provisioning !== 'BrowserWebSpeech',
      );
      if (blobCapable.length > 0 && !blobCapable.some((m) => m.available)) {
        const downloading = blobCapable.some((m) => m.progress?.state === 'Downloading' || m.progress?.state === 'Verifying' || m.progress?.state === 'Extracting' || m.progress?.state === 'Installing');
        setError(downloading
          ? 'The on-device speech model is still downloading — phone phrases will transcribe once it finishes (watch Settings → On-device models). Type mode on the phone works right now.'
          : 'Phone dictation needs the on-device speech engine. Open Settings → On-device models and download the speech model — or use Type mode on the phone meanwhile.');
      }
    } catch {
      // Sidecar unreachable right now — the per-phrase timeout will surface it.
    }
  }, []);

  /**
   * Bring up the direct phone→desktop LAN audio link. The desktop is the WebRTC
   * offerer; the phone streams per-phrase audio over the data channel (never the
   * cloud), and each segment is transcribed on-device by the SAME engine as local
   * dictation, strictly in capture order, then inserted into the focused section.
   */
  const startRtc = useCallback(() => {
    stopRtc();
    const receiver = createAudioReceiver({
      transcribe: async (webm) => {
        // Decode phone audio → 16 kHz WAV for the sidecar. If this WebView can't
        // decode the phone's codec, fall back to the raw container — the sidecar
        // also accepts audio/webm and decodes it server-side. Deadline so a hung
        // decode can never freeze the queue.
        let wav: Blob;
        try {
          wav = await raceTimeout(blobToWav16kMono(webm), DECODE_TIMEOUT_MS, 'decode timed out');
        } catch {
          wav = webm;
        }
        // The session may have been torn down / replaced while decoding — bail
        // before registering an abort controller that would clobber the live
        // session's (the receiver's generation guard drops the thrown error).
        if (receiverRef.current !== receiver) throw new Error('Session ended.');
        const reportId = readQueryParam('id') ?? '';
        // Same engine mode as local dictation; abortable + hard deadline so one
        // hung engine call (cold ONNX load, backlogged sidecar) can never freeze
        // the queue — that was the frozen "Transcribing…".
        const ctrl = new AbortController();
        transcribeAbortRef.current = ctrl;
        const timer = setTimeout(() => ctrl.abort(), TRANSCRIBE_TIMEOUT_MS);
        try {
          const res = await raceTimeout(
            api.reports.transcribe(reportId, wav, getSttMode(), ctrl.signal),
            TRANSCRIBE_TIMEOUT_MS + 5_000,
            'The speech engine timed out.',
          );
          return formatDictation(res.transcript ?? '');
        } catch (e) {
          if (ctrl.signal.aborted) {
            throw new Error('The speech engine timed out on a phrase — skipped it. It may still be warming up; keep dictating.');
          }
          // Same warm-up mapping as local dictation (DictationOverlay): 503 /
          // stt_unavailable = engine present but model not ready; TypeError =
          // sidecar not reachable (still booting).
          const ex = e as { status?: number; body?: { error?: string; kind?: string }; message?: string };
          if (ex.status === 503 || ex.body?.kind === 'stt_unavailable') {
            throw new Error('The on-device speech engine isn’t ready — its model may still be downloading. Check Settings → On-device models, or use Type mode on the phone.');
          }
          if (e instanceof TypeError) {
            throw new Error('The on-device speech engine is still starting up — keep dictating, phrases will resume shortly.');
          }
          throw new Error(ex.body?.error ?? ex.message ?? 'Could not transcribe a phrase.');
        } finally {
          clearTimeout(timer);
          if (transcribeAbortRef.current === ctrl) transcribeAbortRef.current = null;
        }
      },
      insert: (text) => {
        if (!text) return;
        // If the radiologist hasn't clicked into a section yet, don't drop the
        // dictation — land it in Findings (or the first section) by default.
        const target = getLastFocusedSectionEditor()
          ?? getSectionEditor('findings')
          ?? getSectionEditorsInOrder()[0];
        target?.insertAtCursor(text);
        // A phrase landed — any earlier per-phrase error is stale noise now.
        setError(null);
      },
      onBusyChange: (busy) => {
        setTranscribing(busy);
        if (busy) setPhraseSeq((n) => n + 1);
      },
      onError: (m) => setError(m),
    });
    receiverRef.current = receiver;
    setLink('connecting');
    const peer = createRtcPeer({
      role: 'host',
      sendSignal: (s) => connRef.current?.sendSignal(s),
      onSegment: (blob, seq) => receiver.pushSegment(blob, seq),
      onState: (state) => {
        if (state === 'connected') {
          setLink('connected');
          // Surface "speech model not set up" NOW, not after the first phrase.
          void checkEngineReady();
        } else if (state === 'failed') setLink('failed');
      },
      onFailed: () => setLink('failed'),
    });
    rtcRef.current = peer;
    void peer.startAsHost();
  }, [stopRtc, checkEngineReady]);

  const handleCommand = useCallback((command: CompanionCommand) => {
    // Moving focus off the current section: drop any lingering live preview there
    // first so an interrupted utterance can't leave stale ghost text behind.
    const isNav = command === 'next_section' || command === 'prev_section'
      || command === 'jump_findings' || command === 'jump_impression';
    if (isNav) clearInterimEverywhere();

    switch (command) {
      case 'next_section': focusAdjacentSection(1); break;
      case 'prev_section': focusAdjacentSection(-1); break;
      // Quick jumps to the two sections a radiologist lives in.
      case 'jump_findings': getSectionEditor('findings')?.focus(); break;
      case 'jump_impression': getSectionEditor('impression')?.focus(); break;
      case 'new_line': getLastFocusedSectionEditor()?.newLine?.(); break;
      case 'undo': getLastFocusedSectionEditor()?.undo?.(); break;
      case 'generate_impression':
        // Same path as the desktop hotkey / toolbar button (ReportClient listens).
        if (typeof window !== 'undefined') {
          window.dispatchEvent(new CustomEvent('radiopad:generate-impression'));
        }
        break;
      default: break; // insert / read_back / ptt_* carry no host-side action
    }
    sendSectionContext();
  }, [sendSectionContext, clearInterimEverywhere]);

  const teardown = useCallback(() => {
    stopRtc();
    connRef.current?.close();
    connRef.current = null;
    clearInterimEverywhere();
    const id = sessionIdRef.current;
    sessionIdRef.current = null;
    if (id) void api.companion.endSession(id).catch(() => undefined);
  }, [clearInterimEverywhere, stopRtc]);

  useEffect(() => () => teardown(), [teardown]);

  const startPairing = useCallback(async () => {
    setError(null);
    setCompanionName('');
    setPhase('advertising');
    try {
      const session = await api.companion.createSession(hostDeviceName());
      sessionIdRef.current = session.sessionId;
      setCode(session.pairingCode);
      try {
        // Encode the whole pairing payload (relay base + code + short-lived
        // companion bearer + identity) so the phone authenticates AND pairs from
        // one scan — no separate phone login. If the backend didn't return a
        // token (older build), fall back to the bare code so the QR still shows.
        const qrText = session.companionToken
          ? encodeCompanionPairing({
              base: companionBase(),
              code: session.pairingCode,
              token: session.companionToken,
              tenant: session.tenantSlug ?? '',
              user: session.userEmail ?? '',
            })
          : session.pairingCode;
        setQr(await QRCode.toDataURL(qrText, { margin: 1, width: 200, errorCorrectionLevel: 'M' }));
      } catch {
        setQr('');
      }
      const conn = connectCompanion({
        sessionId: session.sessionId,
        role: 'host',
        onMessage: (msg) => {
          if (msg.type === 'dictation') {
            // One resolved target for BOTH branches — same no-focus fallback as
            // the audio path (Findings → first section), so neither the live
            // preview nor the committed text is ever silently dropped, and the
            // interim anchor always matches where the ghost actually renders.
            const resolved = getLastFocusedSectionEditor()
              ?? getSectionEditor('findings')
              ?? getSectionEditorsInOrder()[0]
              ?? null;
            const targetKey = resolved?.sectionKey ?? null;
            // If the live preview was anchored in a different section, clear it there
            // first so focus changes never strand a ghost.
            const prevKey = interimTargetRef.current;
            if (prevKey && prevKey !== targetKey) getSectionEditor(prevKey)?.clearInterim?.();
            if (msg.isFinal) {
              // Commit the completed utterance and drop the live preview.
              if (msg.text) resolved?.insertAtCursor(msg.text);
              resolved?.clearInterim?.();
              interimTargetRef.current = null;
            } else {
              // Real-time: show the not-yet-final words live at the caret.
              resolved?.setInterim?.(msg.text);
              interimTargetRef.current = targetKey;
            }
          } else if (msg.type === 'rtc_answer' || msg.type === 'rtc_ice' || msg.type === 'rtc_offer' || msg.type === 'rtc_bye') {
            // WebRTC signaling for the direct LAN audio link.
            void rtcRef.current?.handleSignal(msg);
          } else if (msg.type === 'command') {
            // Mic on/off is reflected as a live indicator, not an editor action.
            if (msg.command === 'ptt_start') { setPhoneListening(true); return; }
            if (msg.command === 'ptt_stop') {
              setPhoneListening(false);
              clearInterimEverywhere();
              return;
            }
            handleCommand(msg.command);
          } else if (msg.type === 'peer_joined') {
            setCompanionName(msg.deviceName || 'phone');
            setPhase('paired');
            sendSectionContext();
            // Desktop is the offerer — bring up the direct LAN audio link now.
            startRtc();
          } else if (msg.type === 'peer_left') {
            setPhase('advertising');
            setCompanionName('');
            setPhoneListening(false);
            clearInterimEverywhere();
            stopRtc();
          } else if (msg.type === 'session_ended') {
            setPhase('idle');
            setPhoneListening(false);
            clearInterimEverywhere();
            stopRtc();
          }
        },
        onError: () => setError('Companion relay unreachable.'),
        // When the relay drops the socket, return to idle so the host can
        // re-pair. (Must not read `phase` from this closure — it is captured
        // stale at pairing time; reset unconditionally.)
        onClose: () => setPhase('idle'),
      });
      connRef.current = conn;
    } catch (e) {
      const ex = e as { body?: { error?: string }; message?: string };
      setError(ex.body?.error ?? ex.message ?? 'Could not start pairing.');
      setPhase('error');
    }
  }, [handleCommand, sendSectionContext, clearInterimEverywhere, startRtc, stopRtc]);

  const stop = useCallback(() => {
    teardown();
    setPhase('idle');
    setCode('');
    setQr('');
    setCompanionName('');
    setError(null); // per-phrase / readiness errors die with the session
  }, [teardown]);

  if (!open) return null;

  return (
    <div className="rp-companion-host">
      <div className="rp-panel rp-companion-host-panel" role="dialog" aria-label="Phone companion">
          {error && <div className="banner danger" role="alert">{error}</div>}

          {phase === 'idle' || phase === 'error' ? (
            <>
              <p className="rp-page-sub">
                Pair your phone to dictate into this report wirelessly. It becomes a microphone and
                remote — nothing is edited or signed on the phone.
              </p>
              <button className="primary" type="button" onClick={startPairing}>Start pairing</button>
            </>
          ) : phase === 'advertising' ? (
            <>
              <p className="rp-page-sub">Open the RadioPad phone app and <strong>scan this QR</strong> to pair — no phone sign-in needed:</p>
              {qr && (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={qr} alt="Pairing QR code" width={200} height={200} style={{ display: 'block', margin: '12px auto' }} />
              )}
              <p className="rp-page-sub" style={{ textAlign: 'center' }}>
                Can’t scan? Enter this code on the phone:
              </p>
              <div className="section-block rp-pair-code-tile" style={{ textAlign: 'center' }}>
                <code className="rp-pair-code">{code}</code>
              </div>
              <p className="rp-auth-hint">Waiting for your phone to join…</p>
              <button className="ghost" type="button" onClick={stop}>Cancel</button>
            </>
          ) : link === 'failed' ? (
            <>
              <div className="banner danger" role="alert">
                Couldn’t connect to <code>{companionName || 'your phone'}</code> over the local network.
                Make sure this computer and the phone are on the <strong>same Wi‑Fi</strong>, then retry.
              </div>
              <button className="primary" type="button" onClick={startRtc}>Retry connection</button>
              <button className="ghost" type="button" onClick={stop}>Unpair</button>
            </>
          ) : link === 'connected' ? (
            <>
              <div className="banner ok" role="status">
                Paired with <code>{companionName || 'phone'}</code> over Wi‑Fi. Dictate from your phone —
                your voice is transcribed here, on-device.
              </div>
              <p className="rp-auth-hint" aria-live="polite">
                {transcribing
                  ? slowTranscribe
                    ? <><span className="rp-mic-live-dot" aria-hidden /> Still transcribing — the speech engine may be loading (the first phrase can take a while)…</>
                    : <><span className="rp-mic-live-dot" aria-hidden /> Transcribing…</>
                  : phoneListening
                    ? <><span className="rp-mic-live-dot" aria-hidden /> Listening — speak into your phone…</>
                    : 'Mic idle — dictate with the phone mic, or use its Type mode for instant text.'}
              </p>
              <button className="ghost" type="button" onClick={stop}>Unpair</button>
            </>
          ) : (
            <>
              <div className="banner" role="status">
                Paired with <code>{companionName || 'phone'}</code>. Connecting to it over Wi‑Fi…
              </div>
              <button className="ghost" type="button" onClick={stop}>Unpair</button>
            </>
          )}
      </div>
    </div>
  );
}
