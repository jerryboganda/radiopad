import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import ReportingPage from '@/app/(mobile)/reporting/page';
import * as reportingClientModule from '@/lib/api/reportingClient';
import type { ReportDto } from '@/lib/api/reportingClient';

const pushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: pushMock,
    replace: vi.fn(),
    prefetch: vi.fn(),
  }),
  usePathname: () => '/reporting',
}));

vi.mock('@/lib/api/reportingClient', () => ({
  getReports: vi.fn(),
  createReport: vi.fn(),
  getReportById: vi.fn(),
}));

// Modality/body-part catalogs — empty by default so NewReportModal falls back
// to free-text inputs (matches the fallback behaviour tested elsewhere).
// NB: the global test setup calls `vi.restoreAllMocks()` after every test,
// which wipes any `mockResolvedValue(...)` baked directly into a `vi.fn()`
// inside this factory — so delegate to module-level mocks re-armed in
// `beforeEach` instead (matches the pattern used by other `@/lib/api` mocks
// in this suite, e.g. `__tests__/admin/DictationSettings.test.tsx`).
const modalitiesListMock = vi.fn();
const bodyPartsListMock = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    modalities: { list: () => modalitiesListMock() },
    bodyParts: { list: () => bodyPartsListMock() },
  },
}));

const SAMPLE_REPORTS: ReportDto[] = [
  {
    id: 'report-1',
    radiologyId: 'RAD-2026-001',
    patientName: 'Jane Doe',
    patientAge: 52,
    patientGender: 'Female',
    modality: 'CT',
    bodyPart: 'Chest',
    createdAt: '2026-08-09T10:00:00Z',
    status: 'Pending',
    dictations: [
      {
        id: 'aud-1',
        reportId: 'report-1',
        storagePath: '/audio/1.wav',
        durationSeconds: 15.4,
        transcriptionEngine: 'medASR-6gram',
        status: 'Transcribed',
        transcribedText: 'Chest X-ray shows right lung opacity.',
        uploadedAt: '2026-08-09T10:01:00Z',
      },
    ],
  },
  {
    id: 'report-2',
    radiologyId: 'RAD-2026-002',
    patientName: 'John Smith',
    patientAge: 45,
    patientGender: 'Male',
    createdAt: '2026-08-09T11:00:00Z',
    status: 'Completed',
    dictations: [],
  },
];

describe('Mobile ReportingPage & NewReportModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (reportingClientModule.getReports as any).mockResolvedValue(SAMPLE_REPORTS);
    modalitiesListMock.mockResolvedValue([]);
    bodyPartsListMock.mockResolvedValue([]);
  });

  it('renders page header and report cards list', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /reporting/i })).toBeInTheDocument();
    });

    expect(screen.getByText('RAD-2026-001')).toBeInTheDocument();
    expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    expect(screen.getByText('RAD-2026-002')).toBeInTheDocument();
    expect(screen.getByText('John Smith')).toBeInTheDocument();
    expect(screen.getByText('1 audio')).toBeInTheDocument();
    expect(screen.getByText('0 audios')).toBeInTheDocument();
  });

  it('filters reports list by status pill selection', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    });

    const pendingPill = screen.getByRole('tab', { name: /^Pending$/i });
    fireEvent.click(pendingPill);

    await waitFor(() => {
      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      expect(screen.queryByText('John Smith')).not.toBeInTheDocument();
    });
  });

  it('filters reports by search query input', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    });

    const searchInput = screen.getByPlaceholderText(/filter by radiology id or patient name/i);
    fireEvent.change(searchInput, { target: { value: 'John' } });

    await waitFor(() => {
      expect(screen.queryByText('Jane Doe')).not.toBeInTheDocument();
      expect(screen.getByText('John Smith')).toBeInTheDocument();
    });
  });

  it('navigates to dictate route when clicking a report card', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    });

    const card = screen.getAllByTestId('report-card')[0];
    fireEvent.click(card);

    expect(pushMock).toHaveBeenCalledWith('/reporting/dictate?id=report-1');
  });

  it('opens New Report modal when clicking + New Report button', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /reporting/i })).toBeInTheDocument();
    });

    const newReportBtn = screen.getByTestId('new-report-btn');
    fireEvent.click(newReportBtn);

    await waitFor(() => {
      expect(screen.getByText('Create New Report')).toBeInTheDocument();
      expect(screen.getByTestId('new-report-radiology-id')).toBeInTheDocument();
      expect(screen.getByTestId('new-report-patient-name')).toBeInTheDocument();
    });
  });

  it('submits New Report modal form and navigates to dictate route', async () => {
    const createdReport: ReportDto = {
      id: 'report-new-123',
      radiologyId: 'RAD-2026-999',
      patientName: 'Alice Johnson',
      patientAge: 38,
      patientGender: 'Female',
      modality: 'MRI',
      bodyPart: 'Brain',
      createdAt: new Date().toISOString(),
      status: 'Pending',
      dictations: [],
    };
    (reportingClientModule.createReport as any).mockResolvedValue(createdReport);

    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /reporting/i })).toBeInTheDocument();
    });

    // Open modal
    fireEvent.click(screen.getByTestId('new-report-btn'));

    await waitFor(() => {
      expect(screen.getByText('Create New Report')).toBeInTheDocument();
    });

    // Fill form
    fireEvent.change(screen.getByTestId('new-report-radiology-id'), { target: { value: 'RAD-2026-999' } });
    fireEvent.change(screen.getByTestId('new-report-patient-name'), { target: { value: 'Alice Johnson' } });
    fireEvent.change(screen.getByTestId('new-report-patient-age'), { target: { value: '38' } });
    fireEvent.change(screen.getByTestId('new-report-patient-gender'), { target: { value: 'Female' } });
    fireEvent.change(screen.getByTestId('new-report-modality'), { target: { value: 'MRI' } });
    fireEvent.change(screen.getByTestId('new-report-body-part'), { target: { value: 'Brain' } });

    // Submit
    fireEvent.click(screen.getByTestId('new-report-submit'));

    await waitFor(() => {
      expect(reportingClientModule.createReport).toHaveBeenCalledWith({
        radiologyId: 'RAD-2026-999',
        patientName: 'Alice Johnson',
        patientAge: 38,
        patientGender: 'Female',
        modality: 'MRI',
        bodyPart: 'Brain',
      });
      expect(pushMock).toHaveBeenCalledWith('/reporting/dictate?id=report-new-123');
    });
  });
});
