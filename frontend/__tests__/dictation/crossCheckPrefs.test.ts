import { describe, it, expect, beforeEach } from 'vitest';
import {
  isCrossCheckEnabled,
  setCrossCheckEnabled,
  isUseUbagEnabled,
  setUseUbag,
} from '@/lib/dictation/crossCheckPrefs';

beforeEach(() => localStorage.clear());

describe('crossCheckPrefs', () => {
  it('cross-check defaults on, UBAG defaults off', () => {
    expect(isCrossCheckEnabled()).toBe(true);
    expect(isUseUbagEnabled()).toBe(false);
  });

  it('persists each flag independently', () => {
    setCrossCheckEnabled(false);
    expect(isCrossCheckEnabled()).toBe(false);
    expect(isUseUbagEnabled()).toBe(false);

    setUseUbag(true);
    expect(isUseUbagEnabled()).toBe(true);

    setCrossCheckEnabled(true);
    expect(isCrossCheckEnabled()).toBe(true);
    expect(isUseUbagEnabled()).toBe(true);
  });
});
