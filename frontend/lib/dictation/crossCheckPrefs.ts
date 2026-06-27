'use client';

// User preferences for the manual Cross Check, mirroring the sttMode pattern
// (localStorage + CustomEvent + cross-tab `storage`). Both default OFF:
//  - crossCheckEnabled: show the "Cross Check" button + retain audio.
//  - useUbag: also route the LLM medical review through the cloud UBAG gateway.
// UBAG ships text to cloud web-AI, so it stays opt-in behind a no-PHI confirm.

import { useCallback, useEffect, useState } from 'react';

const ENABLED_KEY = 'radiopad:crosscheck-enabled';
const UBAG_KEY = 'radiopad:crosscheck-ubag';
const EVENT = 'radiopad:crosscheck-prefs-changed';

function readStorage(key: string, defaultOn: boolean): boolean {
  if (typeof window === 'undefined') return defaultOn;
  try {
    const v = window.localStorage.getItem(key);
    if (v === null) return defaultOn;
    return v === '1';
  } catch {
    return defaultOn;
  }
}

// In-memory source of truth keyed by storage key. localStorage is best-effort:
// in some webview origins (e.g. Tauri's custom protocol) writes can silently
// fail or not reflect back on read, which would make a controlled checkbox snap
// back to its old value after a click and feel "unresponsive". Holding the live
// value in memory makes every toggle stick; storage only persists/syncs it when
// available. Seeded lazily from storage (or the supplied default) on first use.
const mem = new Map<string, boolean>();

function read(key: string, defaultOn = false): boolean {
  const cached = mem.get(key);
  if (cached !== undefined) return cached;
  const initial = readStorage(key, defaultOn);
  mem.set(key, initial);
  return initial;
}

function write(key: string, on: boolean): void {
  mem.set(key, on);
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(key, on ? '1' : '0');
  } catch {
    /* storage unavailable — the in-memory value above still applies */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
}

/** Re-sync an in-memory value from storage (for cross-tab `storage` events). */
function syncFromStorage(key: string, defaultOn: boolean): void {
  mem.set(key, readStorage(key, defaultOn));
}

// Cross Check ships ON by default (opt-out); UBAG stays opt-in (cloud + PHI risk).
export const isCrossCheckEnabled = () => read(ENABLED_KEY, true);
export const isUseUbagEnabled = () => read(UBAG_KEY, false);
export const setCrossCheckEnabled = (on: boolean) => write(ENABLED_KEY, on);
export const setUseUbag = (on: boolean) => write(UBAG_KEY, on);

function usePref(
  key: string,
  setter: (on: boolean) => void,
  defaultOn: boolean,
): [boolean, (on: boolean) => void] {
  const [on, setOn] = useState(defaultOn);
  useEffect(() => {
    setOn(read(key, defaultOn));
    const onChange = () => setOn(read(key, defaultOn));
    const onStorage = () => {
      syncFromStorage(key, defaultOn);
      setOn(read(key, defaultOn));
    };
    window.addEventListener(EVENT, onChange);
    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener('storage', onStorage);
    };
  }, [key, defaultOn]);
  return [on, useCallback((v: boolean) => setter(v), [setter])];
}

export function useCrossCheckEnabled(): [boolean, (on: boolean) => void] {
  return usePref(ENABLED_KEY, setCrossCheckEnabled, true);
}

export function useUseUbag(): [boolean, (on: boolean) => void] {
  return usePref(UBAG_KEY, setUseUbag, false);
}
