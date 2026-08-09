'use client';

import { useState, useEffect } from 'react';
import type { DictationAudioDto } from '@/lib/api/reportingClient';
import DictationAudioCard from './DictationAudioCard';
import { Volume2, Sparkles, X, RefreshCw } from 'lucide-react';

export interface PositiveFindingsTabProps {
  reportId: string;
  dictations?: DictationAudioDto[];
  onAppendToFindings: (text: string) => void;
  onRefreshDictations?: () => Promise<void> | void;
  onRetranscribe?: (dictationId: string, engine: string) => void;
}

export default function PositiveFindingsTab({
  reportId,
  dictations = [],
  onAppendToFindings,
  onRefreshDictations,
  onRetranscribe,
}: PositiveFindingsTabProps) {
  const [items, setItems] = useState<DictationAudioDto[]>(dictations);
  const [toastNotice, setToastNotice] = useState<string | null>(null);

  useEffect(() => {
    setItems(dictations);
  }, [dictations]);

  useEffect(() => {
    if (typeof window === 'undefined') return;

    // SignalR / Real-time window event listeners
    const handleDictationUploaded = (e: Event) => {
      const detail = (e as CustomEvent<{ reportId: string; dictation?: DictationAudioDto }>).detail;
      if (!detail || (detail.reportId && detail.reportId !== reportId)) return;

      setToastNotice('New Audio Dictation received from Mobile');

      if (detail.dictation) {
        setItems((prev) => {
          if (prev.some((x) => x.id === detail.dictation?.id)) return prev;
          return [detail.dictation!, ...prev];
        });
      }

      if (onRefreshDictations) {
        void onRefreshDictations();
      }
    };

    const handleTranscriptionCompleted = (e: Event) => {
      const detail = (e as CustomEvent<{ reportId: string; dictationId: string; text: string; status?: string }>).detail;
      if (!detail || (detail.reportId && detail.reportId !== reportId)) return;

      setItems((prev) =>
        prev.map((item) =>
          item.id === detail.dictationId
            ? { ...item, transcribedText: detail.text, status: detail.status || 'Completed' }
            : item
        )
      );

      if (onRefreshDictations) {
        void onRefreshDictations();
      }
    };

    window.addEventListener('radiopad:dictation-uploaded', handleDictationUploaded);
    window.addEventListener('radiopad:transcription-completed', handleTranscriptionCompleted);
    // Also listen for SignalR global events if dispatched
    window.addEventListener('DictationUploaded', handleDictationUploaded);
    window.addEventListener('TranscriptionCompleted', handleTranscriptionCompleted);

    return () => {
      window.removeEventListener('radiopad:dictation-uploaded', handleDictationUploaded);
      window.removeEventListener('radiopad:transcription-completed', handleTranscriptionCompleted);
      window.removeEventListener('DictationUploaded', handleDictationUploaded);
      window.removeEventListener('TranscriptionCompleted', handleTranscriptionCompleted);
    };
  }, [reportId, onRefreshDictations]);

  return (
    <div className="rp-panel rp-positive-findings-tab p-4 bg-slate-900/40 rounded-lg border border-slate-800">
      {/* Tab Header */}
      <div className="flex items-center justify-between mb-4 pb-2 border-b border-slate-800">
        <div className="flex items-center gap-2">
          <Sparkles className="text-cyan-400" size={18} aria-hidden />
          <h2 className="text-base font-semibold text-slate-100">Positive Findings & Mobile Dictations</h2>
          <span className="badge font-mono text-xs bg-slate-800 text-cyan-300 px-2 py-0.5 rounded-full" data-testid="dictations-count-badge">
            {items.length}
          </span>
        </div>
        {onRefreshDictations && (
          <button
            type="button"
            className="ghost text-xs flex items-center gap-1 text-slate-400 hover:text-slate-200"
            data-testid="refresh-dictations-btn"
            onClick={() => void onRefreshDictations()}
          >
            <RefreshCw size={12} />
            Refresh
          </button>
        )}
      </div>

      {/* Real-time SignalR toast / banner */}
      {toastNotice && (
        <div
          className="banner info flex items-center justify-between mb-4 p-3 bg-cyan-950/80 border border-cyan-700/60 rounded-md text-cyan-200 text-sm shadow-lg animate-fade-in"
          role="status"
          data-testid="realtime-dictation-toast"
        >
          <div className="flex items-center gap-2">
            <Sparkles size={16} className="text-cyan-400 animate-pulse" aria-hidden />
            <span>{toastNotice}</span>
          </div>
          <button
            type="button"
            className="ghost text-xs p-1 text-cyan-300 hover:text-cyan-100"
            onClick={() => setToastNotice(null)}
            aria-label="Dismiss notification"
          >
            <X size={14} />
          </button>
        </div>
      )}

      {/* Dictation cards list */}
      {items.length === 0 ? (
        <div className="rp-panel text-center py-10 px-4 bg-slate-950/50 rounded-lg border border-slate-800/80" data-testid="empty-dictations-notice">
          <Volume2 size={36} className="mx-auto text-slate-500 mb-3" aria-hidden />
          <h3 className="text-sm font-semibold text-slate-300">No Mobile Dictations Recorded Yet</h3>
          <p className="text-xs text-slate-400 mt-1 max-w-sm mx-auto">
            Audio dictations uploaded from the mobile reporting app will automatically hand off here in real time with AI transcription.
          </p>
        </div>
      ) : (
        <div className="space-y-4" data-testid="dictation-cards-list">
          {items.map((item) => (
            <DictationAudioCard
              key={item.id}
              dictation={item}
              onAppendToFindings={onAppendToFindings}
              onRetranscribe={onRetranscribe}
            />
          ))}
        </div>
      )}
    </div>
  );
}
