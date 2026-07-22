import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';

const modalitiesListMock = vi.fn();
const bodyPartsListMock = vi.fn();
const providersListMock = vi.fn();
const createMock = vi.fn();
const patchMock = vi.fn();
const generateMock = vi.fn();
const localGenerateReportMock = vi.fn();
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
    localGenerate: {
      report: (...a: unknown[]) => localGenerateReportMock(...a),
    },
  },
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
    createMock.mockReset().mockResolvedValue({ id: 'r1' });
    patchMock.mockReset().mockResolvedValue({ id: 'r1' });
    generateMock.mockReset().mockResolvedValue({ id: 'r1' });
    localGenerateReportMock.mockReset();
    routerPushMock.mockReset();
  });

  it('walks intake → generate and calls create, patch(findings), and generate(provider)', async () => {
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
    await waitFor(() => expect(generateMock).toHaveBeenCalledWith('r1', { providerId: 'p1' }));

    // The staged overlay reaches its ready state.
    await waitFor(() => expect(screen.getByText('Report ready')).toBeInTheDocument());
    await waitFor(() => expect(routerPushMock).toHaveBeenCalledWith('/reports/view?id=r1'), { timeout: 1_500 });
  });

  it('routes generation through the local sidecar (not the hosted API) when an on-device provider is selected', async () => {
    providersListMock.mockReset().mockResolvedValue([
      {
        id: 'p-local', name: 'MedGemma (on-device)', adapter: 'llama-cpp', model: 'medgemma-1.5-4b-q4',
        compliance: 4, enabled: true, priority: 100,
      },
    ]);
    localGenerateReportMock.mockResolvedValue({
      indication: 'Suspected infarct.',
      technique: 'Non-contrast CT.',
      findings: 'Large left MCA territory infarct.',
      impression: '1. Acute infarct.',
      recommendations: 'No specific follow-up is indicated.',
      provider: 'llama-cpp',
      model: 'medgemma-1.5-4b-q4',
      latencyMs: 500,
    });

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

    await waitFor(() => expect(localGenerateReportMock).toHaveBeenCalledTimes(1));
    expect(localGenerateReportMock.mock.calls[0][0]).toMatchObject({
      modality: 'CT',
      bodyPart: 'Brain',
      findings: 'Large left MCA territory infarct.',
    });
    // The completion never goes through the hosted generate endpoint.
    expect(generateMock).not.toHaveBeenCalled();

    await waitFor(() =>
      expect(patchMock).toHaveBeenCalledWith('r1', {
        indication: 'Suspected infarct.',
        technique: 'Non-contrast CT.',
        findings: 'Large left MCA territory infarct.',
        impression: '1. Acute infarct.',
        recommendations: 'No specific follow-up is indicated.',
      }),
    );

    await waitFor(() => expect(screen.getByText('Report ready')).toBeInTheDocument());
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
