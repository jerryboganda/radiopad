import { request } from '@/lib/api';

export interface CreateReportRequestDto {
  radiologyId: string;
  patientName: string;
  patientAge: number;
  patientGender: string;
  /** Imaging modality (e.g. "CT", "MRI", "X-Ray") — written into Report.Study.Modality so
   *  template/rulebook auto-resolution also works for mobile-created reports. */
  modality?: string;
  /** Body region/part scanned (e.g. "Chest", "Abdomen") — written into Report.Study.BodyPart. */
  bodyPart?: string;
}

export interface DictationAudioDto {
  id: string;
  reportId: string;
  storagePath: string;
  durationSeconds: number;
  transcriptionEngine: string;
  status: string;
  transcribedText?: string | null;
  uploadedAt: string;
  transcribedAt?: string | null;
  errorMessage?: string | null;
}

export interface ReportDto {
  id: string;
  radiologyId: string;
  patientName: string;
  patientAge: number;
  patientGender: string;
  createdAt: string;
  status: string;
  dictations: DictationAudioDto[];
  modality?: string;
  bodyPart?: string;
}

/** One entry in the transcription-engine dropdown (desktop Positive Findings tab and,
 *  in principle, any other transcription surface) — enumerated dynamically from the
 *  DI-registered engines on the backend rather than hardcoded per client. */
export interface TranscriptionEngineDto {
  engineId: string;
  displayName: string;
  isLocal: boolean;
  isAvailable: boolean;
  isDefault: boolean;
}

/**
  Fetch list of reports with optional search string and status filter.
 */
export async function getReports(search?: string, status?: string): Promise<ReportDto[]> {
  const params = new URLSearchParams();
  if (search && search.trim()) {
    params.set('search', search.trim());
  }
  if (status && status !== 'All') {
    params.set('status', status);
  }
  const queryString = params.toString();
  const url = `/api/v1/reporting/reports${queryString ? `?${queryString}` : ''}`;
  return request<ReportDto[]>(url);
}

/**
  Create a new radiology report.
 */
export async function createReport(dto: CreateReportRequestDto): Promise<ReportDto> {
  return request<ReportDto>('/api/v1/reporting/reports', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
}

/**
  Get a single report by ID including its dictation audio list.
 */
export async function getReportById(id: string): Promise<ReportDto> {
  return request<ReportDto>(`/api/v1/reporting/reports/${id}`);
}

/**
  Upload audio dictation file for a report.
 */
export async function uploadDictation(
  reportId: string,
  audioBlob: Blob,
  durationSeconds: number
): Promise<DictationAudioDto> {
  const formData = new FormData();
  const fileExt = audioBlob.type.includes('wav') ? 'wav' : 'webm';
  formData.append('file', audioBlob, `dictation.${fileExt}`);
  formData.append('durationSeconds', durationSeconds.toString());

  return request<DictationAudioDto>(`/api/v1/reporting/reports/${reportId}/dictations`, {
    method: 'POST',
    body: formData,
  });
}

/**
  List available transcription engines for the dropdown (medASR-6gram — the local,
  on-device default — plus any other enabled provider). Drives both the desktop
  Positive Findings tab's engine picker and any future mobile-side selector.
 */
export async function getTranscriptionEngines(): Promise<TranscriptionEngineDto[]> {
  return request<TranscriptionEngineDto[]>('/api/v1/reporting/reports/transcription-engines');
}

/**
  Transcribe (or re-transcribe) a single dictation, optionally overriding the engine.
  Omitting `engine` keeps the dictation's own stored engine (medASR-6gram by default).
 */
export async function transcribeDictation(
  reportId: string,
  dictationId: string,
  engine?: string
): Promise<DictationAudioDto> {
  return request<DictationAudioDto>(
    `/api/v1/reporting/reports/${reportId}/dictations/${dictationId}/transcribe`,
    {
      method: 'POST',
      body: JSON.stringify({ engine: engine ?? null }),
    }
  );
}

/**
  URL for streaming a dictation's stored audio bytes (range-enabled) — pass directly to
  an <audio> element's `src`. Relative so it resolves through the same API base as every
  other reportingClient call (desktop proxy, or the mobile Capacitor companion base).
 */
export function getDictationAudioUrl(reportId: string, dictationId: string): string {
  return `/api/v1/reporting/reports/${reportId}/dictations/${dictationId}/audio`;
}

export const reportingClient = {
  getReports,
  createReport,
  getReportById,
  uploadDictation,
  getTranscriptionEngines,
  transcribeDictation,
  getDictationAudioUrl,
};

export default reportingClient;
