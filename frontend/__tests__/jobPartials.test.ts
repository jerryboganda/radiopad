import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { jobPartials } from '@/lib/jobStream';

// The module-level streamed-text store behind live AI token streaming. Fully
// deterministic under fake timers (the notify throttle is time-based). Each test
// uses a UNIQUE jobId so the singleton never leaks state between cases.

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('jobPartials — accumulation', () => {
  it('concatenates deltas and tracks the latest cumulative token count', async () => {
    jobPartials.append('acc', 'Hello ', 3);
    // Second append lands inside the throttle window; flush it past the window so
    // the published snapshot reflects both deltas.
    await vi.advanceTimersByTimeAsync(150);
    jobPartials.append('acc', 'world', 7);
    expect(jobPartials.getSnapshot('acc')).toMatchObject({ text: 'Hello world', tokens: 7, done: false });
  });

  it('caps the buffer to the last 20k characters (keeps the tail)', () => {
    // A single append publishes immediately, so the snapshot reflects the cap.
    jobPartials.append('cap', 'HEAD' + 'x'.repeat(20_000)); // 20_004 chars
    const buf = jobPartials.getSnapshot('cap');
    expect(buf?.text.length).toBe(20_000);
    expect(buf?.text.includes('HEAD')).toBe(false); // the head was trimmed off
    expect(buf?.text.endsWith('x')).toBe(true);
  });
});

describe('jobPartials — snapshot stability (useSyncExternalStore contract)', () => {
  it('returns a stable reference within a throttle window and a new one after a flush', async () => {
    jobPartials.append('snap', 'a', 1); // immediate publish
    const snap1 = jobPartials.getSnapshot('snap');
    await vi.advanceTimersByTimeAsync(10);
    jobPartials.append('snap', 'b', 2); // throttled → snapshot unchanged
    expect(jobPartials.getSnapshot('snap')).toBe(snap1);
    await vi.advanceTimersByTimeAsync(100); // trailing flush publishes the tail
    const snap2 = jobPartials.getSnapshot('snap');
    expect(snap2).not.toBe(snap1);
    expect(snap2?.text).toBe('ab');
  });
});

describe('jobPartials — throttled notify', () => {
  it('notifies at most once per ~100ms window but never loses the burst tail', async () => {
    let notifications = 0;
    const off = jobPartials.subscribe('thr', () => {
      notifications += 1;
    });
    jobPartials.append('thr', 'a', 1); // immediate → 1
    await vi.advanceTimersByTimeAsync(10);
    jobPartials.append('thr', 'b', 2); // schedules a trailing flush
    await vi.advanceTimersByTimeAsync(10);
    jobPartials.append('thr', 'c', 3); // trailing already queued → no extra notify
    expect(notifications).toBe(1);
    await vi.advanceTimersByTimeAsync(100); // trailing flush fires once
    expect(notifications).toBe(2);
    expect(jobPartials.getSnapshot('thr')?.text).toBe('abc'); // tail rendered
    off();
  });
});

describe('jobPartials — markDone', () => {
  it('sets done:true immediately, bypassing the throttle and cancelling a pending flush', async () => {
    let notifications = 0;
    const off = jobPartials.subscribe('done', () => {
      notifications += 1;
    });
    jobPartials.append('done', 'a', 1); // immediate → 1
    await vi.advanceTimersByTimeAsync(10);
    jobPartials.append('done', 'b', 2); // schedules a trailing flush
    jobPartials.markDone('done'); // bypasses throttle → immediate → 2, cancels the pending flush
    expect(notifications).toBe(2);
    expect(jobPartials.getSnapshot('done')).toMatchObject({ text: 'ab', done: true });
    await vi.advanceTimersByTimeAsync(500); // the cancelled trailing flush never fires
    expect(notifications).toBe(2);
    off();
  });
});

describe('jobPartials — clear', () => {
  it('removes the buffer and notifies subscribers, who then read undefined', () => {
    jobPartials.append('clr', 'a', 1);
    expect(jobPartials.getSnapshot('clr')).toBeDefined();
    let notifications = 0;
    const off = jobPartials.subscribe('clr', () => {
      notifications += 1;
    });
    jobPartials.clear('clr');
    expect(notifications).toBe(1);
    expect(jobPartials.getSnapshot('clr')).toBeUndefined();
    off();
  });
});

describe('jobPartials — subscription plumbing', () => {
  it('stops notifying an unsubscribed listener', async () => {
    let notifications = 0;
    const off = jobPartials.subscribe('sub', () => {
      notifications += 1;
    });
    jobPartials.append('sub', 'a', 1);
    expect(notifications).toBe(1);
    off();
    await vi.advanceTimersByTimeAsync(150);
    jobPartials.append('sub', 'b', 2);
    expect(notifications).toBe(1); // no further notifications after unsubscribe
  });
});
