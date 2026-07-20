'use client';

// Per-user default AI model (provider) preference — the radiologist's own
// choice of engine (cloud provider, UBAG, or a local model), not an admin
// setting. Mirrors the STT-mode preference pattern (lib/dictation/sttMode.ts):
// persisted in localStorage, kept authoritative in memory (Tauri webview
// storage writes can silently fail), and broadcast via a custom event plus the
// native `storage` event so every picker stays in sync.

import { useCallback, useEffect, useState } from 'react';
import type { Provider } from '@/lib/api';

const KEY = 'radiopad:preferred-provider';
const EVENT = 'radiopad:preferred-provider-changed';

function readStorage(): string {
  try {
    return window.localStorage.getItem(KEY) ?? '';
  } catch {
    return '';
  }
}

// In-memory source of truth; '' means "no personal default — use the
// workspace's highest-priority enabled provider".
let memId: string = typeof window === 'undefined' ? '' : readStorage();

export function getPreferredProviderId(): string {
  return memId;
}

export function setPreferredProviderId(id: string): void {
  memId = id;
  if (typeof window === 'undefined') return;
  try {
    if (id) window.localStorage.setItem(KEY, id);
    else window.localStorage.removeItem(KEY);
  } catch {
    /* storage unavailable — the in-memory choice above still applies */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
}

function syncFromStorage(): void {
  if (typeof window === 'undefined') return;
  memId = readStorage();
}

/**
 * Resolve the provider a fresh surface should start on: the radiologist's
 * saved default when it is still an enabled provider in this workspace,
 * otherwise the highest-priority enabled provider, otherwise the first row.
 */
export function resolveDefaultProvider(providers: Provider[]): Provider | undefined {
  const preferred = providers.find((p) => p.id === getPreferredProviderId() && p.enabled);
  if (preferred) return preferred;
  const enabled = providers.filter((p) => p.enabled);
  return [...enabled].sort((a, b) => (a.priority ?? 999) - (b.priority ?? 999))[0] ?? providers[0];
}

/** React hook — re-renders when any picker (or another window) changes the default. */
export function usePreferredProviderId(): [string, (id: string) => void] {
  const [id, setId] = useState('');
  useEffect(() => {
    setId(getPreferredProviderId());
    const onChange = () => setId(getPreferredProviderId());
    const onStorage = () => {
      syncFromStorage();
      setId(getPreferredProviderId());
    };
    window.addEventListener(EVENT, onChange);
    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener('storage', onStorage);
    };
  }, []);
  return [id, useCallback((v: string) => setPreferredProviderId(v), [])];
}
