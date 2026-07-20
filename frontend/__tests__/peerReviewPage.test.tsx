// PRD §14.13 (PR-001..010) — the peer-review queue + scoring form.
// Guards the wiring between the page and `api.peerReview`, and the two rules a
// reviewer's judgement depends on: the author stays hidden while the assignment
// is blinded, and the RADPEER score/rationale pair must agree before we let the
// form submit (the backend enforces it too — this keeps the user out of a 400).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';
import PeerReviewPage from '@/app/(desktop)/peer-review/page';
import { canSubmit, categoryRequired } from '@/lib/peerReview';

const mine = vi.fn();
const start = vi.fn();
const submit = vi.fn();
const stats = vi.fn();
const permissions = { value: ['peer_review.read', 'peer_review.submit'] as string[] };

vi.mock('@/lib/api', () => ({
  api: {
    peerReview: {
      mine: (opts?: { as?: string }) => mine(opts),
      start: (id: string) => start(id),
      submit: (id: string, body: unknown) => submit(id, body),
      stats: () => stats(),
    },
  },
}));

vi.mock('@/lib/permissions', () => ({
  usePermissions: () => ({
    loading: false,
    signedOut: false,
    permissions: permissions.value,
    role: 0,
    roleName: 'Radiologist',
    can: (key: string) => permissions.value.includes(key),
  }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));

function assignment(over: Record<string, unknown> = {}) {
  return {
    id: 'pr-1',
    reportId: 'rep-1',
    reviewerUserId: 'u-reviewer',
    reviewerName: 'Dr Reviewer',
    reviewType: 'Random',
    status: 'Assigned',
    score: 0,
    scoreName: 'NotScored',
    complexity: 'Routine',
    discrepancyCategory: 'None',
    comments: '',
    blinded: true,
    authorHidden: true,
    startedAt: null,
    completedAt: null,
    disputeReason: null,
    disputedAt: null,
    createdAt: '2026-07-01T09:00:00Z',
    study: { accessionNumber: 'ACC-9001', modality: 'CT', bodyPart: 'Chest' },
    ...over,
  };
}

beforeEach(() => {
  mine.mockReset();
  start.mockReset();
  submit.mockReset();
  stats.mockReset();
  permissions.value = ['peer_review.read', 'peer_review.submit'];
  start.mockImplementation(async (id: string) => assignment({ id, status: 'InProgress' }));
  stats.mockResolvedValue({
    from: '2026-04-01T00:00:00Z',
    to: '2026-07-01T00:00:00Z',
    totals: {
      reviewed: 0,
      concur: 0,
      discrepancies: 0,
      concordanceRate: 0,
      pending: 0,
      byCategory: { perceptual: 0, interpretive: 0, communication: 0, technique: 0 },
    },
    perReader: [],
  });
});

/** Default: reviewer queue returns `rows`, author-feedback view returns nothing. */
function mockQueue(rows: unknown[]) {
  mine.mockImplementation(async (opts?: { as?: string }) =>
    opts?.as === 'author' ? [] : rows,
  );
}

describe('PeerReviewPage — queue', () => {
  it('shows the empty state when nothing is assigned', async () => {
    mockQueue([]);
    const { findByText } = render(<PeerReviewPage />);
    await findByText(/nothing waiting for you/i);
  });

  it('lists an open assignment with its study line', async () => {
    mockQueue([assignment()]);
    const { findByText } = render(<PeerReviewPage />);
    await findByText(/CT Chest · ACC-9001/);
  });

  it('keeps the original reader hidden while the assignment is blinded', async () => {
    mockQueue([assignment()]);
    const { findByText, queryByText } = render(<PeerReviewPage />);
    await findByText(/hidden until you score/i);
    expect(queryByText('Dr Author')).toBeNull();
  });

  it('names the original reader once the review is unblinded', async () => {
    mockQueue([
      assignment({
        status: 'Completed',
        score: 1,
        authorHidden: false,
        originalAuthorUserId: 'u-author',
        originalAuthorName: 'Dr Author',
        completedAt: '2026-07-02T09:00:00Z',
      }),
    ]);
    const { findByText } = render(<PeerReviewPage />);
    await findByText('Dr Author');
  });

  it('marks the assignment as opened when the reviewer starts it', async () => {
    mockQueue([assignment()]);
    const { findByRole } = render(<PeerReviewPage />);
    fireEvent.click(await findByRole('button', { name: /review/i }));
    await waitFor(() => expect(start).toHaveBeenCalledWith('pr-1'));
  });

  it('surfaces a load failure with a retry', async () => {
    mine.mockRejectedValue(new Error('backend unreachable'));
    const { findByText } = render(<PeerReviewPage />);
    await findByText(/backend unreachable/i);
  });
});

describe('PeerReviewPage — scoring form', () => {
  async function openForm() {
    mockQueue([assignment()]);
    const view = render(<PeerReviewPage />);
    fireEvent.click(await view.findByRole('button', { name: /^review$/i }));
    await view.findByText(/score this read/i);
    return view;
  }

  it('submits a concurring score with no discrepancy category', async () => {
    submit.mockResolvedValue(assignment({ status: 'Completed', score: 1, authorHidden: false }));
    const { findByLabelText, getByRole } = await openForm();

    fireEvent.click(await findByLabelText(/i agree with the original read/i));
    fireEvent.click(getByRole('button', { name: /submit review/i }));

    await waitFor(() =>
      expect(submit).toHaveBeenCalledWith('pr-1', {
        score: 1,
        discrepancyCategory: 0,
        complexity: 0,
        comments: '',
      }),
    );
  });

  it('requires a discrepancy category before a non-concurring score can be sent', async () => {
    const { findByLabelText, getByRole, getByLabelText } = await openForm();

    fireEvent.click(await findByLabelText(/should have been caught most of the time/i));
    // The category picker appears and Submit stays disabled until it is answered.
    const submitButton = getByRole('button', { name: /submit review/i });
    expect(submitButton).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();

    fireEvent.change(getByLabelText(/what kind of discrepancy/i), { target: { value: '1' } });
    expect(submitButton).not.toBeDisabled();
  });

  it('sends the structured rationale, difficulty, and trimmed comments', async () => {
    submit.mockResolvedValue(assignment({ status: 'Completed', score: 3, authorHidden: false }));
    const { findByLabelText, getByLabelText, getByRole } = await openForm();

    fireEvent.click(await findByLabelText(/should have been caught most of the time/i));
    fireEvent.change(getByLabelText(/what kind of discrepancy/i), { target: { value: '2' } });
    fireEvent.change(getByLabelText(/how difficult was this study/i), { target: { value: '1' } });
    fireEvent.change(getByLabelText(/^comments$/i), {
      target: { value: '  Subtle nodule called benign.  ' },
    });
    fireEvent.click(getByRole('button', { name: /submit review/i }));

    await waitFor(() =>
      expect(submit).toHaveBeenCalledWith('pr-1', {
        score: 3,
        discrepancyCategory: 2,
        complexity: 1,
        comments: 'Subtle nodule called benign.',
      }),
    );
  });

  it('clears a stale category when the reviewer switches back to concur', async () => {
    submit.mockResolvedValue(assignment({ status: 'Completed', score: 1, authorHidden: false }));
    const { findByLabelText, getByLabelText, getByRole } = await openForm();

    fireEvent.click(await findByLabelText(/should have been caught most of the time/i));
    fireEvent.change(getByLabelText(/what kind of discrepancy/i), { target: { value: '4' } });
    fireEvent.click(getByLabelText(/i agree with the original read/i));
    fireEvent.click(getByRole('button', { name: /submit review/i }));

    await waitFor(() =>
      expect(submit).toHaveBeenCalledWith(
        'pr-1',
        expect.objectContaining({ score: 1, discrepancyCategory: 0 }),
      ),
    );
  });

  it('shows the backend error instead of silently dropping the submission', async () => {
    submit.mockRejectedValue(new Error('This review has already been submitted.'));
    const { findByLabelText, getByRole, findByText } = await openForm();

    fireEvent.click(await findByLabelText(/i agree with the original read/i));
    fireEvent.click(getByRole('button', { name: /submit review/i }));

    await findByText(/already been submitted/i);
  });
});

describe('PeerReviewPage — dashboard gating', () => {
  it('hides the concordance dashboard from a reviewer without peer_review.manage', async () => {
    mockQueue([]);
    const { findByText, queryByText } = render(<PeerReviewPage />);
    await findByText(/nothing waiting for you/i);
    expect(queryByText(/concordance & discrepancies/i)).toBeNull();
    expect(stats).not.toHaveBeenCalled();
  });

  it('shows per-radiologist concordance for a programme administrator', async () => {
    permissions.value = ['peer_review.read', 'peer_review.submit', 'peer_review.manage'];
    mockQueue([]);
    stats.mockResolvedValue({
      from: '2026-04-01T00:00:00Z',
      to: '2026-07-01T00:00:00Z',
      totals: {
        reviewed: 4,
        concur: 3,
        discrepancies: 1,
        concordanceRate: 0.75,
        pending: 2,
        byCategory: { perceptual: 1, interpretive: 0, communication: 0, technique: 0 },
      },
      perReader: [
        {
          userId: 'u-author',
          displayName: 'Dr Author',
          reviewed: 4,
          concur: 3,
          discrepancies: 1,
          concordanceRate: 0.75,
          byScore: { concur: 3, minor: 0, moderate: 1, major: 0 },
          byCategory: { perceptual: 1, interpretive: 0, communication: 0, technique: 0 },
          complexCases: 1,
          disputed: 0,
        },
      ],
    });

    const { findByText, getAllByText } = render(<PeerReviewPage />);
    await findByText(/concordance & discrepancies/i);
    await findByText('Dr Author');
    expect(getAllByText('75%').length).toBeGreaterThan(0);
  });
});

describe('peerReview rationale rules', () => {
  it('mirrors the server rule: only 2–4 need a discrepancy category', () => {
    expect(categoryRequired(1)).toBe(false);
    expect(categoryRequired(2)).toBe(true);
    expect(categoryRequired(4)).toBe(true);

    expect(canSubmit(1, null)).toBe(true);
    expect(canSubmit(3, null)).toBe(false);
    expect(canSubmit(3, 2)).toBe(true);
    expect(canSubmit(null, 2)).toBe(false);
  });
});
