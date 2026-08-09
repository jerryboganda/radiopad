'use client';

/**
 * Dedicated Device Pairing Management Hub (`/device-pairing`).
 *
 * Lets radiologists pair their mobile phone or tablet as an ultra-low-latency
 * wireless dictation microphone and remote control before opening clinical
 * reports, verify connectivity and speech engine health, test voice input in an
 * interactive sandbox, and manage active companion pairings.
 */

import { useState } from 'react';
import Link from 'next/link';
import { useCompanion } from '@/components/companion/CompanionContext';
import CompanionTestSandbox from '@/components/companion/CompanionTestSandbox';
import {
  Smartphone,
  QrCode,
  Copy,
  Check,
  ShieldCheck,
  Zap,
  Mic,
  RefreshCw,
  Unlink,
  Wifi,
  WifiOff,
  AlertTriangle,
  ChevronRight,
  Sparkles,
  Info,
  PenLine,
  ListTodo,
} from 'lucide-react';

export default function DevicePairingPage() {
  const {
    phase,
    link,
    pairingCode,
    pairingPayload,
    qrDataUrl,
    companionDeviceName,
    phoneListening,
    transcribing,
    slowTranscribe,
    error,
    startPairing,
    unpair,
    retryRtc,
    clearError,
  } = useCompanion();

  const [copied, setCopied] = useState(false);
  const [busyStarting, setBusyStarting] = useState(false);

  const handleStartPairing = async () => {
    setBusyStarting(true);
    try {
      await startPairing();
    } finally {
      setBusyStarting(false);
    }
  };

  const handleCopyLink = async () => {
    if (!pairingPayload) return;
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard) {
        await navigator.clipboard.writeText(pairingPayload);
        setCopied(true);
        setTimeout(() => setCopied(false), 2500);
      }
    } catch {
      /* clipboard write failed */
    }
  };

  return (
    <div className="rp-page rp-device-pairing-page space-y-6 max-w-7xl mx-auto px-4 py-6">
      {/* Page Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b border-border/50 pb-5">
        <div>
          <div className="text-[11px] font-bold uppercase tracking-widest text-accent mb-1 flex items-center gap-1.5">
            <Smartphone size={13} />
            <span>Workspace • Companion Microphone &amp; Remote</span>
          </div>
          <h1 className="text-2xl font-bold text-foreground tracking-tight">Device pairing</h1>
          <p className="text-sm text-muted-foreground mt-1 max-w-3xl">
            Pair your smartphone or tablet as a dedicated dictation microphone and navigation remote.
            Voice audio is streamed directly to this desktop over local Wi‑Fi with zero cloud transmission.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Link
            href="/worklist"
            className="ghost text-xs px-3 py-2 inline-flex items-center gap-1.5 rounded-lg border border-border/60 hover:bg-surface-muted"
          >
            <ListTodo size={14} />
            <span>Worklist</span>
          </Link>
          <Link
            href="/reports/compose"
            className="primary text-xs px-3.5 py-2 inline-flex items-center gap-1.5 rounded-lg shadow-sm"
          >
            <PenLine size={14} />
            <span>Report Composer</span>
          </Link>
        </div>
      </div>

      {/* Main Grid: Left Pairing Controller + Right Info/Specs */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Pairing Controller Card (7 cols on wide screens) */}
        <div className="lg:col-span-7">
          <div className="rp-panel border border-border/70 rounded-xl p-5 shadow-sm bg-surface">
            {error && (
              <div className="banner danger mb-4 flex items-center justify-between" role="alert">
                <div className="flex items-center gap-2 text-xs">
                  <AlertTriangle size={15} />
                  <span>{error}</span>
                </div>
                <button
                  type="button"
                  className="text-xs font-semibold underline hover:opacity-80 ml-2"
                  onClick={clearError}
                >
                  Dismiss
                </button>
              </div>
            )}

            {/* State 1: Idle / Disconnected */}
            {(phase === 'idle' || phase === 'error') && (
              <div className="space-y-5">
                <div className="flex items-start gap-4">
                  <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-accent/10 text-accent border border-accent/20">
                    <Smartphone size={24} />
                  </div>
                  <div>
                    <h2 className="text-base font-semibold text-foreground">No companion device connected</h2>
                    <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed">
                      Pair once, and your mobile device stays active across all reporting workflows,
                      drafts, and worklist queues on this workstation.
                    </p>
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-2">
                  <div className="rounded-lg bg-surface-muted/40 border border-border/50 p-3">
                    <div className="flex items-center gap-1.5 text-xs font-semibold text-foreground mb-1">
                      <ShieldCheck size={14} className="text-emerald-500" />
                      <span>Private &amp; Local</span>
                    </div>
                    <p className="text-[11px] text-muted-foreground leading-snug">
                      Zero cloud audio storage. Audio transcribes directly on-device.
                    </p>
                  </div>

                  <div className="rounded-lg bg-surface-muted/40 border border-border/50 p-3">
                    <div className="flex items-center gap-1.5 text-xs font-semibold text-foreground mb-1">
                      <Zap size={14} className="text-amber-500" />
                      <span>Low Latency</span>
                    </div>
                    <p className="text-[11px] text-muted-foreground leading-snug">
                      P2P WebRTC data channels for instant real-time dictation feedback.
                    </p>
                  </div>

                  <div className="rounded-lg bg-surface-muted/40 border border-border/50 p-3">
                    <div className="flex items-center gap-1.5 text-xs font-semibold text-foreground mb-1">
                      <Mic size={14} className="text-accent" />
                      <span>Remote Control</span>
                    </div>
                    <p className="text-[11px] text-muted-foreground leading-snug">
                      Next/prev section, undo, and AI impression triggers from your phone.
                    </p>
                  </div>
                </div>

                <div className="pt-2 flex items-center gap-3">
                  <button
                    type="button"
                    className="primary px-5 py-2.5 rounded-lg text-xs font-semibold inline-flex items-center gap-2 shadow-sm"
                    onClick={handleStartPairing}
                    disabled={busyStarting}
                  >
                    <QrCode size={15} />
                    <span>{busyStarting ? 'Starting session…' : 'Start pairing'}</span>
                  </button>
                  <span className="text-xs text-muted-foreground">
                    Generates a secure QR code and 6-character short code
                  </span>
                </div>
              </div>
            )}

            {/* State 2: Advertising / Scanning */}
            {phase === 'advertising' && (
              <div className="space-y-5">
                <div className="flex items-center justify-between border-b border-border/40 pb-3">
                  <div className="flex items-center gap-2">
                    <span className="flex h-2.5 w-2.5 rounded-full bg-accent animate-ping" />
                    <h2 className="text-base font-semibold text-foreground">Scan or enter code to pair</h2>
                  </div>
                  <button
                    type="button"
                    className="ghost text-xs px-2.5 py-1 text-muted-foreground hover:text-foreground"
                    onClick={unpair}
                  >
                    Cancel
                  </button>
                </div>

                <div className="flex flex-col sm:flex-row items-center gap-6 py-2">
                  {qrDataUrl ? (
                    <div className="p-3 bg-white rounded-xl shadow-md border border-border/40 shrink-0">
                      <img
                        src={qrDataUrl}
                        alt="Companion pairing QR code"
                        width={180}
                        height={180}
                        className="block rounded-lg"
                      />
                    </div>
                  ) : (
                    <div className="h-[180px] w-[180px] flex items-center justify-center bg-surface-muted rounded-xl border border-dashed text-xs text-muted-foreground">
                      Generating QR…
                    </div>
                  )}

                  <div className="space-y-3 flex-1 text-center sm:text-left">
                    <p className="text-xs text-muted-foreground leading-relaxed">
                      1. Open <strong>RadioPad Companion</strong> on your mobile device.<br />
                      2. Point your camera at this QR code to authenticate and pair instantly.
                    </p>

                    <div className="pt-1">
                      <div className="text-[11px] font-bold uppercase tracking-wider text-muted-foreground mb-1">
                        Manual pairing code
                      </div>
                      <div className="inline-flex items-center gap-2 bg-surface-muted px-3 py-1.5 rounded-lg border border-border">
                        <code className="text-base font-mono font-bold tracking-widest text-accent">
                          {pairingCode || '------'}
                        </code>
                      </div>
                    </div>

                    <div className="pt-1 flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        className="ghost text-xs px-3 py-1.5 inline-flex items-center gap-1.5 rounded-lg border border-border/60 hover:bg-surface-muted"
                        onClick={handleCopyLink}
                      >
                        {copied ? <Check size={13} className="text-emerald-500" /> : <Copy size={13} />}
                        <span>{copied ? 'Link copied!' : 'Copy pairing link'}</span>
                      </button>
                      <span className="text-[11px] text-muted-foreground">
                        (Use if phone camera is unavailable)
                      </span>
                    </div>
                  </div>
                </div>

                <div className="rounded-lg bg-surface-muted/30 border border-border/40 p-3 text-xs text-muted-foreground flex items-center gap-2">
                  <span className="flex h-2 w-2 rounded-full bg-accent animate-pulse shrink-0" />
                  <span>Waiting for your mobile device to join session…</span>
                </div>
              </div>
            )}

            {/* State 3: Paired & Active */}
            {phase === 'paired' && (
              <div className="space-y-5">
                <div className="flex items-center justify-between border-b border-border/40 pb-3">
                  <div className="flex items-center gap-2.5">
                    <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                      <Smartphone size={18} />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <h2 className="text-base font-semibold text-foreground">
                          {companionDeviceName || 'Paired Mobile Device'}
                        </h2>
                        <span className="inline-flex items-center gap-1 text-[10px] font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded-full">
                          Connected
                        </span>
                      </div>
                      <span className="text-xs text-muted-foreground">
                        Ready for medical dictation across all reports
                      </span>
                    </div>
                  </div>

                  <button
                    type="button"
                    className="ghost text-xs px-2.5 py-1.5 text-rose-600 dark:text-rose-400 hover:bg-rose-500/10 rounded-lg inline-flex items-center gap-1.5"
                    onClick={unpair}
                  >
                    <Unlink size={13} />
                    <span>Unpair</span>
                  </button>
                </div>

                {/* Connection Health & Mode */}
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <div className="rounded-lg bg-surface-muted/50 border border-border/60 p-3">
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-xs font-semibold text-foreground">Transport mode</span>
                      {link === 'connected' ? (
                        <span className="flex items-center gap-1 text-[11px] text-emerald-600 dark:text-emerald-400 font-medium">
                          <Wifi size={13} /> Direct LAN WebRTC
                        </span>
                      ) : link === 'connecting' ? (
                        <span className="flex items-center gap-1 text-[11px] text-amber-500 font-medium">
                          <RefreshCw size={13} className="animate-spin" /> Connecting LAN…
                        </span>
                      ) : (
                        <span className="flex items-center gap-1 text-[11px] text-muted-foreground font-medium">
                          <WifiOff size={13} /> Cloud relay fallback
                        </span>
                      )}
                    </div>
                    <p className="text-[11px] text-muted-foreground leading-snug">
                      {link === 'connected'
                        ? 'Audio streams peer-to-peer over your local network for minimal latency.'
                        : 'WebRTC P2P direct link could not be negotiated; audio relays via cloud proxy.'}
                    </p>
                    {link === 'failed' && (
                      <button
                        type="button"
                        className="mt-2 text-xs text-accent hover:underline inline-flex items-center gap-1"
                        onClick={retryRtc}
                      >
                        <RefreshCw size={12} /> Retry direct LAN link
                      </button>
                    )}
                  </div>

                  <div className="rounded-lg bg-surface-muted/50 border border-border/60 p-3">
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-xs font-semibold text-foreground">Speech engine</span>
                      <span className="text-[11px] text-emerald-600 dark:text-emerald-400 font-medium flex items-center gap-1">
                        <Check size={13} /> On-device active
                      </span>
                    </div>
                    <p className="text-[11px] text-muted-foreground leading-snug">
                      Speech audio is transcribed by the local speech sidecar on this machine.
                    </p>
                    <Link
                      href="/settings/models"
                      className="mt-2 text-xs text-accent hover:underline inline-flex items-center gap-1"
                    >
                      <span>Manage speech models</span>
                      <ChevronRight size={12} />
                    </Link>
                  </div>
                </div>

                {/* Live Mic Status Notice */}
                <div className="rounded-lg bg-emerald-500/5 border border-emerald-500/20 p-3 flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <span
                      className={`flex h-3 w-3 rounded-full ${
                        phoneListening
                          ? 'bg-rose-500 animate-ping'
                          : transcribing
                          ? 'bg-amber-500 animate-pulse'
                          : 'bg-emerald-500'
                      }`}
                    />
                    <div className="text-xs">
                      <span className="font-semibold text-foreground">
                        {phoneListening
                          ? 'Mic is LIVE (recording from phone)…'
                          : transcribing
                          ? slowTranscribe
                            ? 'Transcribing (engine cold-load)…'
                            : 'Transcribing phrase…'
                          : 'Microphone is standby & ready.'}
                      </span>
                      <span className="text-muted-foreground ml-1.5">
                        Hold the microphone button on your phone to dictate.
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Quick Instructions & Clinical Tips (5 cols on wide screens) */}
        <div className="lg:col-span-5 space-y-4">
          <div className="rp-panel border border-border/70 rounded-xl p-5 shadow-sm bg-surface space-y-3">
            <h3 className="text-xs font-bold uppercase tracking-wider text-muted-foreground flex items-center gap-1.5">
              <Info size={14} className="text-accent" />
              <span>How Companion Pairing Works</span>
            </h3>

            <ul className="text-xs text-muted-foreground space-y-2.5 leading-relaxed">
              <li className="flex items-start gap-2">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent/10 text-accent font-semibold text-[10px]">
                  1
                </span>
                <span>
                  <strong>Pair once:</strong> Scan the QR code with your phone. You remain paired across all reports and worklist navigations.
                </span>
              </li>
              <li className="flex items-start gap-2">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent/10 text-accent font-semibold text-[10px]">
                  2
                </span>
                <span>
                  <strong>Push to talk:</strong> Press and hold the phone microphone while speaking. Audio streams in real-time.
                </span>
              </li>
              <li className="flex items-start gap-2">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent/10 text-accent font-semibold text-[10px]">
                  3
                </span>
                <span>
                  <strong>Target auto-routing:</strong> Transcribed text lands directly into whichever section is active on your screen.
                </span>
              </li>
              <li className="flex items-start gap-2">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent/10 text-accent font-semibold text-[10px]">
                  4
                </span>
                <span>
                  <strong>Remote commands:</strong> Use phone buttons or voice phrases like <em>“Next section”</em> or <em>“Jump impression”</em>.
                </span>
              </li>
            </ul>
          </div>

          <div className="rounded-xl border border-border/60 bg-surface-muted/30 p-4 space-y-2">
            <div className="flex items-center gap-2 text-xs font-semibold text-foreground">
              <Sparkles size={14} className="text-accent" />
              <span>Network Recommendations</span>
            </div>
            <p className="text-[11px] text-muted-foreground leading-relaxed">
              For sub-50ms peer-to-peer audio streaming, ensure both this workstation and your phone are connected to the same clinical or local Wi‑Fi subnet.
            </p>
          </div>
        </div>
      </div>

      {/* Pre-Reporting Live Dictation Test Sandbox */}
      <div className="pt-2">
        <CompanionTestSandbox />
      </div>
    </div>
  );
}
