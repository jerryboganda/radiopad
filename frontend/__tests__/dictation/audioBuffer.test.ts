import { describe, it, expect, beforeEach } from 'vitest';
import {
  setSessionAudio,
  getSessionAudio,
  hasSessionAudio,
  clearSessionAudio,
  _resetAudioBuffer,
} from '@/lib/dictation/audioBuffer';

function blob(size: number): Blob {
  return new Blob([new Uint8Array(size)], { type: 'audio/wav' });
}

beforeEach(() => _resetAudioBuffer());

describe('audioBuffer', () => {
  it('stores and retrieves session audio + transcript + section', () => {
    expect(setSessionAudio('r1', blob(16), 'no acute findings', 'findings')).toBe(true);
    expect(hasSessionAudio('r1')).toBe(true);
    expect(getSessionAudio('r1')?.transcript).toBe('no acute findings');
    expect(getSessionAudio('r1')?.sectionKey).toBe('findings');
  });

  it('rejects empty audio', () => {
    expect(setSessionAudio('r1', blob(0), 't', 'findings')).toBe(false);
    expect(hasSessionAudio('r1')).toBe(false);
  });

  it('clears retained audio', () => {
    setSessionAudio('r1', blob(16), 'x', 'impression');
    clearSessionAudio('r1');
    expect(getSessionAudio('r1')).toBeNull();
  });

  it('isolates by report id', () => {
    setSessionAudio('r1', blob(16), 'one', 'findings');
    setSessionAudio('r2', blob(16), 'two', 'impression');
    expect(getSessionAudio('r1')?.transcript).toBe('one');
    expect(getSessionAudio('r2')?.transcript).toBe('two');
  });
});
