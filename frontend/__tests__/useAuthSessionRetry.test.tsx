// A network blip (API restarting) must not bounce a signed-in user to the login page; a real 401 still must.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

const meMock = vi.fn();
vi.mock('@/lib/api', () => ({ api: { me: () => meMock() } }));

import { useAuthSession } from '@/lib/useAuthSession';

const ME = { tenant: { slug: 't', displayName: 'T' }, user: { email: 'u@t.test', role: 1, permissions: [] } };

beforeEach(() => {
  vi.useFakeTimers();
  meMock.mockReset();
});
afterEach(() => vi.useRealTimers());

describe('useAuthSession network retry', () => {
  it('retries a status-less failure and ends up signed in', async () => {
    meMock.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(ME);
    const { result } = renderHook(() => useAuthSession());
    await act(async () => { await vi.advanceTimersByTimeAsync(1600); });
    expect(result.current.signedOut).toBe(false);
    expect(meMock).toHaveBeenCalledTimes(2);
  });

  it('treats a persistent network failure as signed out after the retries', async () => {
    meMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const { result } = renderHook(() => useAuthSession());
    await act(async () => { await vi.advanceTimersByTimeAsync(3200); });
    expect(result.current.signedOut).toBe(true);
    expect(meMock).toHaveBeenCalledTimes(3);
  });

  it('does not retry a real 401', async () => {
    meMock.mockRejectedValue(Object.assign(new Error('unauthenticated'), { status: 401 }));
    const { result } = renderHook(() => useAuthSession());
    await act(async () => { await vi.advanceTimersByTimeAsync(10); });
    expect(result.current.signedOut).toBe(true);
    expect(meMock).toHaveBeenCalledTimes(1);
  });
});
