import { describe, it, expect, vi, afterEach } from 'vitest';
import { api, isTransientPollError } from '@/lib/api';
import { describeAiError } from '@/lib/aiErrors';

// Request-shaping tests for the durable async-job primitives (Phase 4.1) plus the
// shared errorKind→copy map. The backend is never contacted — `globalThis.fetch`
// is mocked and we assert the URL / method / body each primitive produces. In
// jsdom `apiBase()` and `localSttBase()` both resolve to '' (no Tauri sidecar),
// so `requestLocal` falls back to the same hosted `fetch` path.

const realFetch = globalThis.fetch;

function mockFetch(bodyObj: unknown, status = 200): ReturnType<typeof vi.fn> {
  const fn = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(bodyObj), {
      status,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** The (url, init) of the single fetch the primitive should have made. */
function lastCall(fn: ReturnType<typeof vi.fn>): { url: string; init: RequestInit } {
  expect(fn).toHaveBeenCalledTimes(1);
  const [url, init] = fn.mock.calls[0] as [string, RequestInit];
  return { url, init: init ?? {} };
}

afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
});

describe('api.reports async-job primitives', () => {
  it('submitAiJob POSTs the mode/provider to the ai/jobs endpoint', async () => {
    const fn = mockFetch({ jobId: 'j1', status: 'queued' }, 202);
    const out = await api.reports.submitAiJob('r1', { mode: 'impression', providerId: 'p9' });
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/reports/r1/ai/jobs');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ mode: 'impression', providerId: 'p9' });
    expect(out).toEqual({ jobId: 'j1', status: 'queued' });
  });

  it('submitGenerateJob POSTs to the generate/jobs endpoint (empty body allowed)', async () => {
    const fn = mockFetch({ jobId: 'j2', status: 'queued' }, 202);
    await api.reports.submitGenerateJob('r2');
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/reports/r2/generate/jobs');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({});
  });

  it('submitGenerateJob forwards a providerId when given', async () => {
    const fn = mockFetch({ jobId: 'j3' }, 202);
    await api.reports.submitGenerateJob('r3', { providerId: 'p1' });
    const { init } = lastCall(fn);
    expect(JSON.parse(String(init.body))).toEqual({ providerId: 'p1' });
  });

  it('submitCrossCheckJob POSTs the text/section/useUbag to the crosscheck review jobs endpoint', async () => {
    const fn = mockFetch({ jobId: 'xc1' }, 202);
    const out = await api.reports.submitCrossCheckJob('r7', { text: 'liver lesion', sectionKey: 'findings', useUbag: true });
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/reports/r7/crosscheck/review/jobs');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ text: 'liver lesion', sectionKey: 'findings', useUbag: true });
    expect(out).toEqual({ jobId: 'xc1' });
  });

  it('aiJobStatus GETs the report-scoped poll path', async () => {
    const fn = mockFetch({ jobId: 'j1', kind: 'ai', mode: 'impression', status: 'ok', elapsedMs: 12, result: { text: 'x' }, error: null, errorKind: null });
    const env = await api.reports.aiJobStatus<{ text: string }>('r1', 'j1');
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/reports/r1/ai/jobs/j1');
    expect(init.method ?? 'GET').toBe('GET');
    expect(env.status).toBe('ok');
    expect(env.result).toEqual({ text: 'x' });
  });
});

describe('api.jobs unified engine', () => {
  it('list builds the active/limit query string', async () => {
    const fn = mockFetch({ jobs: [] });
    await api.jobs.list({ active: true, limit: 50 });
    expect(lastCall(fn).url).toBe('/api/jobs?active=true&limit=50');
  });

  it('list with no options hits the bare endpoint', async () => {
    const fn = mockFetch({ jobs: [] });
    await api.jobs.list();
    expect(lastCall(fn).url).toBe('/api/jobs');
  });

  it('list omits active when false but keeps limit', async () => {
    const fn = mockFetch({ jobs: [] });
    await api.jobs.list({ active: false, limit: 20 });
    expect(lastCall(fn).url).toBe('/api/jobs?limit=20');
  });

  it('get fetches a single job with its result', async () => {
    const fn = mockFetch({ jobId: 'j1', reportId: 'r1', status: 'ok', result: { text: 'y' } });
    const detail = await api.jobs.get('j1');
    expect(lastCall(fn).url).toBe('/api/jobs/j1');
    expect(detail.result).toEqual({ text: 'y' });
  });

  it('cancel POSTs to the cancel action', async () => {
    const fn = mockFetch({ status: 'running', cancelRequested: true }, 202);
    const out = await api.jobs.cancel('j1');
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/jobs/j1/cancel');
    expect(init.method).toBe('POST');
    expect(out).toEqual({ status: 'running', cancelRequested: true });
  });

  it('retry POSTs and returns the new job id', async () => {
    const fn = mockFetch({ jobId: 'j2' }, 202);
    const out = await api.jobs.retry('j1');
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/jobs/j1/retry');
    expect(init.method).toBe('POST');
    expect(out).toEqual({ jobId: 'j2' });
  });
});

describe('api.localGenerate async-job primitives', () => {
  it('submitJob POSTs the dto + correlationId to the sidecar job endpoint', async () => {
    const fn = mockFetch({ jobId: 'lj1', status: 'queued' }, 202);
    await api.localGenerate.submitJob({ modality: 'CT', bodyPart: 'Chest', correlationId: 'r1' });
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/local-generation/jobs');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ modality: 'CT', bodyPart: 'Chest', correlationId: 'r1' });
  });

  it('jobStatus GETs the sidecar poll path (carries a stage)', async () => {
    const fn = mockFetch({ jobId: 'lj1', kind: 'local-generate', mode: 'report', status: 'running', elapsedMs: 5, result: null, error: null, errorKind: null, stage: 'model-loading' });
    const env = await api.localGenerate.jobStatus('lj1');
    expect(lastCall(fn).url).toBe('/api/local-generation/jobs/lj1');
    expect(env.stage).toBe('model-loading');
  });

  it('listJobs GETs the sidecar list for rehydration', async () => {
    const fn = mockFetch({ jobs: [] });
    await api.localGenerate.listJobs();
    expect(lastCall(fn).url).toBe('/api/local-generation/jobs');
  });

  it('cancelJob POSTs to the sidecar cancel action', async () => {
    const fn = mockFetch({ status: 'cancelled' }, 202);
    await api.localGenerate.cancelJob('lj1');
    const { url, init } = lastCall(fn);
    expect(url).toBe('/api/local-generation/jobs/lj1/cancel');
    expect(init.method).toBe('POST');
  });
});

describe('isTransientPollError (exported for the shared poll ticker)', () => {
  it('retries network drops and idempotent-safe transient statuses', () => {
    expect(isTransientPollError({ kind: 'network' })).toBe(true);
    for (const status of [408, 429, 500, 502, 503, 504]) {
      expect(isTransientPollError({ status })).toBe(true);
    }
  });

  it('fails fast on deterministic errors', () => {
    for (const status of [400, 401, 403, 404, 409]) {
      expect(isTransientPollError({ status })).toBe(false);
    }
  });
});

describe('describeAiError', () => {
  it('maps every backend errorKind to non-empty copy', () => {
    for (const kind of [
      'not_found',
      'report_modified',
      'quota_exceeded',
      'provider_policy',
      'provider_transport',
      'rulebook_governance',
      'timeout',
      'server_error',
    ]) {
      expect(describeAiError(kind)).toBeTruthy();
      // Curated copy is used, not the generic fallback.
      expect(describeAiError(kind)).not.toBe('The AI request could not be completed. Please try again.');
    }
  });

  it('uses the exact operator-specified copy for the new restart kinds', () => {
    expect(describeAiError('server_restart')).toBe('Interrupted by a server restart — retry to run it again.');
    expect(describeAiError('sidecar_restart')).toBe('The local AI process restarted — retry to run it again.');
    expect(describeAiError('apply_failed')).toBe(
      'The report could not be updated with the generated draft — retry to run it again.',
    );
  });

  it('falls back to the provided message for an unknown kind', () => {
    expect(describeAiError('brand_new_kind', 'raw server detail')).toBe('raw server detail');
  });

  it('falls back to the message when the kind is null/undefined', () => {
    expect(describeAiError(null, 'the server said this')).toBe('the server said this');
    expect(describeAiError(undefined, 'the server said this')).toBe('the server said this');
  });

  it('uses the generic message when neither a known kind nor a fallback is present', () => {
    expect(describeAiError(null)).toBe('The AI request could not be completed. Please try again.');
    expect(describeAiError('', '   ')).toBe('The AI request could not be completed. Please try again.');
  });

  it('prefers curated copy over a fallback for a known kind', () => {
    // The report editor passes `err.body?.error || describeAiError(kind, msg)`, but
    // when describeAiError IS reached for a known kind its curated copy wins.
    expect(describeAiError('server_restart', 'some other message')).toBe(
      'Interrupted by a server restart — retry to run it again.',
    );
  });
});
