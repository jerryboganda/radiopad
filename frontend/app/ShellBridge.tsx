'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { startAutoSync } from '@/lib/offlineDrafts';
import { getAuthToken, setAuthToken } from '@/lib/secureAuth';
import { setActiveAuthToken } from '@/lib/api';
import { isBiometricLockEnabled, unlockWithBiometric } from '@/lib/biometric';

type TauriUnlisten = () => void;
type TauriListenHandle = TauriUnlisten | { unlisten?: TauriUnlisten } | null | undefined;

type TauriGlobal = {
  core?: { invoke?: (cmd: string, args?: unknown) => Promise<unknown> };
  event?: { listen?: (event: string, handler: () => void) => Promise<TauriListenHandle> | TauriListenHandle };
  invoke?: (cmd: string, args?: unknown) => Promise<unknown>;
  listen?: (event: string, handler: () => void) => Promise<TauriListenHandle> | TauriListenHandle;
};

function getTauri(): TauriGlobal | null {
  if (typeof window === 'undefined') return null;
  return (window as typeof window & { __TAURI__?: TauriGlobal }).__TAURI__ ?? null;
}

async function invokeTauri(cmd: string, args?: unknown): Promise<unknown> {
  const tauri = getTauri();
  const invoke = tauri?.core?.invoke ?? tauri?.invoke;
  if (!invoke) return null;
  return invoke(cmd, args);
}

/**
 * Bridges native shells (Tauri desktop, Capacitor mobile) to the Next.js app.
 *
 * Desktop (Tauri): listens for `radiopad://new-report` emitted by the global
 * `Ctrl/Cmd+Shift+N` shortcut and routes to a fresh report editor.
 *
 * Mobile (Capacitor): registers a network listener that flushes the offline
 * draft queue whenever connectivity returns.
 *
 * Both bindings are best-effort. When the runtime is a regular browser the
 * dynamic imports fail silently and this component is a no-op.
 */
export default function ShellBridge() {
  const router = useRouter();

  useEffect(() => {
    let unsub: Array<() => void> = [];
    let cancelled = false;
    (async () => {
      try {
        const tauri = getTauri();
        const listen = tauri?.event?.listen ?? tauri?.listen;
        if (!listen || cancelled) return;
        // PRD DESK-003 — global hotkeys are registered Rust-side in
        // `desktop/src-tauri/src/main.rs`. The shell forwards the user
        // intent as `radiopad://*` events; this bridge translates each
        // event into a navigation or DOM-level action.
        const dispatchAction = (name: string) => {
          if (typeof window === 'undefined') return;
          window.dispatchEvent(new CustomEvent(`radiopad:${name}`));
        };
        const reg = async (event: string, handler: () => void) => {
          const handle = await listen(event, handler);
          const u = typeof handle === 'function'
            ? handle
            : handle?.unlisten;
          if (typeof u === 'function') unsub.push(u);
        };
        await reg('radiopad://new-report', () => router.push('/?new=1'));
        await reg('radiopad://generate-impression', () =>
          dispatchAction('generate-impression'),
        );
        await reg('radiopad://rewrite', () => dispatchAction('rewrite'));
        await reg('radiopad://dictate', () => dispatchAction('dictate'));
        await reg('radiopad://secure-copy-section', () =>
          dispatchAction('secure-copy-section'),
        );
        await reg('radiopad://clipboard-cleared', () =>
          dispatchAction('clipboard-cleared'),
        );
      } catch {
        /* not running under Tauri — no-op */
      }
    })();
    // Mobile/desktop offline-draft auto sync.
    startAutoSync().catch(() => { /* best effort */ });
    // Hydrate the bearer from the OS-level secure store (Keychain /
    // Keystore on native; Preferences/localStorage in the dev preview).
    // PRD MOB-008 — when the user has enabled biometric lock, gate the token
    // release on a Face ID / Touch ID / Android biometric prompt.
    (async () => {
      try {
        if (await isBiometricLockEnabled()) {
          const ok = await unlockWithBiometric('Unlock RadioPad to continue');
          if (!ok) { setActiveAuthToken(null); return; }
        }
        let tok = await getAuthToken();
        if (!tok) {
          const desktopToken = await invokeTauri('device_pairing_token_get');
          if (typeof desktopToken === 'string' && desktopToken.length > 0) {
            tok = desktopToken;
            await setAuthToken(desktopToken);
          }
        }
        if (!cancelled) setActiveAuthToken(tok ?? null);
      } catch {
        /* best effort */
      }
    })();
    return () => {
      cancelled = true;
      for (const u of unsub) {
        try { u(); } catch { /* ignore */ }
      }
      unsub = [];
    };
  }, [router]);

  return null;
}
