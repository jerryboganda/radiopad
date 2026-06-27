'use client';

// Shared on-device STT engine-mode preference, synced across the dictation
// overlay's quick picker and the profile-menu "Dual-engine cross-check" toggle.
// Persisted in localStorage and broadcast via a custom event (and the native
// `storage` event for other tabs/windows) so every control stays consistent.

import { useCallback, useEffect, useState } from 'react';
import type { SttMode } from '@/lib/api';

export const STT_MODES: SttMode[] = ['auto', 'single', 'ensemble'];
const KEY = 'radiopad:stt-mode';
const EVENT = 'radiopad:stt-mode-changed';

function normalize(v: string | null | undefined): SttMode {
  return v === 'single' || v === 'ensemble' ? v : 'auto';
}

function readStorage(): SttMode {
  try {
    return normalize(window.localStorage.getItem(KEY));
  } catch {
    return 'auto';
  }
}

// In-memory source of truth. localStorage is best-effort persistence: in some
// webview origins (e.g. Tauri's custom protocol) writes can silently fail or not
// reflect back on read. If we re-read storage after a toggle in those contexts,
// the controlled checkbox snaps back to its old value and feels "unresponsive".
// Keeping the live value in memory makes toggles always stick; storage just
// persists across reloads and syncs across tabs when it is available.
let memMode: SttMode = typeof window === 'undefined' ? 'auto' : readStorage();

export function getSttMode(): SttMode {
  return memMode;
}

export function setSttMode(mode: SttMode): void {
  memMode = mode;
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(KEY, mode);
  } catch {
    /* storage unavailable — the in-memory choice above still applies */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
}

/** Re-sync the in-memory value from storage (for cross-tab `storage` events). */
function syncFromStorage(): void {
  if (typeof window === 'undefined') return;
  memMode = readStorage();
}

/** True when the dual-engine cross-check (ensemble) is the active mode. */
export function isDualCheckOn(mode: SttMode): boolean {
  return mode === 'ensemble';
}

/**
 * React hook for the shared STT mode. Re-renders when any control (or another
 * tab) changes it, so the overlay picker and the profile-menu toggle agree.
 */
export function useSttMode(): [SttMode, (mode: SttMode) => void] {
  const [mode, setMode] = useState<SttMode>('auto');
  useEffect(() => {
    setMode(getSttMode());
    const onChange = () => setMode(getSttMode());
    const onStorage = () => {
      syncFromStorage();
      setMode(getSttMode());
    };
    window.addEventListener(EVENT, onChange);
    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener('storage', onStorage);
    };
  }, []);
  return [mode, useCallback((m: SttMode) => setSttMode(m), [])];
}
