import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';
import type { Job } from '@/lib/jobs';
import type { JobStreamBuffer } from '@/lib/jobStream';

// AI-013 live token-stream preview (FE-PR4). The component reads streamed text
// from the off-reducer `jobPartials` store via `useJobPartial`, and the exact
// retryable-job decision from the provider via `useJobs().canRetry`. Both are
// mocked at their module boundary so render states are driven directly.
const h = vi.hoisted(() => ({
  partial: undefined as JobStreamBuffer | undefined,
  canRetry: vi.fn(() => true),
}));

vi.mock('@/components/jobs/useJobPartial', () => ({ useJobPartial: () => h.partial }));
vi.mock('@/components/jobs/JobsProvider', () => ({ useJobs: () => ({ canRetry: h.canRetry }) }));

import AiStreamPreview from '@/components/reports/AiStreamPreview';

function job(partial: Partial<Job> = {}): Job {
  return {
    id: 'j1',
    origin: 'hosted',
    kind: 'ai',
    mode: 'impression',
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

function buffer(over: Partial<JobStreamBuffer> = {}): JobStreamBuffer {
  return { text: '', tokens: 0, done: false, ...over };
}

beforeEach(() => {
  h.partial = undefined;
  h.canRetry = vi.fn(() => true);
});

describe('AiStreamPreview', () => {
  it('renders the streamed tail text wearing the AI-tone badge + "Live preview"/"Not yet applied" copy', () => {
    h.partial = buffer({ text: 'Impression: no acute intracranial abnormality…', tokens: 42 });
    render(<AiStreamPreview jobId="j1" job={job({ status: 'running' })} variant="impression" />);

    const panel = screen.getByTestId('ai-stream-preview');
    // AI-blue treatment is the reused StatusBadge tone="ai" (`.rp-status.ai`),
    // not a bespoke AI indicator.
    expect(panel.querySelector('.rp-status.ai')).not.toBeNull();
    expect(screen.getByText(/live preview/i)).toBeInTheDocument();
    expect(screen.getByText(/not yet applied/i)).toBeInTheDocument();
    expect(screen.getByText(/no acute intracranial abnormality/i)).toBeInTheDocument();
    expect(screen.getByText('~42 tokens')).toBeInTheDocument();
  });

  it('marks the streamed body aria-hidden (defers to the single throttled provider live region)', () => {
    h.partial = buffer({ text: 'streaming…', tokens: 3 });
    const { container } = render(<AiStreamPreview jobId="j1" job={job()} variant="generate" />);
    const body = container.querySelector('.rp-stream-preview-body');
    expect(body).not.toBeNull();
    expect(body?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders nothing when there is no partial text and the job is not active', () => {
    h.partial = undefined;
    const { container } = render(
      <AiStreamPreview jobId="j1" job={job({ status: 'error' })} variant="impression" />,
    );
    expect(container.querySelector('.rp-stream-preview')).toBeNull();
  });

  it('still renders (with Stop) while active even before any token has streamed', () => {
    h.partial = undefined;
    render(
      <AiStreamPreview jobId="j1" job={job({ status: 'running' })} variant="impression" onStop={vi.fn()} />,
    );
    expect(screen.getByTestId('ai-stream-preview')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /stop/i })).toBeInTheDocument();
  });

  it('shows Stop while active and calls onStop', () => {
    h.partial = buffer({ text: 'x', tokens: 1 });
    const onStop = vi.fn();
    render(
      <AiStreamPreview jobId="j1" job={job({ status: 'running' })} variant="impression" onStop={onStop} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /stop/i }));
    expect(onStop).toHaveBeenCalledTimes(1);
  });

  it('hides Stop once a cancel has already been requested', () => {
    h.partial = buffer({ text: 'x', tokens: 1 });
    render(
      <AiStreamPreview
        jobId="j1"
        job={job({ status: 'running', cancelRequested: true })}
        variant="impression"
        onStop={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument();
  });

  it('shows Regenerate for a terminal error job when canRetry, and calls onRegenerate', () => {
    h.partial = buffer({ text: 'partial before failure', tokens: 12 });
    const onRegenerate = vi.fn();
    render(
      <AiStreamPreview jobId="j1" job={job({ status: 'error' })} variant="impression" onRegenerate={onRegenerate} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /regenerate/i }));
    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });

  it('shows Regenerate for a cancelled job', () => {
    h.partial = buffer({ text: 'partial', tokens: 5 });
    render(
      <AiStreamPreview jobId="j1" job={job({ status: 'cancelled' })} variant="impression" onRegenerate={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: /regenerate/i })).toBeInTheDocument();
  });

  it('hides Regenerate when the job is not retryable (canRetry false)', () => {
    h.partial = buffer({ text: 'partial', tokens: 5 });
    h.canRetry = vi.fn(() => false);
    render(
      <AiStreamPreview jobId="j1" job={job({ status: 'error' })} variant="impression" onRegenerate={vi.fn()} />,
    );
    expect(screen.queryByRole('button', { name: /regenerate/i })).not.toBeInTheDocument();
  });

  it('does not show Regenerate for an active (non-terminal) job', () => {
    h.partial = buffer({ text: 'streaming', tokens: 5 });
    render(
      <AiStreamPreview
        jobId="j1"
        job={job({ status: 'running' })}
        variant="impression"
        onStop={vi.fn()}
        onRegenerate={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: /regenerate/i })).not.toBeInTheDocument();
  });

  it('shows the raw-output footnote only for variant="local"', () => {
    h.partial = buffer({ text: 'raw model text', tokens: 2 });
    const { rerender } = render(
      <AiStreamPreview
        jobId="j1"
        job={job({ kind: 'local-generate', mode: 'report', origin: 'local' })}
        variant="local"
      />,
    );
    expect(screen.getByText(/raw model output/i)).toBeInTheDocument();

    rerender(<AiStreamPreview jobId="j1" job={job()} variant="generate" />);
    expect(screen.queryByText(/raw model output/i)).not.toBeInTheDocument();
  });

  it('omits the token count until tokens have streamed', () => {
    h.partial = buffer({ text: 'streaming', tokens: 0 });
    render(<AiStreamPreview jobId="j1" job={job({ status: 'running' })} variant="impression" />);
    expect(screen.queryByText(/tokens/i)).not.toBeInTheDocument();
  });
});
