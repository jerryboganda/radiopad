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

function read(key: string, defaultOn = false): boolean {
  if (typeof window === 'undefined') return defaultOn;
  try {
    const v = window.localStorage.getItem(key);
    if (v === null) return defaultOn;
    return v === '1';
  } catch {
    return defaultOn;
  }
}

function write(key: string, on: boolean): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(key, on ? '1' : '0');
  } catch {
    /* storage unavailable — ignore */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
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
    const sync = () => setOn(read(key, defaultOn));
    sync();
    window.addEventListener(EVENT, sync);
    window.addEventListener('storage', sync);
    return () => {
      window.removeEventListener(EVENT, sync);
      window.removeEventListener('storage', sync);
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
