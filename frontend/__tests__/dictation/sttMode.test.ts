import { describe, it, expect, beforeEach } from 'vitest';
import { getSttMode, setSttMode, isDualCheckOn } from '@/lib/dictation/sttMode';

describe('sttMode (shared dictation engine-mode preference)', () => {
  beforeEach(() => window.localStorage.clear());

  it('defaults to auto and persists a change', () => {
    expect(getSttMode()).toBe('auto');
    setSttMode('ensemble');
    expect(getSttMode()).toBe('ensemble');
    expect(window.localStorage.getItem('radiopad:stt-mode')).toBe('ensemble');
  });

  it('ignores unknown stored values', () => {
    window.localStorage.setItem('radiopad:stt-mode', 'nonsense');
    expect(getSttMode()).toBe('auto');
  });

  it('isDualCheckOn reflects ensemble only', () => {
    expect(isDualCheckOn('ensemble')).toBe(true);
    expect(isDualCheckOn('single')).toBe(false);
    expect(isDualCheckOn('auto')).toBe(false);
  });
});
