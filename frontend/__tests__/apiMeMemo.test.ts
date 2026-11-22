// A page load asks /api/tenant/me from four places in sequence; against the hosted database each ask cost 1-2 s.
// api.me() shares one answer for a few seconds, never memoises a failure, and forgets it when the sign-in changes.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const realFetch = globalThis.fetch;
const ME = { tenant: { slug: 't', displayName: 'T' }, user: { email: 'u@t.test', role: 1, permissions: [] } };

beforeEach(() => vi.resetModules());
afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
});

function stubFetch(...statuses: number[]) {
  let i = 0;
  const fn = vi.fn(async () => {
    const status = statuses[Math.min(i++, statuses.length - 1)];
    return new Response(JSON.stringify(status === 200 ? ME : { error: 'x' }), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });
  });
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe('api.me memo', () => {
  it('serves concurrent and back-to-back callers from one request', async () => {
    const fetchFn = stubFetch(200);
    const { api } = await import('@/lib/api');
    await Promise.all([api.me(), api.me()]);
    await api.me();
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('does not memoise a failure', async () => {
    const fetchFn = stubFetch(500, 200);
    const { api } = await import('@/lib/api');
    await expect(api.me()).rejects.toBeTruthy();
    await expect(api.me()).resolves.toMatchObject({ tenant: { slug: 't' } });
    expect(fetchFn).toHaveBeenCalledTimes(2);
  });

  it('forgets the answer when the sign-in changes', async () => {
    const fetchFn = stubFetch(200);
    const { api, setActiveAuthToken } = await import('@/lib/api');
    await api.me();
    setActiveAuthToken('another-token');
    await api.me();
    expect(fetchFn).toHaveBeenCalledTimes(2);
  });
});
