'use client';

import { useState, useRef, useEffect } from 'react';
import { Play, Pause, Check, RefreshCw, Volume2 } from 'lucide-react';
import type { DictationAudioDto } from '@/lib/api/reportingClient';

export interface DictationAudioCardProps {
  dictation: DictationAudioDto;
  onAppendToFindings: (text: string) => void;
  onRetranscribe?: (dictationId: string, engine: string) => void;
}

const SPEED_OPTIONS = [1.0, 1.25, 1.5, 2.0];

export default function DictationAudioCard({
  dictation,
  onAppendToFindings,
  onRetranscribe,
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
    : `/api/v1/reporting/reports/${dictation.reportId}/dictations/${dictation.id}/audio`;

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

  return (
    <div className="rp-panel rp-dictation-card mb-4 p-4 rounded-lg bg-slate-900/80 border border-slate-800 shadow-md" data-testid="dictation-audio-card">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-slate-800 pb-2 mb-3">
        <div className="flex items-center gap-2">
          <Volume2 size={16} className="text-cyan-400" aria-hidden />
          <span className="font-semibold text-sm text-slate-200">Senior Radiologist Dictation</span>
        </div>
        <div className="flex items-center gap-2">
          <span className={`badge ${isMedAsr ? 'ok' : 'ai'} text-xs px-2 py-0.5 rounded`} data-testid="engine-badge">
            {engineBadgeLabel}
          </span>
          <span className="text-xs text-slate-400">
            {dictation.uploadedAt
              ? new Date(dictation.uploadedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
              : 'Just now'}
          </span>
        </div>
      </div>

      {/* Audio Player Bar */}
      <div className="rp-audio-player flex items-center gap-3 bg-slate-950/70 p-2.5 rounded-md border border-slate-800/80 mb-3">
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
          className="primary p-2 rounded-full flex items-center justify-center"
          onClick={togglePlay}
          aria-label={isPlaying ? 'Pause' : 'Play'}
          data-testid="audio-play-pause-btn"
        >
          {isPlaying ? <Pause size={16} /> : <Play size={16} />}
        </button>

        <div className="flex-1 flex flex-col justify-center">
          <input
            type="range"
            min={0}
            max={duration > 0 ? duration : 1}
            step={0.1}
            value={currentTime}
            onChange={handleSeek}
            aria-label="Seek audio"
            data-testid="audio-seek-slider"
            className="rp-input-range w-full cursor-pointer accent-cyan-500 h-1.5 bg-slate-700 rounded-lg"
          />
          <div className="flex justify-between text-xs text-slate-400 mt-1">
            <span>{formatTime(currentTime)}</span>
            <span>{formatTime(duration)}</span>
          </div>
        </div>

        {/* Speed options */}
        <div className="flex gap-1 bg-slate-900 p-1 rounded border border-slate-800">
          {SPEED_OPTIONS.map((s) => (
            <button
              key={s}
              type="button"
              data-testid={`speed-option-${s}`}
              className={`text-xs px-1.5 py-0.5 rounded ${
                speed === s ? 'bg-cyan-600 text-white font-bold' : 'text-slate-400 hover:text-slate-200'
              }`}
              onClick={() => handleSpeedChange(s)}
            >
              {s}x
            </button>
          ))}
        </div>
      </div>

      {/* Editable AI-Transcribed findings textarea */}
      <div className="mb-3">
        <label className="block text-xs font-medium text-slate-400 mb-1">
          AI-Transcribed Findings (Editable)
        </label>
        <textarea
          value={editedText}
          onChange={(e) => setEditedText(e.target.value)}
          placeholder="Dictation transcript will appear here..."
          rows={3}
          className="rp-input w-full p-2.5 bg-slate-950 border border-slate-800 rounded text-sm text-slate-100 font-mono focus:border-cyan-500 focus:outline-none"
          aria-label="Editable AI Transcribed Findings"
          data-testid="transcribed-text-input"
        />
      </div>

      {/* Action Buttons */}
      <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-slate-800/60">
        <div className="flex items-center gap-2">
          <select
            value={selectedEngine}
            onChange={(e) => setSelectedEngine(e.target.value)}
            className="rp-input text-xs bg-slate-950 border border-slate-800 text-slate-300 rounded px-2 py-1"
            aria-label="Select transcription engine"
            data-testid="engine-select"
          >
            <option value="medASR-6gram">medASR (Local 4.4% WER)</option>
            <option value="UBAG Gemini">UBAG Gemini (Cloud)</option>
          </select>
          <button
            type="button"
            className="ghost text-xs flex items-center gap-1.5 px-2.5 py-1.5"
            data-testid="retranscribe-btn"
            onClick={() => onRetranscribe?.(dictation.id, selectedEngine)}
          >
            <RefreshCw size={12} />
            Re-transcribe
          </button>
        </div>

        <button
          type="button"
          className="primary text-xs flex items-center gap-1.5 px-3 py-1.5 rounded font-medium"
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
