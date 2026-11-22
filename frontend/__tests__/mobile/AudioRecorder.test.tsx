import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react';
import AudioRecorderControls from '@/app/(mobile)/reporting/components/AudioRecorderControls';
import DictateClient from '@/app/(mobile)/reporting/dictate/DictateClient';
import * as reportingClientModule from '@/lib/api/reportingClient';
import type { ReportDto, DictationAudioDto } from '@/lib/api/reportingClient';

// Mock Next.js navigation hooks
const pushMock = vi.fn();
const searchParamsGetMock = vi.fn(() => 'report-123');
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: pushMock,
    replace: vi.fn(),
    prefetch: vi.fn(),
  }),
  useSearchParams: () => ({
    get: searchParamsGetMock,
  }),
  usePathname: () => '/reporting/dictate',
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

// jsdom has no microphone: stand in for the browser's recorder so the component is exercised
// against the same API surface a phone provides. Each stop() delivers one real (non-empty) chunk.
class FakeMediaRecorder {
  state: 'inactive' | 'recording' | 'paused' = 'inactive';
  mimeType = 'audio/webm';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  constructor(public stream: MediaStream) {}
  start() { this.state = 'recording'; }
  pause() { this.state = 'paused'; }
  resume() { this.state = 'recording'; }
  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['voice'], { type: 'audio/webm' }) });
    this.onstop?.();
  }
}

function setMicrophone(getUserMedia: unknown) {
  Object.defineProperty(navigator, 'mediaDevices', {
    value: getUserMedia ? { getUserMedia } : undefined,
    configurable: true,
  });
}

describe('AudioRecorderControls & DictateClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    setMicrophone(vi.fn().mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] }));
    (reportingClientModule.getReportById as any).mockResolvedValue(MOCK_REPORT);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    setMicrophone(undefined);
  });

  it('renders patient info banner correctly', () => {
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} />);

    expect(screen.getByText('RAD-2026-888')).toBeInTheDocument();
    expect(screen.getByText('Sarah Connor')).toBeInTheDocument();
    expect(screen.getByText(/Age 42/i)).toBeInTheDocument();
    expect(screen.getByText('Female')).toBeInTheDocument();
  });

  it('handles record, pause, resume, and stop workflow with timer updates', async () => {
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} />);

    // Initial state
    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:00');
    const recordBtn = screen.getByRole('button', { name: /start recording/i });
    expect(recordBtn).toBeInTheDocument();

    // Click Record (the microphone grant is async)
    await act(async () => { fireEvent.click(recordBtn); });

    expect(screen.getByRole('button', { name: /stop recording/i })).toBeInTheDocument();

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

    // Verify stopped state — re-record and submit buttons appear
    expect(screen.getByRole('button', { name: /submit dictation/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /re-record audio/i })).toBeInTheDocument();
  });

  it('submits audio dictation, resets for another take, and triggers onUploadSuccess callback', async () => {
    vi.useRealTimers();
    const mockDictationResult: DictationAudioDto = {
      id: 'dict-999',
      reportId: 'report-123',
      storagePath: '/uploads/audio.webm',
      durationSeconds: 5,
      transcriptionEngine: 'medASR-6gram',
      status: 'Uploaded',
      uploadedAt: new Date().toISOString(),
    };
    (reportingClientModule.uploadDictation as any).mockResolvedValue(mockDictationResult);

    const onUploadSuccess = vi.fn();
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} onUploadSuccess={onUploadSuccess} />);

    // Start & Stop recording
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /start recording/i })); });
    fireEvent.click(screen.getByRole('button', { name: /stop recording/i }));

    // Click Submit Dictation
    const submitBtn = screen.getByRole('button', { name: /submit dictation/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(reportingClientModule.uploadDictation).toHaveBeenCalledWith(
        'report-123',
        expect.any(Blob),
        expect.any(Number),
      );
    });
    // The upload carries what the recorder captured — never a stand-in payload.
    const uploaded = (reportingClientModule.uploadDictation as any).mock.calls[0][1] as Blob;
    expect(uploaded.size).toBeGreaterThan(0);

    await waitFor(() => {
      expect(onUploadSuccess).toHaveBeenCalledWith(mockDictationResult);
    }, { timeout: 2000 });

    // Multi-take requirement: component resets back to idle, ready for another take.
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start recording/i })).toBeInTheDocument();
    });
  });

  it('refuses to start (and never fabricates audio) when the device has no microphone support', async () => {
    setMicrophone(undefined);
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} />);

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /start recording/i })); });

    expect(screen.getByRole('alert')).toHaveTextContent(/not supported/i);
    expect(screen.getByRole('button', { name: /start recording/i })).toBeInTheDocument();
    expect(reportingClientModule.uploadDictation).not.toHaveBeenCalled();
  });

  it('stays idle with a clear error when microphone access is denied', async () => {
    setMicrophone(vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')));
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} />);

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /start recording/i })); });

    expect(screen.getByRole('alert')).toHaveTextContent(/microphone access was denied/i);
    expect(screen.getByRole('button', { name: /start recording/i })).toBeInTheDocument();
    expect(screen.getByLabelText('Recording Timer')).toHaveTextContent('00:00');
  });

  it('does not upload an empty take', async () => {
    vi.useRealTimers();
    // A recorder that yields no audio data at all.
    vi.stubGlobal('MediaRecorder', class extends FakeMediaRecorder {
      stop() { this.state = 'inactive'; this.onstop?.(); }
    });
    render(<AudioRecorderControls report={MOCK_REPORT} takes={[]} />);

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /start recording/i })); });
    fireEvent.click(screen.getByRole('button', { name: /stop recording/i }));
    fireEvent.click(screen.getByRole('button', { name: /submit dictation/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/no audio was captured/i);
    expect(reportingClientModule.uploadDictation).not.toHaveBeenCalled();
  });

  it('shows a running list of already-uploaded takes for the case', () => {
    const takes: DictationAudioDto[] = [
      {
        id: 'dict-1',
        reportId: 'report-123',
        storagePath: '/uploads/1.webm',
        durationSeconds: 12,
        transcriptionEngine: 'medASR-6gram',
        status: 'Uploaded',
        uploadedAt: new Date().toISOString(),
      },
      {
        id: 'dict-2',
        reportId: 'report-123',
        storagePath: '/uploads/2.webm',
        durationSeconds: 8,
        transcriptionEngine: 'medASR-6gram',
        status: 'Transcribed',
        uploadedAt: new Date().toISOString(),
      },
    ];

    render(<AudioRecorderControls report={MOCK_REPORT} takes={takes} />);

    expect(screen.getByText('2 takes recorded for this case')).toBeInTheDocument();
    expect(screen.getAllByTestId('take-row')).toHaveLength(2);
  });

  it('renders DictateClient, loads report and navigates back on header click', async () => {
    vi.useRealTimers();
    render(<DictateClient />);

    await waitFor(() => {
      expect(screen.getByText('Sarah Connor')).toBeInTheDocument();
      expect(screen.getByText('RAD-2026-888')).toBeInTheDocument();
    });

    const backBtn = screen.getByRole('button', { name: /back to reporting list/i });
    fireEvent.click(backBtn);

    expect(pushMock).toHaveBeenCalledWith('/reporting');
  });
});
