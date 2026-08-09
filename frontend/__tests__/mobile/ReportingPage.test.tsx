import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import React from 'react';
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

const SAMPLE_REPORTS: ReportDto[] = [
  {
    id: 'report-1',
    radiologyId: 'RAD-2026-001',
    patientName: 'Jane Doe',
    patientAge: 52,
    patientGender: 'Female',
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

    // Click 'Pending' pill
    const pendingPill = screen.getByRole('button', { name: /^Pending$/i });
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

  it('navigates to dictate page when clicking a report card', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    });

    const card = screen.getByText('Jane Doe').closest('div[role="listitem"]');
    expect(card).toBeInTheDocument();
    if (card) {
      fireEvent.click(card);
    }

    expect(pushMock).toHaveBeenCalledWith('/mobile/reporting/report-1/dictate');
  });

  it('opens New Report modal when clicking + New Report button', async () => {
    render(<ReportingPage />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /reporting/i })).toBeInTheDocument();
    });

    const newReportBtn = screen.getByRole('button', { name: /new report/i });
    fireEvent.click(newReportBtn);

    await waitFor(() => {
      expect(screen.getByText('Create New Report')).toBeInTheDocument();
      expect(screen.getByLabelText(/radiology id/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/patient name/i)).toBeInTheDocument();
    });
  });

  it('submits New Report modal form and navigates to dictate route', async () => {
    const createdReport: ReportDto = {
      id: 'report-new-123',
      radiologyId: 'RAD-2026-999',
      patientName: 'Alice Johnson',
      patientAge: 38,
      patientGender: 'Female',
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
    fireEvent.click(screen.getByRole('button', { name: /new report/i }));

    await waitFor(() => {
      expect(screen.getByText('Create New Report')).toBeInTheDocument();
    });

    // Fill form
    fireEvent.change(screen.getByLabelText(/radiology id/i), { target: { value: 'RAD-2026-999' } });
    fireEvent.change(screen.getByLabelText(/patient name/i), { target: { value: 'Alice Johnson' } });
    fireEvent.change(screen.getByLabelText(/patient age/i), { target: { value: '38' } });
    fireEvent.change(screen.getByLabelText(/patient gender/i), { target: { value: 'Female' } });

    // Submit
    const submitBtn = screen.getByRole('button', { name: /create & start dictation/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(reportingClientModule.createReport).toHaveBeenCalledWith({
        radiologyId: 'RAD-2026-999',
        patientName: 'Alice Johnson',
        patientAge: 38,
        patientGender: 'Female',
      });
      expect(pushMock).toHaveBeenCalledWith('/mobile/reporting/report-new-123/dictate');
    });
  });
});
