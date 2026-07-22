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
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/lib/api', () => ({
  isTransientPollError: (e: { status?: number; kind?: string }) =>
    e?.kind === 'network' || [408, 429, 500, 502, 503, 504].includes(e?.status ?? 0),
  api: {
    jobs: { list: m.list, get: m.get, cancel: vi.fn(), retry: m.retry },
    reports: { submitAiJob: vi.fn(), submitGenerateJob: m.submitGenerate, aiJobStatus: m.aiJobStatus },
    localGenerate: { submitJob: m.localSubmit, jobStatus: m.localStatus, listJobs: m.localList, cancelJob: vi.fn() },
  },
}));

import JobsProvider, { useJobs } from '@/components/jobs/JobsProvider';
import { ToastProvider } from '@/components/ui/ToastProvider';
import type { JobSubmitSpec } from '@/lib/jobs';

function Harness() {
  const { jobs, submit } = useJobs();
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
