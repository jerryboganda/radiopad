import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// Regression for the "Failed to execute 'text' on 'Response': body stream
// already read" masking bug: the old error path called res.json() (consuming
// the stream) and fell back to res.text() on the SAME response. Any non-OK
// response without a JSON body (empty 404 from a missing route, HTML from a
// proxy) then threw the stream error instead of the real API error. The fix
// reads the body once as text and JSON.parses opportunistically.

const realFetch = globalThis.fetch;

beforeEach(() => {
  vi.resetModules();
});
afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
});

async function callWithResponse(res: Response): Promise<{ message: string; status?: number; body?: unknown }> {
  globalThis.fetch = vi.fn().mockResolvedValue(res);
  const { api } = await import('@/lib/api');
  try {
    await api.reports.generate('r1', { providerId: 'p1' });
    throw new Error('expected api error');
  } catch (e) {
    const err = e as { message: string; status?: number; body?: unknown };
    return { message: err.message, status: err.status, body: err.body };
  }
}

describe('api error body extraction', () => {
  it('surfaces an empty-body 404 as the API error, not a stream error', async () => {
    // A route that does not exist on the backend returns 404 with an empty body.
    const err = await callWithResponse(new Response(null, { status: 404, statusText: 'Not Found' }));
    expect(err.message).toBe('API 404 Not Found');
    expect(err.message).not.toMatch(/body stream/i);
    expect(err.status).toBe(404);
    expect(err.body).toBeNull();
  });

  it('surfaces a non-JSON (HTML) error body as raw text', async () => {
    const err = await callWithResponse(
      new Response('<html>502 Bad Gateway</html>', { status: 502, statusText: 'Bad Gateway' }),
    );
    expect(err.message).toBe('API 502 Bad Gateway');
    expect(err.body).toBe('<html>502 Bad Gateway</html>');
  });

  it('still parses a JSON error body into an object', async () => {
    const err = await callWithResponse(
      new Response(JSON.stringify({ error: 'Provider not found.', kind: 'not_found' }), {
        status: 400,
        statusText: 'Bad Request',
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    expect(err.status).toBe(400);
    expect(err.body).toMatchObject({ error: 'Provider not found.' });
  });
});
