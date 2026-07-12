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
import {
  getLastFocusedSectionEditor,
  getSectionEditorsInOrder,
} from '@/lib/editor/sectionEditorRegistry';

type Phase = 'idle' | 'advertising' | 'paired' | 'error';

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
  const [error, setError] = useState<string | null>(null);
  const connRef = useRef<CompanionConnection | null>(null);
  const sessionIdRef = useRef<string | null>(null);

  const sendSectionContext = useCallback(() => {
    const current = getLastFocusedSectionEditor();
    if (current) connRef.current?.sendSectionContext(current.sectionKey, current.sectionKey);
  }, []);

  const handleCommand = useCallback((command: CompanionCommand) => {
    if (command === 'next_section') focusAdjacentSection(1);
    else if (command === 'prev_section') focusAdjacentSection(-1);
    // insert/undo/read_back/ptt_* are advisory here — dictation is already
    // inserted on final results; these keep the phone UI responsive.
    sendSectionContext();
  }, [sendSectionContext]);

  const teardown = useCallback(() => {
    connRef.current?.close();
    connRef.current = null;
    const id = sessionIdRef.current;
    sessionIdRef.current = null;
    if (id) void api.companion.endSession(id).catch(() => undefined);
  }, []);

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
            // Only commit final results to the report; interim text is noise.
            if (msg.isFinal && msg.text) {
              getLastFocusedSectionEditor()?.insertAtCursor(msg.text);
            }
          } else if (msg.type === 'command') {
            handleCommand(msg.command);
          } else if (msg.type === 'peer_joined') {
            setCompanionName(msg.deviceName || 'phone');
            setPhase('paired');
            sendSectionContext();
          } else if (msg.type === 'peer_left') {
            setPhase('advertising');
            setCompanionName('');
          } else if (msg.type === 'session_ended') {
            setPhase('idle');
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
  }, [handleCommand, sendSectionContext]);

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
          ) : (
            <>
              <div className="banner ok" role="status">
                Paired with <code>{companionName || 'phone'}</code>. Dictate from your phone — text
                lands in the focused section.
              </div>
              <button className="ghost" type="button" onClick={stop}>Unpair</button>
            </>
          )}
        </div>
      )}
    </div>
  );
}
