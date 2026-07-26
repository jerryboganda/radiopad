'use client';

// Device-local Report Templates preferences: designer zoom level and the last
// export-layout choice. Neither is server-persisted (per-user server state is
// `ReportLayoutUserDefault` — see api.reportLayouts.setDefault); these are purely
// ergonomic, device-local conveniences. Mirrors the in-memory-first idiom in
// lib/dictation/crossCheckPrefs.ts: localStorage is best-effort (some webview
// origins silently fail to persist or read back writes), so a live in-memory
// value is the source of truth and storage is just a best-effort sync.

import { useCallback, useEffect, useState } from 'react';

const ZOOM_KEY = 'radiopad:report-layout-zoom';
const LAST_EXPORT_KEY = 'radiopad:report-layout-last-export';
const EVENT = 'radiopad:report-layout-prefs-changed';

const DEFAULT_ZOOM = 100;

const mem = new Map<string, string>();

function readStorage(key: string): string | null {
  if (typeof window === 'undefined') return null;
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function read(key: string, fallback: string): string {
  const cached = mem.get(key);
  if (cached !== undefined) return cached;
  const initial = readStorage(key) ?? fallback;
  mem.set(key, initial);
  return initial;
}

function write(key: string, value: string): void {
  mem.set(key, value);
  if (typeof window !== 'undefined') {
    try {
      window.localStorage.setItem(key, value);
    } catch {
      /* storage unavailable — the in-memory value above still applies */
    }
    window.dispatchEvent(new CustomEvent(EVENT));
  }
}

export function getDesignerZoom(): number {
  const n = Number(read(ZOOM_KEY, String(DEFAULT_ZOOM)));
  return Number.isFinite(n) && n >= 50 && n <= 150 ? n : DEFAULT_ZOOM;
}

export function setDesignerZoom(zoom: number): void {
  write(ZOOM_KEY, String(Math.round(Math.max(50, Math.min(150, zoom)))));
}

/** `null` = no remembered choice; `"classic"` or a layout id otherwise. */
export function getLastExportLayoutId(): string | null {
  const v = read(LAST_EXPORT_KEY, '');
  return v.length > 0 ? v : null;
}

export function setLastExportLayoutId(layoutId: string | null): void {
  write(LAST_EXPORT_KEY, layoutId ?? '');
}

export function useDesignerZoom(): [number, (zoom: number) => void] {
  const [zoom, setZoom] = useState(DEFAULT_ZOOM);
  useEffect(() => {
    setZoom(getDesignerZoom());
    const onChange = () => setZoom(getDesignerZoom());
    window.addEventListener(EVENT, onChange);
    window.addEventListener('storage', onChange);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener('storage', onChange);
    };
  }, []);
  return [zoom, useCallback((z: number) => setDesignerZoom(z), [])];
}
