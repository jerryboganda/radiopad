import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { EventStreamManager, type AppEvent } from '@/lib/events';
import type { EventStreamConnectOpts } from '@/lib/api';
import type { SseEvent } from '@/lib/sse';

// The ref-counted reconnecting SSE manager. Fully driven by a fake `connect`
// function we control (never touches the network) plus fake timers, so backoff
// and liveness are deterministic.

/**
 * A controllable stand-in for `api.events.stream`. Each `connect` call parks a
 * promise the test settles by hand; the test can push events/comments through the
 * captured callbacks and inspect the abort signal. A caller abort resolves the
 * promise (mirroring the real connector's clean-resolve-on-abort).
 */
function makeConnect() {
  const opts: EventStreamConnectOpts[] = [];
  const resolvers: Array<{ resolve: () => void; reject: (e: unknown) => void }> = [];
  const connect = (o: EventStreamConnectOpts): Promise<void> =>
    new Promise<void>((resolve, reject) => {
      opts.push(o);
      resolvers.push({ resolve, reject });
      o.signal.addEventListener('abort', () => resolve());
    });
  const state = {
    get count() {
      return opts.length;
    },
    fireLast(e: SseEvent) {
      opts[opts.length - 1].onEvent(e);
    },
    commentLast() {
      opts[opts.length - 1].onComment?.();
    },
    resolveLast() {
      resolvers[resolvers.length - 1].resolve();
    },
    rejectLast(e: unknown) {
      resolvers[resolvers.length - 1].reject(e);
    },
    lastSignal() {
      return opts[opts.length - 1].signal;
    },
  };
  return { connect, state };
}

const progressData = (jobId: string) =>
  JSON.stringify({ jobId, kind: 'ai', mode: 'impression', reportId: 'r1', tokens: 5 });

/** Flush the microtask queue so a settled connect promise runs `afterConnection`. */
async function flush(): Promise<void> {
  for (let i = 0; i < 4; i++) await Promise.resolve();
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('EventStreamManager — ref counting', () => {
  it('first subscriber opens the connection; last unsubscribe aborts it', () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    expect(mgr.status).toBe('idle');

    const off1 = mgr.subscribe(() => {});
    expect(state.count).toBe(1);
    expect(mgr.status).toBe('connecting');

    const off2 = mgr.subscribe(() => {});
    expect(state.count).toBe(1); // still one connection

    const sig = state.lastSignal();
    off1();
    expect(sig.aborted).toBe(false); // one subscriber remains
    off2();
    expect(sig.aborted).toBe(true); // last unsubscribe aborts
    expect(mgr.status).toBe('idle');
  });
});

describe('EventStreamManager — reconnect backoff', () => {
  it('reconnects with jittered exponential backoff capped at 30s', async () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const off = mgr.subscribe(() => {});
    expect(state.count).toBe(1);

    const bases = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000];
    for (const base of bases) {
      state.rejectLast(new Error('transport')); // failure, no status → 'down' + reconnect
      await flush();
      const before = state.count;
      // Delay is base × (0.7 .. 1.3): not fired just below 0.7×base…
      const low = Math.floor(0.7 * base) - 1;
      await vi.advanceTimersByTimeAsync(low);
      expect(state.count).toBe(before);
      // …fired by 1.3×base.
      await vi.advanceTimersByTimeAsync(Math.ceil(1.3 * base) - low);
      expect(state.count).toBe(before + 1);
    }
    off();
  });

  it('resets the backoff after a connection stays open ≥30s', async () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.5); // jitter factor = 1.0 → exact base delays
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const off = mgr.subscribe(() => {});

    // First attempt fails → reconnect scheduled at the base 1000ms.
    state.rejectLast(new Error('transport'));
    await flush();
    await vi.advanceTimersByTimeAsync(1_000);
    expect(state.count).toBe(2); // second connection

    // Second connection opens and stays healthy for 30s (< 45s liveness).
    state.fireLast({ event: 'progress', data: progressData('j1') });
    expect(mgr.status).toBe('open');
    await vi.advanceTimersByTimeAsync(30_000);
    state.resolveLast(); // server closed the healthy stream
    await flush();

    // Backoff must have reset to base: the next reconnect is ~1000ms, not ~2000ms.
    const before = state.count;
    await vi.advanceTimersByTimeAsync(999);
    expect(state.count).toBe(before);
    await vi.advanceTimersByTimeAsync(1);
    expect(state.count).toBe(before + 1);
    off();
  });
});

describe('EventStreamManager — liveness', () => {
  it('aborts and reconnects after 45s of silence', async () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const off = mgr.subscribe(() => {});

    state.commentLast(); // keep-alive → open + arms the liveness timer
    expect(mgr.status).toBe('open');
    const sig = state.lastSignal();

    await vi.advanceTimersByTimeAsync(45_000);
    expect(sig.aborted).toBe(true); // dead socket aborted
    expect(state.count).toBe(2); // reconnected immediately
    off();
  });
});

describe('EventStreamManager — auth failure', () => {
  it('goes to auth-error on a surviving 401 and stops reconnecting', async () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const off = mgr.subscribe(() => {});

    state.rejectLast(Object.assign(new Error('unauthorized'), { status: 401 }));
    await flush();
    expect(mgr.status).toBe('auth-error');
    expect(state.count).toBe(1);

    // No further scheduled reconnects — even far in the future.
    await vi.advanceTimersByTimeAsync(120_000);
    expect(state.count).toBe(1);
    off();
  });

  it('a fresh subscribe after auth-error restarts the connection', async () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const off1 = mgr.subscribe(() => {});
    state.rejectLast(Object.assign(new Error('forbidden'), { status: 403 }));
    await flush();
    expect(mgr.status).toBe('auth-error');

    const off2 = mgr.subscribe(() => {}); // e.g. AuthGate remounts on fresh login
    expect(state.count).toBe(2);
    expect(mgr.status).toBe('connecting');
    off1();
    off2();
  });
});

describe('EventStreamManager — event mapping', () => {
  it('drops an event with malformed JSON without throwing and keeps streaming', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const seen: AppEvent[] = [];
    const off = mgr.subscribe((e) => seen.push(e));

    state.fireLast({ event: 'job', data: '{not valid json' });
    expect(seen).toHaveLength(0);
    expect(warn).toHaveBeenCalled();

    state.fireLast({ event: 'progress', data: progressData('j9') });
    expect(seen).toHaveLength(1);
    expect(seen[0].type).toBe('progress');
    off();
  });

  it('ignores unknown/`message` event names', () => {
    const { connect, state } = makeConnect();
    const mgr = new EventStreamManager({ connect });
    const seen: AppEvent[] = [];
    const off = mgr.subscribe((e) => seen.push(e));
    state.fireLast({ event: 'message', data: '{}' });
    expect(seen).toHaveLength(0);
    off();
  });
});
