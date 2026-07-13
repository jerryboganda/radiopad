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
import { readQueryParam } from '@/lib/browserParams';
import {
  getLastFocusedSectionEditor,
  getSectionEditor,
  getSectionEditorsInOrder,
} from '@/lib/editor/sectionEditorRegistry';

type Phase = 'idle' | 'advertising' | 'paired' | 'error';
/** State of the direct phone↔desktop LAN audio link (WebRTC). */
type LinkState = 'idle' | 'connecting' | 'connected' | 'failed';

function hostDeviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad desktop';
  if (/mac/i.test(navigator.platform)) return 'Mac desktop';
  if (/win/i.test(navigator.platform)) return 'Windows desktop';
  return 'RadioPad desktop';
}

function focusAdjacentSection(direction: 1 | -1): string | null {
  const editors = getSectionEditorsInOrder();
  if (editors.length === 0) return null;
  const current = getLastFocusedSectionEditor();
  const idx = current ? editors.findIndex((e) => e.sectionKey === current.sectionKey) : -1;
  const nextIdx = idx < 0 ? 0 : (idx + direction + editors.length) % editors.length;
  const target = editors[nextIdx];
  target.focus();
  return target.sectionKey;
}

export default function CompanionHostPanel() {
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<Phase>('idle');
  const [code, setCode] = useState<string>('');
  const [qr, setQr] = useState<string>('');
  const [companionName, setCompanionName] = useState<string>('');
  const [phoneListening, setPhoneListening] = useState(false);
  const [link, setLink] = useState<LinkState>('idle');
  const [transcribing, setTranscribing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const connRef = useRef<CompanionConnection | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const rtcRef = useRef<RtcPeer | null>(null);
  const receiverRef = useRef<AudioReceiver | null>(null);
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
    setLink('idle');
    setTranscribing(false);
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
        let wav: Blob;
        try { wav = await blobToWav16kMono(webm); } catch { wav = webm; }
        const reportId = readQueryParam('id') ?? '';
        const res = await api.reports.transcribe(reportId, wav);
        return formatDictation(res.transcript ?? '');
      },
      insert: (text) => {
        if (!text) return;
        // If the radiologist hasn't clicked into a section yet, don't drop the
        // dictation — land it in Findings (or the first section) by default.
        const target = getLastFocusedSectionEditor()
          ?? getSectionEditor('findings')
          ?? getSectionEditorsInOrder()[0];
        target?.insertAtCursor(text);
      },
      onBusyChange: setTranscribing,
      onError: (m) => setError(m),
    });
    receiverRef.current = receiver;
    setLink('connecting');
    const peer = createRtcPeer({
      role: 'host',
      sendSignal: (s) => connRef.current?.sendSignal(s),
      onSegment: (blob, seq) => receiver.pushSegment(blob, seq),
      onState: (state) => {
        if (state === 'connected') setLink('connected');
        else if (state === 'failed') setLink('failed');
      },
      onFailed: () => setLink('failed'),
    });
    rtcRef.current = peer;
    void peer.startAsHost();
  }, [stopRtc]);

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
            const target = getLastFocusedSectionEditor();
            const targetKey = target?.sectionKey ?? null;
            // If the live preview was anchored in a different section, clear it there
            // first so focus changes never strand a ghost.
            const prevKey = interimTargetRef.current;
            if (prevKey && prevKey !== targetKey) getSectionEditor(prevKey)?.clearInterim?.();
            if (msg.isFinal) {
              // Commit the completed utterance and drop the live preview.
              if (msg.text) target?.insertAtCursor(msg.text);
              target?.clearInterim?.();
              interimTargetRef.current = null;
            } else {
              // Real-time: show the not-yet-final words live at the caret.
              target?.setInterim?.(msg.text);
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
  }, [teardown]);

  return (
    <div className="rp-companion-host">
      <button
        type="button"
        className="ghost"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        title="Pair your phone as a dictation companion"
      >
        📱 Pair phone
      </button>

      {open && (
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
                  ? <><span className="rp-mic-live-dot" aria-hidden /> Transcribing…</>
                  : phoneListening
                    ? <><span className="rp-mic-live-dot" aria-hidden /> Listening — speak into your phone…</>
                    : 'Mic idle — tap the mic on your phone to dictate.'}
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
      )}
    </div>
  );
}
