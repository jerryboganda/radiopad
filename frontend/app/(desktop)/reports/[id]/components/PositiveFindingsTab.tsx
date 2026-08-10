'use client';

import { useState, useEffect } from 'react';
import type { DictationAudioDto, TranscriptionEngineDto } from '@/lib/api/reportingClient';
import { getTranscriptionEngines } from '@/lib/api/reportingClient';
import DictationAudioCard from './DictationAudioCard';
import EmptyState from '@/components/ui/EmptyState';
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
  const [engines, setEngines] = useState<TranscriptionEngineDto[]>([]);

  useEffect(() => {
    let cancelled = false;
    getTranscriptionEngines()
      .then((list) => {
        if (!cancelled) setEngines(list);
      })
      .catch(() => {
        // Dropdown falls back to the dictation's own engine in DictationAudioCard.
      });
    return () => {
      cancelled = true;
    };
  }, []);

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
    <div className="rp-panel rp-positive-findings-tab">
      {/* Tab Header */}
      <div className="rp-positive-findings-header">
        <div className="rp-dictation-title">
          <Sparkles size={18} aria-hidden style={{ color: 'var(--accent)' }} />
          <h2 className="rp-positive-findings-title">Positive Findings & Mobile Dictations</h2>
          <span className="badge info" data-testid="dictations-count-badge">
            {items.length}
          </span>
        </div>
        {onRefreshDictations && (
          <button
            type="button"
            className="ghost"
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
          className="banner info rp-dictation-toast"
          role="status"
          data-testid="realtime-dictation-toast"
        >
          <div className="rp-dictation-actions-left">
            <Sparkles size={16} aria-hidden />
            <span>{toastNotice}</span>
          </div>
          <button
            type="button"
            className="ghost"
            onClick={() => setToastNotice(null)}
            aria-label="Dismiss notification"
          >
            <X size={14} />
          </button>
        </div>
      )}

      {/* Dictation cards list */}
      {items.length === 0 ? (
        <div data-testid="empty-dictations-notice">
          <EmptyState
            icon={<Volume2 size={18} aria-hidden />}
            title="No Mobile Dictations Recorded Yet"
            description="Audio dictations uploaded from the mobile reporting app will automatically hand off here in real time with AI transcription."
          />
        </div>
      ) : (
        <div className="rp-takes-list" data-testid="dictation-cards-list">
          {items.map((item) => (
            <DictationAudioCard
              key={item.id}
              dictation={item}
              onAppendToFindings={onAppendToFindings}
              onRetranscribe={onRetranscribe}
              engines={engines}
            />
          ))}
        </div>
      )}
    </div>
  );
}
