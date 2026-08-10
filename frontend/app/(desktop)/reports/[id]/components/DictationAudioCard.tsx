'use client';

import { useState, useRef, useEffect } from 'react';
import { Play, Pause, Check, RefreshCw, Volume2 } from 'lucide-react';
import type { DictationAudioDto, TranscriptionEngineDto } from '@/lib/api/reportingClient';
import { getDictationAudioUrl } from '@/lib/api/reportingClient';

export interface DictationAudioCardProps {
  dictation: DictationAudioDto;
  onAppendToFindings: (text: string) => void;
  onRetranscribe?: (dictationId: string, engine: string) => void;
  /** Transcription engines available for the dropdown (fetched once by the parent tab
   *  via getTranscriptionEngines() and shared across every card). Falls back to just the
   *  dictation's own engine if the list hasn't loaded yet. */
  engines?: TranscriptionEngineDto[];
}

const SPEED_OPTIONS = [1.0, 1.25, 1.5, 2.0];

export default function DictationAudioCard({
  dictation,
  onAppendToFindings,
  onRetranscribe,
  engines,
}: DictationAudioCardProps) {
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(dictation.durationSeconds || 0);
  const [speed, setSpeed] = useState(1.0);
  const [editedText, setEditedText] = useState(dictation.transcribedText || '');
  const [selectedEngine, setSelectedEngine] = useState<string>(
    dictation.transcriptionEngine || 'medASR-6gram'
  );
  const audioRef = useRef<HTMLAudioElement | null>(null);

  useEffect(() => {
    setEditedText(dictation.transcribedText || '');
  }, [dictation.transcribedText]);

  const audioUrl = dictation.storagePath?.startsWith('http') || dictation.storagePath?.startsWith('/')
    ? dictation.storagePath
    : getDictationAudioUrl(dictation.reportId, dictation.id);

  const togglePlay = () => {
    if (!audioRef.current) return;
    if (isPlaying) {
      audioRef.current.pause();
      setIsPlaying(false);
    } else {
      audioRef.current.playbackRate = speed;
      audioRef.current
        .play()
        ?.then(() => setIsPlaying(true))
        ?.catch(() => setIsPlaying(false));
    }
  };

  const handleSeek = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newTime = parseFloat(e.target.value);
    setCurrentTime(newTime);
    if (audioRef.current) {
      audioRef.current.currentTime = newTime;
    }
  };

  const handleSpeedChange = (newSpeed: number) => {
    setSpeed(newSpeed);
    if (audioRef.current) {
      audioRef.current.playbackRate = newSpeed;
    }
  };

  const formatTime = (secs: number) => {
    if (isNaN(secs) || secs < 0) return '0:00';
    const m = Math.floor(secs / 60);
    const s = Math.floor(secs % 60);
    return `${m}:${s < 10 ? '0' : ''}${s}`;
  };

  const isMedAsr = (dictation.transcriptionEngine || '').toLowerCase().includes('medasr');
  const engineBadgeLabel = isMedAsr ? 'medASR (4.4% WER)' : dictation.transcriptionEngine || 'UBAG Gemini';
  const engineOptions: TranscriptionEngineDto[] =
    engines && engines.length > 0
      ? engines
      : [
          { engineId: 'medASR-6gram', displayName: 'medASR (Local 4.4% WER)', isLocal: true, isAvailable: true, isDefault: true },
          { engineId: 'UBAG Gemini', displayName: 'UBAG Gemini', isLocal: false, isAvailable: true, isDefault: false },
        ];

  return (
    <div className="rp-panel rp-dictation-card" data-testid="dictation-audio-card">
      {/* Header */}
      <div className="rp-dictation-card-header">
        <div className="rp-dictation-title">
          <Volume2 size={16} aria-hidden style={{ color: 'var(--accent)' }} />
          <span>Senior Radiologist Dictation</span>
        </div>
        <div className="rp-dictation-meta">
          <span className={`badge ${isMedAsr ? 'ok' : 'ai'}`} data-testid="engine-badge">
            {engineBadgeLabel}
          </span>
          <span>
            {dictation.uploadedAt
              ? new Date(dictation.uploadedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
              : 'Just now'}
          </span>
        </div>
      </div>

      {/* Audio Player Bar */}
      <div className="rp-audio-player">
        <audio
          ref={audioRef}
          src={audioUrl}
          onTimeUpdate={() => {
            if (audioRef.current) setCurrentTime(audioRef.current.currentTime);
          }}
          onLoadedMetadata={() => {
            if (audioRef.current && audioRef.current.duration) {
              setDuration(audioRef.current.duration);
            }
          }}
          onEnded={() => setIsPlaying(false)}
        />
        <button
          type="button"
          className="primary rp-audio-play-btn"
          onClick={togglePlay}
          aria-label={isPlaying ? 'Pause' : 'Play'}
          data-testid="audio-play-pause-btn"
        >
          {isPlaying ? <Pause size={16} /> : <Play size={16} />}
        </button>

        <div className="rp-audio-seek-wrap">
          <input
            type="range"
            min={0}
            max={duration > 0 ? duration : 1}
            step={0.1}
            value={currentTime}
            onChange={handleSeek}
            aria-label="Seek audio"
            data-testid="audio-seek-slider"
            className="rp-audio-seek"
          />
          <div className="rp-audio-time">
            <span>{formatTime(currentTime)}</span>
            <span>{formatTime(duration)}</span>
          </div>
        </div>

        {/* Speed options */}
        <div className="rp-speed-options">
          {SPEED_OPTIONS.map((s) => (
            <button
              key={s}
              type="button"
              data-testid={`speed-option-${s}`}
              className={`rp-speed-btn${speed === s ? ' active' : ''}`}
              onClick={() => handleSpeedChange(s)}
            >
              {s}x
            </button>
          ))}
        </div>
      </div>

      {/* Editable AI-Transcribed findings textarea */}
      <div>
        <label className="rp-dictation-transcript-label">
          AI-Transcribed Findings (Editable)
        </label>
        <textarea
          value={editedText}
          onChange={(e) => setEditedText(e.target.value)}
          placeholder="Dictation transcript will appear here..."
          rows={3}
          className="rp-dictation-transcript"
          aria-label="Editable AI Transcribed Findings"
          data-testid="transcribed-text-input"
        />
      </div>

      {/* Action Buttons */}
      <div className="rp-dictation-actions">
        <div className="rp-dictation-actions-left">
          <select
            value={selectedEngine}
            onChange={(e) => setSelectedEngine(e.target.value)}
            aria-label="Select transcription engine"
            data-testid="engine-select"
          >
            {engineOptions.map((eng) => (
              <option key={eng.engineId} value={eng.engineId} disabled={!eng.isAvailable}>
                {eng.displayName}
                {eng.isLocal ? ' (Local)' : ' (Cloud)'}
              </option>
            ))}
          </select>
          <button
            type="button"
            className="ghost"
            data-testid="retranscribe-btn"
            onClick={() => onRetranscribe?.(dictation.id, selectedEngine)}
          >
            <RefreshCw size={12} />
            Re-transcribe
          </button>
        </div>

        <button
          type="button"
          className="primary"
          data-testid="append-to-findings-btn"
          onClick={() => onAppendToFindings(editedText)}
        >
          <Check size={14} />
          Append to Main Findings
        </button>
      </div>
    </div>
  );
}
