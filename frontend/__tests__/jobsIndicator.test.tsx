import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import * as React from 'react';
import type { Job, JobsContextValue } from '@/lib/jobs';

// The widget consumes the provider through `useJobs`; we mock that boundary and
// drive render states directly. `useRouter` is mocked so "Open report" is
// observable.
const h = vi.hoisted(() => ({
  value: null as unknown as JobsContextValue,
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: h.push }) }));
vi.mock('@/components/jobs/JobsProvider', () => ({ useJobs: () => h.value }));

import JobsIndicator from '@/components/jobs/JobsIndicator';

function job(partial: Partial<Job> = {}): Job {
  return {
    id: 'j1',
    origin: 'hosted',
    kind: 'generate',
    mode: 'generate',
    reportId: 'r1',
    status: 'running',
    createdAt: Date.now(),
    attempt: 1,
    dismissed: false,
    seen: false,
    notified: false,
    ...partial,
  };
}

function ctx(jobs: Job[], over: Partial<JobsContextValue> = {}): JobsContextValue {
  return {
    jobs,
    submit: vi.fn(),
    cancel: vi.fn(),
    retry: vi.fn(),
    dismiss: vi.fn(),
    clearFinished: vi.fn(),
    markSeen: vi.fn(),
    markApplied: vi.fn(),
    canRetry: () => true,
    ...over,
  };
}

beforeEach(() => {
  h.push.mockReset();
});

describe('JobsIndicator — button + badge', () => {
  it('renders nothing when there are no jobs', () => {
    h.value = ctx([]);
    const { container } = render(<JobsIndicator />);
    expect(container.querySelector('.rp-jobs')).toBeNull();
  });

  it('shows an active count badge + spinner ring and an accurate aria-label', () => {
    h.value = ctx([job({ id: 'a', status: 'running' }), job({ id: 'b', status: 'queued' })]);
    const { container } = render(<JobsIndicator />);
    const btn = screen.getByRole('button');
    expect(btn.getAttribute('aria-label')).toBe('AI jobs — 2 running, 0 finished');
    expect(container.querySelector('.rp-jobs-ring')).not.toBeNull();
    const badge = container.querySelector('.rp-jobs-badge');
    expect(badge?.textContent).toBe('2');
    expect(badge?.className).toContain('tone-active');
  });

  it('flips the badge to a danger tone when an unseen job failed', () => {
    h.value = ctx([job({ id: 'e', status: 'error', errorKind: 'timeout', seen: false })]);
    const { container } = render(<JobsIndicator />);
    const badge = container.querySelector('.rp-jobs-badge');
    expect(badge?.className).toContain('tone-danger');
    expect(container.querySelector('.rp-jobs-ring')).toBeNull(); // nothing running
  });

  it('uses a success tone when the only unseen terminal jobs succeeded', () => {
    h.value = ctx([job({ id: 'k', status: 'ok', seen: false })]);
    const { container } = render(<JobsIndicator />);
    expect(container.querySelector('.rp-jobs-badge')?.className).toContain('tone-success');
  });
});

describe('JobsIndicator — popover rows & actions', () => {
  it('marks jobs seen when opened and lists a row per job', () => {
    const markSeen = vi.fn();
    h.value = ctx([job({ id: 'a', status: 'running' })], { markSeen });
    render(<JobsIndicator />);
    act(() => {
      fireEvent.click(screen.getByRole('button'));
    });
    expect(markSeen).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('a running row offers Cancel and calls cancel(id)', () => {
    const cancel = vi.fn();
    h.value = ctx([job({ id: 'a', status: 'running' })], { cancel });
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    fireEvent.click(screen.getByText('Cancel'));
    expect(cancel).toHaveBeenCalledWith('a');
  });

  it('hides Cancel once a cancel has been requested', () => {
    h.value = ctx([job({ id: 'a', status: 'running', cancelRequested: true })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.queryByText('Cancel')).toBeNull();
    expect(screen.getByText('Cancelling…')).toBeInTheDocument();
  });

  it('an ok row offers Open report and routes with the aiJob hint', () => {
    h.value = ctx([job({ id: 'a', kind: 'ai', mode: 'impression', status: 'ok' })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    fireEvent.click(screen.getByText('Open report'));
    expect(h.push).toHaveBeenCalledWith('/reports/view?id=r1&aiJob=a');
  });

  it('an error row shows the friendly copy, Retry and Open report', () => {
    const retry = vi.fn();
    h.value = ctx([job({ id: 'a', kind: 'ai', mode: 'rewrite', status: 'error', errorKind: 'server_restart' })], { retry });
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.getByText('Interrupted by a server restart — retry to run it again.')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Retry'));
    expect(retry).toHaveBeenCalledWith('a');
    expect(screen.getByText('Open report')).toBeInTheDocument();
  });

  it('does not offer Retry when the job is not retryable', () => {
    h.value = ctx([job({ id: 'a', status: 'error' })], { canRetry: () => false });
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.queryByText('Retry')).toBeNull();
  });

  it('a cancelled row offers Dismiss (no Open report)', () => {
    const dismiss = vi.fn();
    h.value = ctx([job({ id: 'a', status: 'cancelled' })], { dismiss, canRetry: () => false });
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    fireEvent.click(screen.getByText('Dismiss'));
    expect(dismiss).toHaveBeenCalledWith('a');
    expect(screen.queryByText('Open report')).toBeNull();
  });

  it('renders a local stage line for a running local job', () => {
    h.value = ctx([job({ id: 'l', origin: 'local', kind: 'local-generate', status: 'running', stage: 'model-loading' })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.getByText('Loading model…')).toBeInTheDocument();
  });

  it('offers Clear finished when a terminal job is present', () => {
    const clearFinished = vi.fn();
    h.value = ctx([job({ id: 'a', status: 'ok' })], { clearFinished });
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    fireEvent.click(screen.getByText('Clear finished'));
    expect(clearFinished).toHaveBeenCalledTimes(1);
  });
});

describe('JobsIndicator — progress row & token count', () => {
  it('renders a determinate progressbar with aria-valuenow when a real percent is present', () => {
    h.value = ctx([job({ id: 'a', status: 'running', progress: { tokens: 200, percent: 42.6 } })]);
    const { container } = render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    const bar = container.querySelector('.rp-jobs-progress');
    expect(bar).not.toBeNull();
    expect(bar?.getAttribute('role')).toBe('progressbar');
    expect(bar?.getAttribute('aria-valuenow')).toBe('43'); // rounded, clamped 0..100
    expect(bar?.hasAttribute('data-indeterminate')).toBe(false);
    const fill = bar?.querySelector('.rp-progress-fill') as HTMLElement;
    expect(fill.style.width).toBe('43%');
  });

  it('renders an indeterminate progressbar (no aria-valuenow) when there is no percent — the realistic v1 case', () => {
    h.value = ctx([job({ id: 'a', status: 'running', progress: { tokens: 412 } })]);
    const { container } = render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    const bar = container.querySelector('.rp-jobs-progress');
    expect(bar).not.toBeNull();
    expect(bar?.getAttribute('data-indeterminate')).toBe('true');
    expect(bar?.hasAttribute('aria-valuenow')).toBe(false);
    // No inline width is forged for an indeterminate bar — the primitive sweeps it.
    const fill = bar?.querySelector('.rp-progress-fill') as HTMLElement;
    expect(fill.style.width).toBe('');
  });

  it('shows the streamed token count next to the elapsed timer', () => {
    h.value = ctx([job({ id: 'a', status: 'running', progress: { tokens: 412 } })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.getByText('~412 tokens')).toBeInTheDocument();
  });

  it('renders no progress row for a terminal job', () => {
    h.value = ctx([job({ id: 'a', status: 'ok', seen: false })]);
    const { container } = render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(container.querySelector('.rp-jobs-progress')).toBeNull();
  });
});

describe('JobsIndicator — close behaviour', () => {
  it('closes on Escape', () => {
    h.value = ctx([job({ id: 'a', status: 'running' })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    act(() => {
      fireEvent.keyDown(window, { key: 'Escape' });
    });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('closes on an outside mousedown', () => {
    h.value = ctx([job({ id: 'a', status: 'running' })]);
    render(<JobsIndicator />);
    act(() => fireEvent.click(screen.getByRole('button')));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    act(() => {
      fireEvent.mouseDown(document.body);
    });
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
