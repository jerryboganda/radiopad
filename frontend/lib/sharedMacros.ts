'use client';

// PRD RPT-021 — tenant / subspecialty macros, the shared half of autotext.
//
// Personal snippets (lib/snippets.ts) stay device-local and always WIN on a
// trigger collision: a radiologist must be able to override a departmental
// macro without asking an admin. Shared macros are fetched once per session
// and cached in memory, so trigger lookup during dictation stays synchronous —
// the expansion path runs on every keystroke and cannot await a request.

import { api, type SharedMacro } from '@/lib/api';
import { findSnippetByTrigger, type Snippet } from '@/lib/snippets';

export const SHARED_MACROS_CHANGE_EVENT = 'rp-shared-macros-change';

let cache: SharedMacro[] = [];
let loaded = false;
let inFlight: Promise<SharedMacro[]> | null = null;

/** Cached shared macros. Empty until {@link loadSharedMacros} resolves. */
export function getSharedMacros(): SharedMacro[] {
  return cache;
}

export function isSharedMacrosLoaded(): boolean {
  return loaded;
}

/**
 * Fetch (once) the macros visible to this user. Failures are swallowed to the
 * cache level: a workspace with no macro permission, or an offline desktop
 * session, must still expand personal snippets normally.
 */
export function loadSharedMacros(subspecialty?: string): Promise<SharedMacro[]> {
  if (inFlight) return inFlight;
  inFlight = api.macros
    .list(subspecialty)
    .then((rows) => {
      cache = rows;
      loaded = true;
      if (typeof window !== 'undefined') {
        window.dispatchEvent(new CustomEvent(SHARED_MACROS_CHANGE_EVENT));
      }
      return rows;
    })
    .catch(() => {
      loaded = true; // resolved-but-empty; don't retry-storm during dictation
      return cache;
    })
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

/** Drop the cache so the next load refetches (after an admin edits macros). */
export function invalidateSharedMacros(): void {
  cache = [];
  loaded = false;
  inFlight = null;
}

/**
 * Resolve a typed trigger to an expansion body: personal snippet first, then a
 * subspecialty macro, then a tenant-wide macro. Returns null when nothing
 * matches, so the caller leaves the typed text alone.
 */
export function resolveTrigger(trigger: string): { body: string; source: 'personal' | 'subspecialty' | 'tenant' } | null {
  const t = trigger.trim().toLowerCase();
  if (!t) return null;

  const personal: Snippet | undefined = findSnippetByTrigger(t);
  if (personal) return { body: personal.body, source: 'personal' };

  const matches = cache.filter((m) => m.trigger.trim().toLowerCase() === t);
  const sub = matches.find((m) => m.scope === 'Subspecialty');
  if (sub) return { body: sub.body, source: 'subspecialty' };
  const tenantWide = matches.find((m) => m.scope === 'Tenant');
  if (tenantWide) return { body: tenantWide.body, source: 'tenant' };
  return null;
}

/** Test-only reset of module state. */
export function _resetSharedMacros(): void {
  cache = [];
  loaded = false;
  inFlight = null;
}

/** Test-only cache seeding (avoids a network round-trip in unit tests). */
export function _seedSharedMacros(rows: SharedMacro[]): void {
  cache = rows;
  loaded = true;
}
