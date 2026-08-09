'use client';

/**
 * Desktop companion host panel. Shown when toggled from the composer ribbon
 * inside an open report.
 *
 * Consumes the global CompanionContext so companion pairing and direct LAN
 * audio persist across all page transitions and reports seamlessly.
 */

import Link from 'next/link';
import { useCompanion } from './CompanionContext';
import { ExternalLink, Smartphone } from 'lucide-react';

export default function CompanionHostPanel({ open }: { open: boolean }) {
  const {
    phase,
    link,
    pairingCode,
    qrDataUrl,
    companionDeviceName,
    phoneListening,
    transcribing,
    slowTranscribe,
    error,
    startPairing,
    unpair,
    retryRtc,
  } = useCompanion();

  if (!open) return null;

  return (
    <div className="rp-companion-host">
      <div className="rp-panel rp-companion-host-panel" role="dialog" aria-label="Phone companion">
        <div className="flex items-center justify-between border-b border-border/40 pb-2 mb-3">
          <div className="flex items-center gap-2 text-xs font-semibold text-foreground">
            <Smartphone size={14} className="text-accent" />
            <span>Companion Microphone</span>
          </div>
          <Link
            href="/device-pairing"
            className="text-[11px] text-accent hover:underline inline-flex items-center gap-1"
          >
            <span>Device pairing hub</span>
            <ExternalLink size={11} />
          </Link>
        </div>

        {error && <div className="banner danger" role="alert">{error}</div>}

        {phase === 'idle' || phase === 'error' ? (
          <>
            <p className="rp-page-sub">
              Pair your phone to dictate into this report wirelessly. It becomes a microphone and
              remote — nothing is edited or signed on the phone.
            </p>
            <div className="flex items-center gap-2">
              <button className="primary" type="button" onClick={() => void startPairing()}>
                Start pairing
              </button>
              <Link href="/device-pairing" className="ghost text-xs px-2.5 py-1.5 rounded border">
                Open pairing hub
              </Link>
            </div>
          </>
        ) : phase === 'advertising' ? (
          <>
            <p className="rp-page-sub">
              Open the RadioPad phone app and <strong>scan this QR</strong> to pair — no phone sign-in needed:
            </p>
            {qrDataUrl && (
              <img
                src={qrDataUrl}
                alt="Pairing QR code"
                width={200}
                height={200}
                style={{ display: 'block', margin: '12px auto' }}
              />
            )}
            <p className="rp-page-sub" style={{ textAlign: 'center' }}>
              Can’t scan? Enter this code on the phone:
            </p>
            <div className="section-block rp-pair-code-tile" style={{ textAlign: 'center' }}>
              <code className="rp-pair-code">{pairingCode || '------'}</code>
            </div>
            <p className="rp-auth-hint">Waiting for your phone to join…</p>
            <button className="ghost" type="button" onClick={() => void unpair()}>
              Cancel
            </button>
          </>
        ) : link === 'failed' ? (
          <>
            <div className="banner danger" role="alert">
              Couldn’t connect to <code>{companionDeviceName || 'your phone'}</code> over the local network.
              Make sure this computer and the phone are on the <strong>same Wi‑Fi</strong>, then retry.
            </div>
            <button className="primary" type="button" onClick={retryRtc}>
              Retry connection
            </button>
            <button className="ghost" type="button" onClick={() => void unpair()}>
              Unpair
            </button>
          </>
        ) : link === 'connected' ? (
          <>
            <div className="banner ok" role="status">
              Paired with <code>{companionDeviceName || 'phone'}</code> over Wi‑Fi. Dictate from your phone —
              your voice is transcribed here, on-device.
            </div>
            <p className="rp-auth-hint" aria-live="polite">
              {transcribing ? (
                slowTranscribe ? (
                  <>
                    <span className="rp-mic-live-dot" aria-hidden /> Still transcribing — the speech engine may be loading (the first phrase can take a while)…
                  </>
                ) : (
                  <>
                    <span className="rp-mic-live-dot" aria-hidden /> Transcribing…
                  </>
                )
              ) : phoneListening ? (
                <>
                  <span className="rp-mic-live-dot" aria-hidden /> Listening — speak into your phone…
                </>
              ) : (
                'Mic idle — dictate with the phone mic, or use its Type mode for instant text.'
              )}
            </p>
            <button className="ghost" type="button" onClick={() => void unpair()}>
              Unpair
            </button>
          </>
        ) : (
          <>
            <div className="banner" role="status">
              Paired with <code>{companionDeviceName || 'phone'}</code>. Connecting to it over Wi‑Fi…
            </div>
            <button className="ghost" type="button" onClick={() => void unpair()}>
              Unpair
            </button>
          </>
        )}
      </div>
    </div>
  );
}
