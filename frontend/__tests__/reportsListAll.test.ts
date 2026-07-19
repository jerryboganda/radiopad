// The worklist must see every report, not just the first page.
//
// GET /api/reports defaults to take=100 ordered by UpdatedAt descending, and the worklist called
// the bare `api.reports.list()` with no parameters at all. It then sorted the rows it got by
// derived priority — client-side. So priority only ever ranked the 100 most recently touched
// reports: a STAT study that had been sitting in the queue while newer routine work was updated
// fell off the end of the response and never reached the sorter. It was not ranked last; it was
// absent, with nothing on screen to say so.
//
// `listAll` pages until the server's X-Total-Count is satisfied, and reports honestly when it hits
// its own ceiling rather than silently truncating.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const realFetch = globalThis.fetch;

beforeEach(() => {
  vi.resetModules();
});
afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
});

/** A fake backend holding `total` reports, honouring skip/take and clamping take to 500. */
function fakeBackend(total: number) {
  const calls: Array<{ skip: number; take: number }> = [];
  globalThis.fetch = vi.fn(async (url: string | URL | Request) => {
    const u = new URL(String(url), 'http://localhost');
    const skip = Number(u.searchParams.get('skip') ?? 0);
    const take = Math.min(Number(u.searchParams.get('take') ?? 100), 500);
    calls.push({ skip, take });
    const items = Array.from({ length: Math.max(0, Math.min(take, total - skip)) }, (_, i) => ({
      id: `r${skip + i}`,
      indication: skip + i === total - 1 ? 'STAT please' : 'routine follow-up',
    }));
    return new Response(JSON.stringify(items), {
      status: 200,
      headers: { 'Content-Type': 'application/json', 'X-Total-Count': String(total) },
    });
  }) as unknown as typeof fetch;
  return calls;
}

describe('api.reports.listAll', () => {
  it('pages past the server default until every report is retrieved', async () => {
    const calls = fakeBackend(1250);
    const { api } = await import('@/lib/api');

    const { items, total, truncated } = await api.reports.listAll();

    expect(total).toBe(1250);
    expect(items).toHaveLength(1250);
    expect(truncated).toBe(false);
    // More than one request: the whole point is not stopping at the first page.
    expect(calls.length).toBeGreaterThan(1);
    // The STAT case is the very last row the server holds — exactly the one the old
    // single-page fetch dropped.
    expect(items.at(-1)?.indication).toBe('STAT please');
  });

  it('returns a single page unchanged when everything fits', async () => {
    const calls = fakeBackend(12);
    const { api } = await import('@/lib/api');

    const { items, truncated } = await api.reports.listAll();

    expect(items).toHaveLength(12);
    expect(truncated).toBe(false);
    expect(calls).toHaveLength(1);
  });

  it('flags truncation instead of silently dropping rows past its ceiling', async () => {
    fakeBackend(100_000);
    const { api } = await import('@/lib/api');

    const { items, total, truncated } = await api.reports.listAll({}, { maxPages: 2, pageSize: 500 });

    expect(items).toHaveLength(1000);
    expect(total).toBe(100_000);
    // The caller must be able to tell the user the view is incomplete.
    expect(truncated).toBe(true);
  });
});
