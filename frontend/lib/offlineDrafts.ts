/**
 * PRD MOB-001..004 — offline draft store for the Capacitor mobile shell and
 * the desktop Tauri shell. Uses `@capacitor/preferences` when available
 * (native), otherwise falls back to `localStorage` for the web preview.
 *
 * Scope: drafts created or edited while the device is offline are buffered
 * locally and replayed against `POST /api/reports` (create) or
 * `PATCH /api/reports/:id` (edit) once the network reports `connected`.
 *
 * This module deliberately depends ONLY on the public `api` surface so the
 * web build does not pull Capacitor types when running in a regular browser.
 */

import { api } from './api';
import { isNativeCapacitorPlatform } from './nativeRuntime';

export type OfflineDraft = {
  /** Local id; replaced with the server id once the create succeeds. */
  localId: string;
  /** Server id once known; null while still pending. */
  serverId: string | null;
  /** Last known status from the API (or 'Draft' for never-synced). */
  status: 'Draft' | 'Validated' | 'Acknowledged' | 'Exported' | string;
  modality: string;
  bodyPart: string;
  accessionNumber: string;
  indication: string;
  technique: string;
  comparison: string;
  findings: string;
  impression: string;
  recommendations: string;
  /** Monotonic edit counter for last-write-wins conflict resolution. */
  rev: number;
  /** Last local edit timestamp (ms since epoch). */
  updatedAt: number;
  /** Whether the draft has unsynced local changes. */
  dirty: boolean;
};

const STORAGE_KEY = 'radiopad.offline.drafts.v1';

type Storage = {
  get(): Promise<OfflineDraft[]>;
  set(value: OfflineDraft[]): Promise<void>;
};

let storage: Storage | null = null;

async function getStorage(): Promise<Storage> {
  if (storage) return storage;
  // PRD DESK-006 — prefer the Tauri-side encrypted store (AES-256-GCM, key
  // held in the OS keyring) when running under the desktop shell. Each
  // draft is stored as a single record keyed by its `localId`.
  try {
    if (typeof window !== 'undefined') {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const tauri: any = (window as any).__TAURI__;
      const invoke: undefined | ((cmd: string, args?: unknown) => Promise<unknown>) =
        tauri?.core?.invoke || tauri?.invoke;
      if (typeof invoke === 'function') {
        storage = {
          async get() {
            const ids = (await invoke('offline_drafts_list')) as string[];
            const out: OfflineDraft[] = [];
            for (const id of ids) {
              const rec = (await invoke('offline_drafts_get', { draftId: id })) as
                | { sections: OfflineDraft }
                | null;
              if (rec?.sections) out.push(rec.sections);
            }
            return out;
          },
          async set(v) {
            const seen = new Set<string>();
            for (const d of v) {
              await invoke('offline_drafts_save', { draftId: d.localId, sections: d });
              seen.add(d.localId);
            }
            const existing = (await invoke('offline_drafts_list')) as string[];
            for (const id of existing) {
              if (!seen.has(id)) {
                await invoke('offline_drafts_delete', { draftId: id });
              }
            }
          },
        };
        return storage;
      }
    }
  } catch {
    /* fall through to Capacitor / localStorage */
  }
  if (isNativeCapacitorPlatform()) {
    try {
      // Capacitor Preferences is dynamic so the web bundle stays small.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const mod: any = await import('@capacitor/preferences').catch(() => null);
      if (mod?.Preferences) {
        storage = {
          async get() {
            const { value } = await mod.Preferences.get({ key: STORAGE_KEY });
            return value ? (JSON.parse(value) as OfflineDraft[]) : [];
          },
          async set(v) {
            await mod.Preferences.set({ key: STORAGE_KEY, value: JSON.stringify(v) });
          },
        };
        return storage;
      }
    } catch {
      /* fall through to localStorage */
    }
  }
  storage = {
    async get() {
      if (typeof localStorage === 'undefined') return [];
      try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]') as OfflineDraft[]; }
      catch { return []; }
    },
    async set(v) {
      if (typeof localStorage === 'undefined') return;
      localStorage.setItem(STORAGE_KEY, JSON.stringify(v));
    },
  };
  return storage;
}

export async function listOfflineDrafts(): Promise<OfflineDraft[]> {
  return (await getStorage()).get();
}

export async function saveOfflineDraft(d: OfflineDraft): Promise<void> {
  const s = await getStorage();
  const all = await s.get();
  const i = all.findIndex((x) => x.localId === d.localId);
  if (i >= 0) all[i] = d;
  else all.push(d);
  await s.set(all);
}

export async function deleteOfflineDraft(localId: string): Promise<void> {
  const s = await getStorage();
  const all = await s.get();
  await s.set(all.filter((x) => x.localId !== localId));
}

/**
 * Replay every dirty draft against the server. Returns the number of drafts
 * successfully synchronised. Failures (network or 4xx) are left in the queue
 * for the next call. Order is preserved by `updatedAt`.
 */
export async function syncOfflineDrafts(): Promise<{ synced: number; failed: number }> {
  const s = await getStorage();
  const all = (await s.get()).slice().sort((a, b) => a.updatedAt - b.updatedAt);
  let synced = 0;
  let failed = 0;
  for (const d of all) {
    if (!d.dirty) continue;
    try {
      let serverId = d.serverId;
      if (!serverId) {
        const created = await api.reports.create({
          modality: d.modality,
          bodyPart: d.bodyPart,
          indication: d.indication,
          accessionNumber: d.accessionNumber,
        });
        serverId = created.id;
      }
      await api.reports.patch(serverId!, {
        indication: d.indication,
        technique: d.technique,
        comparison: d.comparison,
        findings: d.findings,
        impression: d.impression,
        recommendations: d.recommendations,
      });
      d.serverId = serverId!;
      d.dirty = false;
      await saveOfflineDraft(d);
      synced++;
    } catch {
      failed++;
    }
  }
  return { synced, failed };
}

/**
 * Wire <c>@capacitor/network</c> so drafts auto-sync on reconnection.
 * Safe to call multiple times — subsequent calls are no-ops.
 */
let networkWired = false;
export async function startAutoSync(): Promise<void> {
  if (networkWired) return;
  networkWired = true;

  if (typeof window !== 'undefined') {
    window.addEventListener('online', () => {
      void syncOfflineDrafts().catch(() => undefined);
    });
    if (typeof navigator !== 'undefined' && navigator.onLine) {
      void syncOfflineDrafts().catch(() => undefined);
    }
  }

  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const mod: any = await import('@capacitor/network').catch(() => null);
    if (!mod?.Network) return;
    await mod.Network.addListener('networkStatusChange', async (status: { connected: boolean }) => {
      if (status.connected) {
        try { await syncOfflineDrafts(); } catch { /* swallow — retry next event */ }
      }
    });
  } catch {
    /* not running under Capacitor — no-op */
  }
}
