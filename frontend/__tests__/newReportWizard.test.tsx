import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';

const modalitiesListMock = vi.fn();
const bodyPartsListMock = vi.fn();
const providersListMock = vi.fn();
const createMock = vi.fn();
const patchMock = vi.fn();
const generateMock = vi.fn();
const submitMock = vi.fn();
const toastMock = vi.fn();
const routerPushMock = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPushMock }),
}));

vi.mock('@/lib/api', () => ({
  COMPLIANCE_LABELS: { 0: 'Blocked', 1: 'No patient data', 3: 'Safe for patient data', 4: 'Runs on-site' },
  api: {
    modalities: { list: () => modalitiesListMock() },
    bodyParts: { list: () => bodyPartsListMock() },
    providers: { list: () => providersListMock() },
    reports: {
      create: (...a: unknown[]) => createMock(...a),
      patch: (...a: unknown[]) => patchMock(...a),
      generate: (...a: unknown[]) => generateMock(...a),
    },
  },
}));

// Phase 6.2 — generation is submit-and-continue through the global JobsProvider,
// confirmed via a toast; mock both boundaries.
vi.mock('@/components/jobs/JobsProvider', () => ({
  useJobs: () => ({ submit: submitMock }),
}));
vi.mock('@/components/ui/ToastProvider', () => ({
  useToast: () => ({ toast: toastMock, dismiss: vi.fn() }),
  ToastProvider: ({ children }: { children: React.ReactNode }) => children,
}));

// Stub the rich editor + dictation overlay (Tiptap/ProseMirror is heavy in jsdom) so
// the test targets the wizard's orchestration, and the searchable combobox as a
// plain <select> so we can drive selections by label.
vi.mock('@/components/editor/RichTextEditor', () => ({
  default: ({ ariaLabel, onChange }: { ariaLabel?: string; onChange: (v: string) => void }) => (
    <textarea aria-label={ariaLabel} onChange={(e) => onChange(e.target.value)} />
  ),
}));
vi.mock('@/components/dictation/DictationOverlay', () => ({ default: () => null }));
vi.mock('@/components/ui/SearchableSelect', () => ({
  default: ({
    options,
    value,
    onChange,
    ariaLabel,
  }: {
    options: { value: string; label: string }[];
    value: string | null;
    onChange: (v: string | null) => void;
    ariaLabel?: string;
  }) => (
    <select aria-label={ariaLabel} value={value ?? ''} onChange={(e) => onChange(e.target.value || null)}>
      <option value="">—</option>
      {options.map((o) => (
        <option key={o.value} value={o.value}>
          {o.label}
        </option>
      ))}
    </select>
  ),
}));

import NewReportWizard from '@/app/(desktop)/reports/new/NewReportWizard';

describe('new report wizard', () => {
  beforeEach(() => {
    modalitiesListMock.mockReset().mockResolvedValue([{ code: 'CT', name: 'Computed Tomography', active: true }]);
    bodyPartsListMock.mockReset().mockResolvedValue([{ code: 'Brain', name: 'Brain', active: true }]);
    providersListMock.mockReset().mockResolvedValue([
      { id: 'p1', name: 'Gemini CLI (OAuth)', adapter: 'gemini-cli', model: '', compliance: 1, enabled: true, priority: 5 },
    ]);
    createMock.mockReset().mockResolvedValue({
      id: 'r1',
      study: { accessionNumber: 'ACC1', modality: 'CT', bodyPart: 'Brain' },
    });
    patchMock.mockReset().mockResolvedValue({ id: 'r1' });
    generateMock.mockReset().mockResolvedValue({ id: 'r1' });
    submitMock.mockReset().mockResolvedValue('job1');
    toastMock.mockReset();
    routerPushMock.mockReset();
  });

  it('walks intake → generate and calls create, patch(findings), then submits a generate JOB', async () => {
    render(<NewReportWizard />);

    // Step 1 — study context loads and populates the selects.
    await waitFor(() => expect(screen.getByLabelText('Modality')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Modality'), { target: { value: 'CT' } });
    fireEvent.change(screen.getByLabelText('Body part'), { target: { value: 'Brain' } });
    fireEvent.change(screen.getByLabelText('Age'), { target: { value: '54' } });
    fireEvent.change(screen.getByLabelText('Gender'), { target: { value: 'Female' } });
    fireEvent.click(screen.getByText('Next →'));

    // Step 2 — positive findings.
    fireEvent.change(screen.getByLabelText('Positive findings'), {
      target: { value: 'Large left MCA territory infarct.' },
    });
    fireEvent.click(screen.getByText('Next →'));

    // Step 3 — history (optional) → Step 4.
    fireEvent.click(screen.getByText('Next →'));

    // Step 4 — provider defaulted to the only enabled model; generate.
    const genBtn = await screen.findByText('Generate report');
    fireEvent.click(genBtn);

    await waitFor(() => expect(createMock).toHaveBeenCalled());
    expect(createMock.mock.calls[0][0]).toMatchObject({
      modality: 'CT',
      bodyPart: 'Brain',
      age: 54,
      gender: 'Female',
    });
    await waitFor(() => expect(patchMock).toHaveBeenCalledWith('r1', { findings: 'Large left MCA territory infarct.' }));
    // A hosted `generate` JOB is submitted — no inline blocking generate call.
    await waitFor(() =>
      expect(submitMock).toHaveBeenCalledWith(
        expect.objectContaining({ origin: 'hosted', kind: 'generate', reportId: 'r1', providerId: 'p1' }),
      ),
    );
    expect(generateMock).not.toHaveBeenCalled();

    // Confirms with a toast; does NOT navigate into the report (batch-posting flow).
    await waitFor(() => expect(toastMock).toHaveBeenCalled());
    expect(routerPushMock).not.toHaveBeenCalled();

    // Form resets for the next case — back on step 1 with a cleared modality.
    await waitFor(() => expect((screen.getByLabelText('Modality') as HTMLSelectElement).value).toBe(''));
  });

  it('routes generation through the local sidecar JOB when an on-device provider is selected', async () => {
    providersListMock.mockReset().mockResolvedValue([
      {
        id: 'p-local', name: 'MedGemma (on-device)', adapter: 'llama-cpp', model: 'medgemma-1.5-4b-q4',
        compliance: 4, enabled: true, priority: 100,
      },
    ]);

    render(<NewReportWizard />);

    await waitFor(() => expect(screen.getByLabelText('Modality')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Modality'), { target: { value: 'CT' } });
    fireEvent.change(screen.getByLabelText('Body part'), { target: { value: 'Brain' } });
    fireEvent.click(screen.getByText('Next →'));
    fireEvent.change(screen.getByLabelText('Positive findings'), {
      target: { value: 'Large left MCA territory infarct.' },
    });
    fireEvent.click(screen.getByText('Next →'));
    fireEvent.click(screen.getByText('Next →'));

    const genBtn = await screen.findByText('Generate report');
    fireEvent.click(genBtn);

    await waitFor(() => expect(createMock).toHaveBeenCalled());
    // A local-generate JOB carrying the study-context dto is submitted…
    await waitFor(() =>
      expect(submitMock).toHaveBeenCalledWith(
        expect.objectContaining({
          origin: 'local',
          kind: 'local-generate',
          reportId: 'r1',
          dto: expect.objectContaining({ modality: 'CT', bodyPart: 'Brain', findings: 'Large left MCA territory infarct.' }),
        }),
      ),
    );
    // …never the hosted generate endpoint.
    expect(generateMock).not.toHaveBeenCalled();
  });

  it('keeps Generate disabled until findings are provided', async () => {
    render(<NewReportWizard />);
    await waitFor(() => expect(screen.getByLabelText('Modality')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Modality'), { target: { value: 'CT' } });
    fireEvent.change(screen.getByLabelText('Body part'), { target: { value: 'Brain' } });
    fireEvent.click(screen.getByText('Next →')); // step 2
    fireEvent.click(screen.getByText('Next →')); // step 3
    fireEvent.click(screen.getByText('Next →')); // step 4

    expect((screen.getByText('Generate report') as HTMLButtonElement).disabled).toBe(true);
  });
});
