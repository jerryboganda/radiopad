import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import * as React from 'react';

// Integration test of the real provider: shared ticker scheduling, fire-once
// terminal side effects, sidecar-restart detection, and rehydration merge. Only
// `@/lib/api` and `next/navigation` are mocked; everything else is real.

const m = vi.hoisted(() => ({
  list: vi.fn(),
  get: vi.fn(),
  retry: vi.fn(),
  submitGenerate: vi.fn(),
  aiJobStatus: vi.fn(),
  localSubmit: vi.fn(),
  localStatus: vi.fn(),
  localList: vi.fn(),
  crossCheckStatus: vi.fn(),
}));

// Controllable stand-in for the shared `hostedEvents` SSE singleton so tests can
// drive events + status transitions without any network. `subscribe`/`onStatus`
// capture the provider's callbacks; the helpers invoke them.
const ev = vi.hoisted(() => {
  const cbs: { evt: ((e: unknown) => void) | null; st: ((s: string) => void) | null } = {
    evt: null,
    st: null,
  };
  return {
    subscribe: vi.fn((fn: (e: unknown) => void) => {
      cbs.evt = fn;
      return () => {
        if (cbs.evt === fn) cbs.evt = null;
      };
    }),
    onStatus: vi.fn((fn: (s: string) => void) => {
      cbs.st = fn;
      return () => {
        if (cbs.st === fn) cbs.st = null;
      };
    }),
    emit: (e: unknown) => cbs.evt?.(e),
    setStatus: (s: string) => cbs.st?.(s),
    reset: () => {
      cbs.evt = null;
      cbs.st = null;
    },
  };
});

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/lib/api', () => ({
  isTransientPollError: (e: { status?: number; kind?: string }) =>
    e?.kind === 'network' || [408, 429, 500, 502, 503, 504].includes(e?.status ?? 0),
  api: {
    jobs: { list: m.list, get: m.get, cancel: vi.fn(), retry: m.retry },
    reports: {
      submitAiJob: vi.fn(),
      submitGenerateJob: m.submitGenerate,
      aiJobStatus: m.aiJobStatus,
      crossCheckStatus: m.crossCheckStatus,
    },
    localGenerate: { submitJob: m.localSubmit, jobStatus: m.localStatus, listJobs: m.localList, cancelJob: vi.fn() },
  },
}));
vi.mock('@/lib/events', () => ({
  hostedEvents: { subscribe: ev.subscribe, onStatus: ev.onStatus },
  createLocalEvents: () => ({ subscribe: () => () => {}, onStatus: () => () => {} }),
}));

import JobsProvider, { useJobs } from '@/components/jobs/JobsProvider';
import { ToastProvider } from '@/components/ui/ToastProvider';
import { jobPartials } from '@/lib/jobStream';
import type { JobSubmitSpec } from '@/lib/jobs';
import type { JobSummary } from '@/lib/api';

function Harness() {
  const { jobs, submit, trackExternal } = useJobs();
  const gen: JobSubmitSpec = { origin: 'hosted', kind: 'generate', reportId: 'r1' };
  const local: JobSubmitSpec = {
    origin: 'local',
    kind: 'local-generate',
    reportId: 'r9',
    dto: { modality: 'CT', bodyPart: 'Chest' },
  };
  return (
    <div>
      <button onClick={() => void submit(gen)}>gen</button>
      <button onClick={() => void submit(local)}>local</button>
      <button
        onClick={() =>
          trackExternal({
            id: 'xc1',
            origin: 'local',
            kind: 'crosscheck',
            mode: 'findings',
            reportId: 'r5',
            status: 'queued',
            createdAt: Date.now(),
            attempt: 1,
            dismissed: false,
            seen: false,
            notified: false,
          })
        }
      >
        trackXc
      </button>
      {jobs.map((j) => (
        <span key={j.id} data-testid="job">{`${j.id}:${j.status}:${j.errorKind ?? ''}`}</span>
      ))}
    </div>
  );
}

function renderApp() {
  return render(
    <ToastProvider>
      <JobsProvider>
        <Harness />
      </JobsProvider>
    </ToastProvider>,
  );
}

function detail(status: string, extra: Record<string, unknown> = {}) {
  return {
    jobId: 'j1', kind: 'generate', mode: 'generate', status,
    reportId: 'r1', report: { accession: 'A', modality: 'CT', bodyPart: 'Chest', status: 'Draft' },
    error: null, errorKind: null, attempt: 1, retryOfJobId: null,
    startedAt: null, completedAt: null, elapsedMs: 1_000, result: null, ...extra,
  };
}

let terminalEvents: CustomEvent[] = [];
function onTerminal(e: Event) {
  terminalEvents.push(e as CustomEvent);
}

beforeEach(() => {
  terminalEvents = [];
  ev.reset();
  window.addEventListener('radiopad:job-terminal', onTerminal);
  m.list.mockResolvedValue({ jobs: [] });
  m.localList.mockResolvedValue({ jobs: [] });
});

afterEach(() => {
  window.removeEventListener('radiopad:job-terminal', onTerminal);
  vi.useRealTimers();
  vi.clearAllMocks();
  window.localStorage.clear();
});

describe('JobsProvider — shared ticker', () => {
  it('submits, polls queued → running → ok, fires the terminal effect exactly once, then idles', async () => {
    vi.useFakeTimers();
    m.submitGenerate.mockResolvedValue({ jobId: 'j1' });
    m.get
      .mockResolvedValueOnce(detail('running'))
      .mockResolvedValueOnce(detail('ok', { completedAt: new Date().toISOString() }));

    renderApp();
    // Submit (awaits the network jobId, then adds a queued row + kicks the ticker).
    await act(async () => {
      screen.getByText('gen').click();
    });
    expect(screen.getByTestId('job').textContent).toBe('j1:queued:');

    // First poll at 300ms → running.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(screen.getByTestId('job').textContent).toBe('j1:running:');

    // Next poll at +600ms → ok (terminal).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(600);
    });
    expect(screen.getByTestId('job').textContent).toBe('j1:ok:');
    expect(terminalEvents).toHaveLength(1);
    expect(terminalEvents[0].detail).toMatchObject({ jobId: 'j1', status: 'ok', kind: 'generate' });

    // Idle: no active jobs → the ticker stops; further time does not re-poll and
    // does not re-fire the terminal effect.
    const callsAfterOk = m.get.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(m.get.mock.calls.length).toBe(callsAfterOk);
    expect(terminalEvents).toHaveLength(1);
  });

  it('marks a local job errored with sidecar_restart when the sidecar poll 404s', async () => {
    vi.useFakeTimers();
    m.localSubmit.mockResolvedValue({ jobId: 'l1' });
    m.localStatus.mockRejectedValue(Object.assign(new Error('gone'), { status: 404 }));

    renderApp();
    await act(async () => {
      screen.getByText('local').click();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(screen.getByTestId('job').textContent).toBe('l1:error:sidecar_restart');
    expect(terminalEvents).toHaveLength(1);
    expect(terminalEvents[0].detail).toMatchObject({ status: 'error', kind: 'local-generate' });
  });
});

function summary(jobId: string, status: string, extra: Partial<JobSummary> = {}): JobSummary {
  return {
    jobId,
    kind: 'generate',
    mode: 'generate',
    status: status as JobSummary['status'],
    errorKind: null,
    error: null,
    attempt: 1,
    retryOfJobId: null,
    reportId: 'r2',
    report: null,
    createdAt: new Date(1_000).toISOString(),
    startedAt: null,
    completedAt: status === 'ok' ? new Date().toISOString() : null,
    elapsedMs: 1_000,
    ...extra,
  };
}

describe('JobsProvider — SSE stream integration', () => {
  it('pauses hosted polling while the stream status is open', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [summary('j2', 'running')] });
    m.get.mockResolvedValue(detail('running', { jobId: 'j2', reportId: 'r2', report: null }));

    renderApp();
    // Let the mount rehydration paint j2 (a poll timer is now armed at 300ms).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    // Open the stream BEFORE the first poll fires → hosted jobs are pushed, not polled.
    await act(async () => {
      ev.setStatus('open');
    });
    const getCallsAtOpen = m.get.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(m.get.mock.calls.length).toBe(getCallsAtOpen);
  });

  it('resumes hosted polling when the stream transitions from open to down', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [summary('j2', 'running')] });
    m.get.mockResolvedValue(detail('running', { jobId: 'j2', reportId: 'r2', report: null }));

    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      ev.setStatus('open');
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000);
    });
    const before = m.get.mock.calls.length; // paused → no polls
    await act(async () => {
      ev.setStatus('down');
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400);
    });
    expect(m.get.mock.calls.length).toBeGreaterThan(before);
  });

  it('re-hydrates via api.jobs.list on each transition to open (reconnect resume)', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [] });

    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    const afterMount = m.list.mock.calls.length; // 1 (mount hydration)
    await act(async () => {
      ev.setStatus('open');
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    expect(m.list.mock.calls.length).toBe(afterMount + 1);
    // A redundant 'open' with no intervening transition does not re-list.
    await act(async () => {
      ev.setStatus('open');
    });
    expect(m.list.mock.calls.length).toBe(afterMount + 1);
  });

  it('fires the terminal side effect exactly once for a job settled via SSE then re-seen', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [summary('j2', 'running')] });

    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      ev.emit({ type: 'job', data: summary('j2', 'ok') });
    });
    expect(terminalEvents).toHaveLength(1);
    // A stray duplicate terminal (e.g. an in-flight poll landing after) is dropped.
    await act(async () => {
      ev.emit({ type: 'job', data: summary('j2', 'ok') });
    });
    expect(terminalEvents).toHaveLength(1);
  });

  it('auth-error stream status clears all tracked jobs (same as a 401 poll)', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [summary('j2', 'running')] });

    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    expect(screen.getAllByTestId('job').length).toBeGreaterThan(0);
    await act(async () => {
      ev.setStatus('auth-error');
    });
    expect(screen.queryAllByTestId('job')).toHaveLength(0);
  });

  it('ADDs an unknown ACTIVE job from SSE (cross-window) but ignores an unknown TERMINAL one', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [] });
    // Open the stream so the freshly-ADDed active job is pushed, not polled.
    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      ev.setStatus('open');
    });
    await act(async () => {
      ev.emit({ type: 'job', data: summary('jx', 'running') });
    });
    let ids = screen.getAllByTestId('job').map((n) => n.textContent?.split(':')[0]);
    expect(ids).toContain('jx');
    await act(async () => {
      ev.emit({ type: 'job', data: summary('jy', 'ok') });
    });
    ids = screen.getAllByTestId('job').map((n) => n.textContent?.split(':')[0]);
    expect(ids).not.toContain('jy');
    expect(m.get).not.toHaveBeenCalled(); // stream open → no polling of the pushed job
  });

  it('routes partial events to jobPartials.append and never into the reducer', async () => {
    vi.useFakeTimers();
    m.list.mockResolvedValue({ jobs: [] });
    const appendSpy = vi.spyOn(jobPartials, 'append');

    renderApp();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      ev.emit({ type: 'partial', data: { jobId: 'jz', delta: 'Hello', tokens: 2 } });
    });
    expect(appendSpy).toHaveBeenCalledWith('jz', 'Hello', 2);
    expect(screen.queryAllByTestId('job')).toHaveLength(0); // no reducer row from a partial
  });
});

describe('JobsProvider — rehydration', () => {
  it('paints the localStorage local-job seed and merges the server active list', async () => {
    window.localStorage.setItem(
      'rp.jobs.v1',
      JSON.stringify({ v: 1, tenant: '', jobs: [{ id: 'l1', reportId: 'r9', kind: 'local-generate', createdAt: 500 }] }),
    );
    m.list.mockResolvedValue({
      jobs: [
        {
          jobId: 'j2', kind: 'generate', mode: 'generate', status: 'running', errorKind: null, error: null,
          attempt: 1, retryOfJobId: null, reportId: 'r2', report: null,
          createdAt: new Date().toISOString(), startedAt: null, completedAt: null, elapsedMs: 0,
        },
      ],
    });
    // Keep both jobs alive so the ticker doesn't churn during the assertion.
    m.get.mockResolvedValue(detail('running', { jobId: 'j2', reportId: 'r2', report: null }));
    m.localStatus.mockResolvedValue({
      jobId: 'l1', kind: 'local-generate', mode: 'report', status: 'running', elapsedMs: 0, result: null, error: null, errorKind: null,
    });

    renderApp();

    // Seed paints instantly; the server row arrives after list() resolves.
    await waitFor(() => {
      const ids = screen.getAllByTestId('job').map((n) => n.textContent?.split(':')[0]);
      expect(ids).toContain('l1');
      expect(ids).toContain('j2');
    });
  });
});

// FE-PR6 — cross-check migration. The audio half is registered via
// `trackExternal` (bypassing submit()'s network+dedupe path entirely — the
// sidecar multipart call already happened by the time ReportClient calls
// this), and polled through a DEDICATED branch (`api.reports.crossCheckStatus`)
// distinct from every other local job (`api.localGenerate.jobStatus`).
describe('JobsProvider — cross-check external tracking (FE-PR6)', () => {
  it('trackExternal adds the row with no network call, then the ticker polls it via crossCheckStatus', async () => {
    vi.useFakeTimers();
    m.crossCheckStatus.mockResolvedValue({ jobId: 'xc1', status: 'running', stage: 'reconciling engines' });

    renderApp();
    await act(async () => {
      screen.getByText('trackXc').click();
    });
    // Added synchronously (no await submit()) — no network call has happened yet.
    expect(screen.getByTestId('job').textContent).toBe('xc1:queued:');
    expect(m.crossCheckStatus).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(m.crossCheckStatus).toHaveBeenCalledWith('r5', 'xc1');
    expect(m.localStatus).not.toHaveBeenCalled(); // the dedicated branch, never localGenerate.jobStatus
    expect(screen.getByTestId('job').textContent).toBe('xc1:running:');
  });

  it('maps a "completed" cross-check status to the tracked job going "ok"', async () => {
    vi.useFakeTimers();
    m.crossCheckStatus.mockResolvedValue({ jobId: 'xc1', status: 'completed', stage: 'done', corrections: [] });

    renderApp();
    await act(async () => {
      screen.getByText('trackXc').click();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(screen.getByTestId('job').textContent).toBe('xc1:ok:');
  });

  it('a 404 on the cross-check poll marks the job sidecar_restart (registry is in-memory, same as local-generate)', async () => {
    vi.useFakeTimers();
    m.crossCheckStatus.mockRejectedValue(Object.assign(new Error('gone'), { status: 404 }));

    renderApp();
    await act(async () => {
      screen.getByText('trackXc').click();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(screen.getByTestId('job').textContent).toBe('xc1:error:sidecar_restart');
  });
});

