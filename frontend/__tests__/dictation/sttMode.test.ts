import { describe, it, expect, beforeEach, vi } from 'vitest';
import { isDualCheckOn } from '@/lib/dictation/sttMode';

// The module holds an in-memory source of truth seeded from localStorage at load
// time (so toggles still stick where webview storage writes silently fail).
// Each case loads a fresh module instance after seeding storage.
async function freshModule() {
  vi.resetModules();
  return import('@/lib/dictation/sttMode');
}

describe('sttMode (shared dictation engine-mode preference)', () => {
  beforeEach(() => window.localStorage.clear());

  it('defaults to auto and persists a change', async () => {
    const { getSttMode, setSttMode } = await freshModule();
    expect(getSttMode()).toBe('auto');
    setSttMode('ensemble');
    expect(getSttMode()).toBe('ensemble');
    expect(window.localStorage.getItem('radiopad:stt-mode')).toBe('ensemble');
  });

  it('ignores unknown stored values', async () => {
    window.localStorage.setItem('radiopad:stt-mode', 'nonsense');
    const { getSttMode } = await freshModule();
    expect(getSttMode()).toBe('auto');
  });

  it('seeds from a valid stored value', async () => {
    window.localStorage.setItem('radiopad:stt-mode', 'single');
    const { getSttMode } = await freshModule();
    expect(getSttMode()).toBe('single');
  });

  it('keeps the choice even when localStorage writes throw', async () => {
    const { getSttMode, setSttMode } = await freshModule();
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('storage blocked');
    });
    try {
      setSttMode('ensemble');
      expect(getSttMode()).toBe('ensemble');
    } finally {
      setItem.mockRestore();
    }
  });

  it('isDualCheckOn reflects ensemble only', () => {
    expect(isDualCheckOn('ensemble')).toBe(true);
    expect(isDualCheckOn('single')).toBe(false);
    expect(isDualCheckOn('auto')).toBe(false);
  });
});
