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

export function getSttMode(): SttMode {
  if (typeof window === 'undefined') return 'auto';
  const v = window.localStorage.getItem(KEY);
  return v === 'single' || v === 'ensemble' ? v : 'auto';
}

export function setSttMode(mode: SttMode): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(KEY, mode);
  } catch {
    /* storage unavailable — keep the in-memory choice */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
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
    window.addEventListener(EVENT, onChange);
    window.addEventListener('storage', onChange);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener('storage', onChange);
    };
  }, []);
  return [mode, useCallback((m: SttMode) => setSttMode(m), [])];
}
