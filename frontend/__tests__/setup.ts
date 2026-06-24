// Test setup for the RadioPad frontend.
// - Wires `@testing-library/jest-dom` matchers into vitest's `expect`.
// - Provides a default `fetch` mock so accidental network calls fail loudly
//   instead of hitting the real backend.
// - Installs a working in-memory `localStorage` (the jsdom build used here
//   exposes a Storage object whose methods are missing, so any code that
//   touches `localStorage` — secureAuth's web fallback, offline dictation
//   drafts, the login page — fails with "clear is not a function").
import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

// Minimal spec-compliant Storage backed by a plain object. Shared by every
// test file so individual tests no longer need to hand-roll a localStorage
// stub; they just call the real API (getItem/setItem/clear/...).
function createMemoryStorage(): Storage {
  let store: Record<string, string> = {};
  return {
    get length() {
      return Object.keys(store).length;
    },
    clear() {
      store = {};
    },
    getItem(key: string) {
      return Object.prototype.hasOwnProperty.call(store, key) ? store[key] : null;
    },
    key(index: number) {
      return Object.keys(store)[index] ?? null;
    },
    removeItem(key: string) {
      delete store[key];
    },
    setItem(key: string, value: string) {
      store[key] = String(value);
    },
  } as Storage;
}

// Install only when the environment's Storage is missing or non-functional so
// we don't clobber a real implementation if a future jsdom upgrade provides one.
function ensureStorage(prop: 'localStorage' | 'sessionStorage') {
  let working = false;
  try {
    working = typeof (globalThis as { [k: string]: unknown })[prop] === 'object'
      && typeof (globalThis as unknown as Record<string, Storage>)[prop]?.clear === 'function';
  } catch {
    working = false;
  }
  if (!working) {
    Object.defineProperty(globalThis, prop, {
      value: createMemoryStorage(),
      configurable: true,
      writable: true,
    });
  }
}

ensureStorage('localStorage');
ensureStorage('sessionStorage');

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  // Keep tests isolated from each other's stored auth tokens / drafts.
  try {
    globalThis.localStorage?.clear();
    globalThis.sessionStorage?.clear();
  } catch {
    /* storage may be intentionally absent in some specs */
  }
});

// Default fetch stub — individual tests override with `vi.spyOn(globalThis, 'fetch')`.
if (typeof globalThis.fetch !== 'function') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = vi.fn(() =>
    Promise.reject(new Error('fetch is not stubbed in this test')),
  );
}
