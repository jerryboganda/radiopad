import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, act, fireEvent } from '@testing-library/react';
import * as React from 'react';
import GenerationBanner from '@/components/reports/GenerationBanner';
import type { Job } from '@/lib/jobs';

// Slim, non-modal in-editor banner shown while a whole-report generation for the
// open report runs in the background (Phase 6.2). Pure render component — no api
// or provider mocks needed; it reads `formatElapsed`/`stageLabel` from lib/jobs.

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

afterEach(() => {
  vi.useRealTimers();
});

describe('GenerationBanner', () => {
  it('renders the running label, a spinner and an mm:ss elapsed timer', () => {
    render(<GenerationBanner job={job({ createdAt: Date.now() })} />);
    expect(screen.getByTestId('generation-banner')).toBeInTheDocument();
    expect(screen.getByText(/generating draft/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/elapsed time/i).textContent).toMatch(/^\d+:\d{2}$/);
  });

  it('shows the sidecar stage text for a local job', () => {
    render(
      <GenerationBanner
        job={job({ origin: 'local', kind: 'local-generate', mode: 'report', stage: 'model-loading' })}
      />,
    );
    expect(screen.getByText(/loading model/i)).toBeInTheDocument();
  });

  it('does not show a stage for a hosted job (stage is local-only)', () => {
    // A hosted job with a stray stage value must not render the stage line.
    render(<GenerationBanner job={job({ stage: 'generating' })} />);
    expect(screen.queryByText('Generating…')).not.toBeInTheDocument();
  });

  it('advances the elapsed timer once a second', () => {
    vi.useFakeTimers();
    const start = 1_000_000;
    vi.setSystemTime(start);
    render(<GenerationBanner job={job({ createdAt: start })} />);
    expect(screen.getByLabelText(/elapsed time/i).textContent).toBe('0:00');
    // Advancing the fake timers also advances the mocked clock, so the interval's
    // Date.now() lands at start + 65s → 1:05.
    act(() => {
      vi.advanceTimersByTime(65_000);
    });
    expect(screen.getByLabelText(/elapsed time/i).textContent).toBe('1:05');
  });

  it('fires onDismiss when the Dismiss button is clicked', () => {
    const onDismiss = vi.fn();
    render(<GenerationBanner job={job()} onDismiss={onDismiss} />);
    fireEvent.click(screen.getByRole('button', { name: /dismiss/i }));
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('omits the Dismiss button when no handler is supplied', () => {
    render(<GenerationBanner job={job()} />);
    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument();
  });
});
