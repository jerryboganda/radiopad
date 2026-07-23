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

  it('shows the streamed token count next to the elapsed timer', () => {
    render(<GenerationBanner job={job({ progress: { tokens: 412 } })} />);
    expect(screen.getByText('~412 tokens')).toBeInTheDocument();
  });

  it('omits the token count until tokens have streamed', () => {
    render(<GenerationBanner job={job()} />);
    expect(screen.queryByText(/tokens/i)).not.toBeInTheDocument();
  });

  it('renders a Stop button that calls onStop', () => {
    const onStop = vi.fn();
    render(<GenerationBanner job={job({ status: 'running' })} onStop={onStop} />);
    fireEvent.click(screen.getByRole('button', { name: /stop/i }));
    expect(onStop).toHaveBeenCalledTimes(1);
  });

  it('hides Stop once a cancel has already been requested', () => {
    render(
      <GenerationBanner job={job({ status: 'running', cancelRequested: true })} onStop={vi.fn()} />,
    );
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument();
  });

  it('omits Stop when no onStop handler is supplied', () => {
    render(<GenerationBanner job={job({ status: 'running' })} />);
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument();
  });

  it('reveals the preview slot only after the toggle is clicked (collapsed by default)', () => {
    render(<GenerationBanner job={job()} previewSlot={<div>live-output-body</div>} />);
    expect(screen.queryByText('live-output-body')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /show live output/i }));
    expect(screen.getByText('live-output-body')).toBeInTheDocument();
  });

  it('does not render a preview toggle when no previewSlot is supplied', () => {
    render(<GenerationBanner job={job()} />);
    expect(screen.queryByRole('button', { name: /live output/i })).not.toBeInTheDocument();
  });
});
