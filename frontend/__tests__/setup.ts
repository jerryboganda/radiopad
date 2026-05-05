// Test setup for the RadioPad frontend.
// - Wires `@testing-library/jest-dom` matchers into vitest's `expect`.
// - Provides a default `fetch` mock so accidental network calls fail loudly
//   instead of hitting the real backend.
import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

// Default fetch stub — individual tests override with `vi.spyOn(globalThis, 'fetch')`.
if (typeof globalThis.fetch !== 'function') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = vi.fn(() =>
    Promise.reject(new Error('fetch is not stubbed in this test')),
  );
}
