'use client';

import React, { useEffect, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, Mic, RefreshCw, AlertCircle } from 'lucide-react';
import { getReportById, ReportDto } from '@/lib/api/reportingClient';
import AudioRecorderControls from '../components/AudioRecorderControls';

export default function DictateClient() {
  const router = useRouter();
  const params = useParams();
  const searchParams = useSearchParams();
  const reportId = searchParams?.get('id') || (typeof params?.id === 'string' ? params.id : Array.isArray(params?.id) ? params.id[0] : '');

  const [report, setReport] = useState<ReportDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchReport = async () => {
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
  };

  useEffect(() => {
    fetchReport();
  }, [reportId]);

  const handleNavigationBack = () => {
    router.push('/reporting');
  };

  return (
    <div className="rp-dictate-page min-h-screen bg-[var(--bg-app,#0b0f17)] text-[var(--text,#e2e8f0)] pb-24">
      {/* Top Header */}
      <header className="sticky top-0 z-30 bg-[var(--bg-panel,#131722)]/90 backdrop-blur-md border-b border-[var(--border,#262c40)] px-4 py-3.5 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <button
            type="button"
            aria-label="Back to Reporting List"
            onClick={handleNavigationBack}
            className="p-2 rounded-xl bg-slate-800/80 hover:bg-slate-700 active:scale-95 text-slate-300 transition-all"
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
          <div>
            <h1 className="text-lg font-bold text-white flex items-center gap-2">
              <Mic className="w-5 h-5 text-blue-500" />
              Audio Dictation
            </h1>
            <p className="text-xs text-slate-400">Record radiology audio findings</p>
          </div>
        </div>
      </header>

      {/* Main Container */}
      <main className="p-4 flex items-center justify-center min-h-[calc(100vh-120px)]">
        {isLoading ? (
          <div className="py-16 text-center text-slate-400 space-y-3">
            <RefreshCw className="w-7 h-7 animate-spin mx-auto text-blue-500" />
            <p className="text-sm font-medium">Loading report details...</p>
          </div>
        ) : error ? (
          <div className="p-5 rounded-2xl bg-red-950/40 border border-red-900 text-center text-red-300 space-y-3 max-w-md w-full">
            <AlertCircle className="w-8 h-8 mx-auto text-red-400" />
            <p className="text-sm font-medium">{error}</p>
            <div className="flex justify-center gap-3">
              <button
                type="button"
                onClick={fetchReport}
                className="px-4 py-2 text-xs bg-red-900/60 rounded-xl hover:bg-red-800 text-white font-semibold transition-all"
              >
                Retry
              </button>
              <button
                type="button"
                onClick={handleNavigationBack}
                className="px-4 py-2 text-xs bg-slate-800 rounded-xl hover:bg-slate-700 text-slate-300 font-semibold transition-all"
              >
                Back to Worklist
              </button>
            </div>
          </div>
        ) : report ? (
          <AudioRecorderControls
            report={report}
            onUploadSuccess={() => {
              router.push('/reporting');
            }}
            onCancel={handleNavigationBack}
          />
        ) : null}
      </main>
    </div>
  );
}
