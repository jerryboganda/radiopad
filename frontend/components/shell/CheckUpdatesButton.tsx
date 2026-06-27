'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Download, RefreshCw, CheckCircle2, AlertCircle } from 'lucide-react';

/**
 * DESK-001 — one-click desktop self-update.
 *
 * Renders only inside the Tauri desktop shell (web/mobile never self-update).
 * On mount it runs a silent `check()` so a waiting update surfaces as an accent
 * dot on the button. Clicking the button runs the full hands-off flow:
 * check → download (with live %) → install (signature-verified by Tauri) →
 * relaunch into the new build. No file pickers, no browser, no manual steps.
 *
 * Mirrors the Tauri-presence guard in `app/ShellBridge.tsx` and the 36×36
 * top-bar icon-button styling used by `Topbar`/`ProfileMenu`.
 */

type Phase =
  | 'idle'
  | 'checking'
  | 'available'
  | 'downloading'
  | 'installing'
  | 'uptodate'
  | 'error';

function hasTauri(): boolean {
  return typeof window !== 'undefined' && '__TAURI__' in window;
}

/**
 * Tauri-presence gate. Renders nothing outside the desktop shell. Keeping the
 * i18n + updater body in an inner component that only mounts under Tauri means
 * `useTranslations` is never called on web/mobile — or in tests that render the
 * shell without a `NextIntlClientProvider` — since the update button is
 * desktop-only anyway. (Calling `useTranslations` at the top of the old single
 * component threw "context from NextIntlClientProvider was not found" the moment
 * any non-desktop tree mounted it, even though it ultimately rendered null.)
 */
export default function CheckUpdatesButton() {
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(hasTauri());
  }, []);

  if (!mounted) return null;
  return <UpdateButton />;
}

function UpdateButton() {
  const t = useTranslations('topbar.update');
  const [phase, setPhase] = useState<Phase>('idle');
  const [pct, setPct] = useState(0);
  const busyRef = useRef(false);

  // Silent check-on-launch — surfaces the badge without interrupting the user.
  // This inner component only mounts under Tauri, so the check always runs.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const { check } = await import('@tauri-apps/plugin-updater');
        const update = await check();
        if (!cancelled && update) setPhase('available');
      } catch {
        /* offline or not under Tauri — stay idle, no nagging */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Transient states (up-to-date / error) fall back to idle after a beat.
  const revertSoon = useCallback(() => {
    window.setTimeout(() => {
      setPhase((p) => (p === 'uptodate' || p === 'error' ? 'idle' : p));
    }, 4000);
  }, []);

  const run = useCallback(async () => {
    if (busyRef.current) return;
    busyRef.current = true;
    setPct(0);
    try {
      const { check } = await import('@tauri-apps/plugin-updater');
      const { relaunch } = await import('@tauri-apps/plugin-process');

      setPhase('checking');
      const update = await check();
      if (!update) {
        setPhase('uptodate');
        busyRef.current = false;
        revertSoon();
        return;
      }

      setPhase('downloading');
      let total = 0;
      let got = 0;
      await update.downloadAndInstall((e) => {
        if (e.event === 'Started') {
          total = e.data.contentLength ?? 0;
        } else if (e.event === 'Progress') {
          got += e.data.chunkLength ?? 0;
          if (total > 0) setPct(Math.min(100, Math.round((got / total) * 100)));
        } else if (e.event === 'Finished') {
          setPct(100);
          setPhase('installing');
        }
      });

      // Installer has run; restart into the new version.
      setPhase('installing');
      await relaunch();
    } catch {
      setPhase('error');
      busyRef.current = false;
      revertSoon();
    }
  }, [revertSoon]);

  const busy = phase === 'checking' || phase === 'downloading' || phase === 'installing';
  const label =
    phase === 'checking'
      ? t('checking')
      : phase === 'downloading'
        ? t('downloading', { pct })
        : phase === 'installing'
          ? t('installing')
          : phase === 'uptodate'
            ? t('upToDate')
            : phase === 'available'
              ? t('available')
              : phase === 'error'
                ? t('failed')
                : t('checkForUpdates');

  const Icon =
    phase === 'uptodate'
      ? CheckCircle2
      : phase === 'error'
        ? AlertCircle
        : phase === 'available'
          ? Download
          : RefreshCw;

  return (
    <div className={`rp-update is-${phase}`}>
      {phase !== 'idle' && <span className="rp-update-label">{label}</span>}
      <button
        type="button"
        className="rp-update-btn"
        aria-label={label}
        title={label}
        onClick={run}
        disabled={busy}
        data-phase={phase}
      >
        <Icon
          size={17}
          className={busy ? 'rp-spin' : undefined}
          aria-hidden
        />
        {phase === 'available' && <span className="rp-update-dot" aria-hidden />}
      </button>
    </div>
  );
}
