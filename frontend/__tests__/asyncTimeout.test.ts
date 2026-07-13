import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { raceTimeout } from '@/lib/asyncTimeout';

describe('raceTimeout', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('resolves with the work result before the deadline', async () => {
    const p = raceTimeout(Promise.resolve('ok'), 1000, 'too slow');
    await expect(p).resolves.toBe('ok');
  });

  it('rejects with the given message when the deadline passes first', async () => {
    const never = new Promise<string>(() => undefined);
    const p = raceTimeout(never, 1000, 'too slow');
    const assertion = expect(p).rejects.toThrow('too slow');
    vi.advanceTimersByTime(1001);
    await assertion;
  });

  it('propagates the work rejection unchanged', async () => {
    const p = raceTimeout(Promise.reject(new Error('boom')), 1000, 'too slow');
    await expect(p).rejects.toThrow('boom');
  });
});
