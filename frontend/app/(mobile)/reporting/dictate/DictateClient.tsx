'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, Mic } from 'lucide-react';
import { getReportById } from '@/lib/api/reportingClient';
import type { ReportDto, DictationAudioDto } from '@/lib/api/reportingClient';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';
import AudioRecorderControls from '../components/AudioRecorderControls';

export default function DictateClient() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const reportId = searchParams?.get('id') || '';

  const [report, setReport] = useState<ReportDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchReport = useCallback(async () => {
    if (!reportId) return;
    setIsLoading(true);
    setError(null);
    try {
      const data = await getReportById(reportId);
      setReport(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load report details';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [reportId]);

  useEffect(() => {
    fetchReport();
  }, [fetchReport]);

  const handleBack = () => router.push('/reporting');

  const handleUploadSuccess = (dictation: DictationAudioDto) => {
    setReport((prev) => (prev ? { ...prev, dictations: [dictation, ...(prev.dictations || [])] } : prev));
  };

  return (
    <div className="rp-mobile">
      <div className="rp-reporting-list-header">
        <button type="button" className="ghost icon-btn" aria-label="Back to Reporting List" onClick={handleBack}>
          <ArrowLeft size={18} aria-hidden />
        </button>
        <div>
          <h1 className="rp-page-title">
            <Mic size={17} aria-hidden style={{ marginRight: 6, verticalAlign: '-3px' }} />
            Audio Dictation
          </h1>
          <p className="rp-page-sub">Record radiology audio findings</p>
        </div>
      </div>

      {isLoading ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }} aria-busy="true">
          <Skeleton variant="block" height={80} />
          <Skeleton variant="block" height={220} />
        </div>
      ) : error ? (
        <ErrorState title="Couldn't load report" message={error} onRetry={fetchReport} />
      ) : report ? (
        <AudioRecorderControls
          report={report}
          takes={report.dictations || []}
          onUploadSuccess={handleUploadSuccess}
          onDone={handleBack}
        />
      ) : null}
    </div>
  );
}
