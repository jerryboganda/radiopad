import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react';
import React from 'react';
import AudioRecorderControls from '@/app/(mobile)/reporting/components/AudioRecorderControls';
import DictatePage from '@/app/(mobile)/reporting/[id]/dictate/page';
import * as reportingClientModule from '@/lib/api/reportingClient';
import type { ReportDto, DictationAudioDto } from '@/lib/api/reportingClient';

// Mock Next.js navigation hooks
const pushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: pushMock,
    replace: vi.fn(),
    prefetch: vi.fn(),
  }),
  useParams: () => ({
    id: 'report-123',
  }),
  usePathname: () => '/reporting/report-123/dictate',
}));

// Mock reportingClient module
vi.mock('@/lib/api/reportingClient', async (importOriginal) => {
  const actual = await importOriginal<typeof reportingClientModule>();
  return {
    ...actual,
    getReports: vi.fn(),
    createReport: vi.fn(),
    getReportById: vi.fn(),
    uploadDictation: vi.fn(),
  };
});

const MOCK_REPORT: ReportDto = {
  id: 'report-123',
  radiologyId: 'RAD-2026-888',
  patientName: 'Sarah Connor',
  patientAge: 42,
  patientGender: 'Female',
  createdAt: '2026-08-09T12:00:00Z',
  status: 'Pending',
  dictations: [],
};

describe('AudioRecorderControls & DictatePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    (reportingClientModule.getReportById as any).mockResolvedValue(MOCK_REPORT);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders patient info banner correctly', () => {
    render(<AudioRecorderControls report={MOCK_REPORT} />);

    expect(screen.getByText('RAD-2026-888')).toBeInTheDocument();
    expect(screen.getByText('Sarah Connor')).toBeInTheDocument();
    expect(screen.getByText(/Age: 42/i)).toBeInTheDocument();
    expect(screen.getByText(/Gender: Female/i)).toBeInTheDocument();
  });

  it('handles record, pause, resume, and stop workflow with timer updates', async () => {
    render(<AudioRecorderControls report={MOCK_REPORT} />);

    // Initial state
    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:00');
    const recordBtn = screen.getByRole('button', { name: /start recording/i });
    expect(recordBtn).toBeInTheDocument();

    // Click Record
    fireEvent.click(recordBtn);

    // Advance timer 3 seconds
    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:03');
    const pauseBtn = screen.getByRole('button', { name: /pause recording/i });
    const stopBtn = screen.getByRole('button', { name: /stop recording/i });
    expect(pauseBtn).toBeInTheDocument();
    expect(stopBtn).toBeInTheDocument();

    // Click Pause
    fireEvent.click(pauseBtn);

    // Advance timer 2 seconds (should not increment while paused)
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:03');

    // Click Resume
    const resumeBtn = screen.getByRole('button', { name: /resume recording/i });
    fireEvent.click(resumeBtn);

    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:05');

    // Click Stop
    const currentStopBtn = screen.getByRole('button', { name: /stop recording/i });
    fireEvent.click(currentStopBtn);

    // Verify stopped state, preview bar, re-record, and submit buttons appear
    expect(screen.getByText(/audio preview/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /submit dictation/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /re-record audio/i })).toBeInTheDocument();
  });

  it('submits audio dictation and triggers onUploadSuccess callback', async () => {
    vi.useRealTimers();
    const mockDictationResult: DictationAudioDto = {
      id: 'dict-999',
      reportId: 'report-123',
      storagePath: '/uploads/audio.webm',
      durationSeconds: 5,
      transcriptionEngine: 'Whisper-v3',
      status: 'Uploaded',
      uploadedAt: new Date().toISOString(),
    };
    (reportingClientModule.uploadDictation as any).mockResolvedValue(mockDictationResult);

    const onUploadSuccess = vi.fn();
    render(<AudioRecorderControls report={MOCK_REPORT} onUploadSuccess={onUploadSuccess} />);

    // Start & Stop recording
    fireEvent.click(screen.getByRole('button', { name: /start recording/i }));
    fireEvent.click(screen.getByRole('button', { name: /stop recording/i }));

    // Click Submit Dictation
    const submitBtn = screen.getByRole('button', { name: /submit dictation/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(reportingClientModule.uploadDictation).toHaveBeenCalledWith(
        'report-123',
        expect.any(Blob),
        expect.any(Number)
      );
    });

    await waitFor(() => {
      expect(onUploadSuccess).toHaveBeenCalledWith(mockDictationResult);
    }, { timeout: 2000 });
  });

  it('renders DictatePage, loads report and navigates back on header click', async () => {
    vi.useRealTimers();
    render(<DictatePage />);

    await waitFor(() => {
      expect(screen.getByText('Sarah Connor')).toBeInTheDocument();
      expect(screen.getByText('RAD-2026-888')).toBeInTheDocument();
    });

    const backBtn = screen.getByRole('button', { name: /back to reporting list/i });
    fireEvent.click(backBtn);

    expect(pushMock).toHaveBeenCalledWith('/reporting');
  });
});
