// PRD §14.14 (TF-001/TF-004/TF-007) — the teaching library browse page and the
// "save as teaching case" affordance.
//
// The two things worth pinning here are (a) the filters actually reach the
// SERVER — the visibility rule lives there, so a client-side filter would be
// both wrong and unsafe — and (b) the save button states the de-identification
// up front, before the clinician commits, rather than after.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';
import TeachingLibraryPage from '@/app/(desktop)/teaching/page';
import SaveAsTeachingCaseButton from '@/components/teaching/SaveAsTeachingCaseButton';

const search = vi.fn();
const createFromReport = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    teachingCases: {
      search: (params: unknown) => search(params),
      createFromReport: (reportId: string, body: unknown) => createFromReport(reportId, body),
    },
  },
  TEACHING_DIFFICULTY_LABELS: {
    0: 'Introductory',
    1: 'Intermediate',
    2: 'Advanced',
  },
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));

function caseRow(overrides: Record<string, unknown> = {}) {
  return {
    id: 'tc-1',
    title: 'Acute appendicitis on CT',
    modality: 'CT',
    bodyPart: 'Abdomen',
    diagnosis: 'Acute appendicitis',
    teachingPoints: 'Look for the appendicolith.',
    clinicalHistory: '45-year-old with RLQ pain',
    findingsText: 'Dilated appendix measuring 12 mm.',
    impressionText: 'Acute appendicitis.',
    tags: 'gi,emergency',
    difficulty: 1,
    difficultyName: 'Intermediate',
    visibility: 0,
    visibilityName: 'Private',
    publishedAt: null,
    viewCount: 3,
    sourceReportId: null,
    isOwner: true,
    canEdit: true,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

beforeEach(() => {
  search.mockReset();
  createFromReport.mockReset();
});

describe('TeachingLibraryPage', () => {
  it('renders the cases returned by the server', async () => {
    search.mockResolvedValue({ total: 1, items: [caseRow()] });
    const { findByText } = render(<TeachingLibraryPage />);
    await findByText('Acute appendicitis on CT');
    await findByText(/CT \/ Abdomen/);
    await findByText(/3 views/);
  });

  it('shows a first-run empty state when the library has nothing in it', async () => {
    search.mockResolvedValue({ total: 0, items: [] });
    const { findByText } = render(<TeachingLibraryPage />);
    await findByText(/no teaching cases yet/i);
  });

  it('shows a distinct "no matches" empty state once a filter is applied', async () => {
    search.mockResolvedValue({ total: 0, items: [] });
    const { findByText, getByLabelText } = render(<TeachingLibraryPage />);
    await findByText(/no teaching cases yet/i);

    fireEvent.change(getByLabelText(/search teaching cases/i), {
      target: { value: 'pneumothorax' },
    });

    await findByText(/no matching teaching cases/i);
  });

  it('pushes every filter to the server rather than narrowing client-side', async () => {
    search.mockResolvedValue({ total: 1, items: [caseRow()] });
    const { findByText, getByLabelText } = render(<TeachingLibraryPage />);
    await findByText('Acute appendicitis on CT');

    fireEvent.change(getByLabelText(/filter by modality/i), { target: { value: 'CT' } });
    fireEvent.change(getByLabelText(/filter by difficulty/i), { target: { value: '2' } });
    fireEvent.change(getByLabelText(/search teaching cases/i), { target: { value: ' appendix ' } });

    await waitFor(() => {
      const last = search.mock.calls.at(-1)?.[0];
      expect(last).toMatchObject({ modality: 'CT', difficulty: 2, q: 'appendix' });
    });
  });
});

describe('SaveAsTeachingCaseButton', () => {
  it('states the de-identification BEFORE the case is saved', async () => {
    const { getByRole, findByText, queryByText } = render(
      <SaveAsTeachingCaseButton reportId="r-1" />,
    );
    // Nothing has been promised yet — the notice appears with the form.
    expect(queryByText(/will be de-identified/i)).toBeNull();

    fireEvent.click(getByRole('button', { name: /save as teaching case/i }));

    await findByText(/this case will be de-identified/i);
    expect(createFromReport).not.toHaveBeenCalled();
  });

  it('sends trimmed metadata to create-from-report and confirms the save', async () => {
    createFromReport.mockResolvedValue(caseRow({ id: 'tc-new' }));
    const { getByRole, findByText, getByPlaceholderText } = render(
      <SaveAsTeachingCaseButton reportId="r-42" />,
    );
    fireEvent.click(getByRole('button', { name: /save as teaching case/i }));
    await findByText(/this case will be de-identified/i);

    fireEvent.change(getByPlaceholderText(/acute appendicitis on ct/i), {
      target: { value: '  Appendicitis teaching case  ' },
    });
    fireEvent.click(getByRole('button', { name: /de-identify and save/i }));

    await waitFor(() => expect(createFromReport).toHaveBeenCalledTimes(1));
    expect(createFromReport).toHaveBeenCalledWith('r-42', {
      title: 'Appendicitis teaching case',
      diagnosis: undefined,
      teachingPoints: undefined,
      tags: undefined,
      difficulty: 1,
    });

    await findByText(/saved to your teaching file/i);
  });

  it('surfaces a server error instead of claiming the case was saved', async () => {
    createFromReport.mockRejectedValue(new Error('De-identification did not fully scrub the source report'));
    const { getByRole, findByText, queryByText } = render(
      <SaveAsTeachingCaseButton reportId="r-9" />,
    );
    fireEvent.click(getByRole('button', { name: /save as teaching case/i }));
    await findByText(/this case will be de-identified/i);
    fireEvent.click(getByRole('button', { name: /de-identify and save/i }));

    await findByText(/couldn't save the teaching case/i);
    expect(queryByText(/saved to your teaching file/i)).toBeNull();
  });
});
