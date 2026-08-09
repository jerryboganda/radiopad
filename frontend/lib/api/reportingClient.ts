import { request } from '@/lib/api';

export interface CreateReportRequestDto {
  radiologyId: string;
  patientName: string;
  patientAge: number;
  patientGender: string;
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

export const reportingClient = {
  getReports,
  createReport,
  getReportById,
};

export default reportingClient;
