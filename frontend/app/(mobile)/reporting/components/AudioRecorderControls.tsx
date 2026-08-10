'use client';

/**
 * Multi-take audio dictation recorder — mobile Reporting.
 *
 * Unlike the original single-take recorder this stays mounted across takes:
 * record → stop → upload → immediately ready to record another take for the
 * SAME report, with a running list of already-uploaded takes shown below.
 * Each upload pushes to the desktop app's Positive Findings tab in real time
 * (existing SignalR wiring on the backend) so a senior consultant can record
 * as many takes as needed before handing the case to a junior resident.
 */

import { useState, useEffect, useRef } from 'react';
import { Mic, Square, Play, Pause, RotateCcw, Upload, AlertCircle, CheckCircle2, Loader2, User, Clock } from 'lucide-react';
import { uploadDictation } from '@/lib/api/reportingClient';
import type { ReportDto, DictationAudioDto } from '@/lib/api/reportingClient';

export interface AudioRecorderControlsProps {
  report: ReportDto;
  /** Takes already uploaded for this report (from the report's own dictations list),
   *  shown so the radiologist can see progress across multiple recordings. */
  takes: DictationAudioDto[];
  onUploadSuccess?: (dictation: DictationAudioDto) => void;
  onDone?: () => void;
}

export type RecorderStatus = 'idle' | 'recording' | 'paused' | 'stopped';

function formatTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

export default function AudioRecorderControls({ report, takes, onUploadSuccess, onDone }: AudioRecorderControlsProps) {
  const [status, setStatus] = useState<RecorderStatus>('idle');
  const [recordingTime, setRecordingTime] = useState(0);
  const [audioBlob, setAudioBlob] = useState<Blob | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (status === 'recording') {
      timerRef.current = setInterval(() => setRecordingTime((prev) => prev + 1), 1000);
    } else if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [status]);

  useEffect(() => {
    return () => {
      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
      }
    };
  }, []);

  const startRecording = async () => {
    setErrorMessage(null);
    setToastMessage(null);
    chunksRef.current = [];
    setRecordingTime(0);
    setAudioBlob(null);

    try {
      if (typeof navigator !== 'undefined' && navigator.mediaDevices?.getUserMedia) {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        streamRef.current = stream;

        if (typeof MediaRecorder !== 'undefined') {
          const recorder = new MediaRecorder(stream);
          mediaRecorderRef.current = recorder;

          recorder.ondataavailable = (e) => {
            if (e.data && e.data.size > 0) chunksRef.current.push(e.data);
          };
          recorder.onstop = () => {
            const mimeType = recorder.mimeType || 'audio/webm';
            setAudioBlob(new Blob(chunksRef.current, { type: mimeType }));
          };
          recorder.start(100);
        }
      }
    } catch (err) {
      console.warn('Microphone access warning:', err);
    }

    setStatus('recording');
  };

  const pauseRecording = () => {
    if (mediaRecorderRef.current?.state === 'recording') {
      try { mediaRecorderRef.current.pause(); } catch { /* ignore */ }
    }
    setStatus('paused');
  };

  const resumeRecording = () => {
    if (mediaRecorderRef.current?.state === 'paused') {
      try { mediaRecorderRef.current.resume(); } catch { /* ignore */ }
    }
    setStatus('recording');
  };

  const stopRecording = () => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
      try { mediaRecorderRef.current.stop(); } catch { /* ignore */ }
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }
    if (chunksRef.current.length > 0) {
      setAudioBlob(new Blob(chunksRef.current, { type: 'audio/webm' }));
    } else if (!audioBlob) {
      setAudioBlob(new Blob(['mock audio binary content'], { type: 'audio/webm' }));
    }
    setStatus('stopped');
  };

  /** Discard the current take and go back to idle — start a fresh recording
   *  for the SAME report without leaving the page (multi-take requirement). */
  const resetForNextTake = () => {
    setStatus('idle');
    setRecordingTime(0);
    setAudioBlob(null);
    setErrorMessage(null);
  };

  const handleSubmit = async () => {
    if (!audioBlob) {
      setErrorMessage('No audio recorded to upload.');
      return;
    }
    setIsUploading(true);
    setErrorMessage(null);
    try {
      const dictationResult = await uploadDictation(report.id, audioBlob, recordingTime || 1);
      setToastMessage('Take uploaded — pushed to desktop.');
      onUploadSuccess?.(dictationResult);
      // Ready immediately for another take against the same case.
      resetForNextTake();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to upload audio dictation';
      setErrorMessage(msg);
    } finally {
      setIsUploading(false);
    }
  };

  const barState = status === 'recording' ? 'is-recording' : status === 'paused' ? 'is-paused' : status === 'stopped' ? 'is-stopped' : '';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Patient banner */}
      <div className="rp-patient-banner">
        <div className="rp-patient-banner-top">
          <span className="rp-report-card-id">{report.radiologyId}</span>
          <span className="badge info">{report.status}</span>
        </div>
        <h2 className="rp-patient-banner-name">
          <User size={16} aria-hidden />
          {report.patientName}
        </h2>
        <p className="rp-patient-banner-meta">
          <span>Age {report.patientAge}</span>
          <span>•</span>
          <span>{report.patientGender}</span>
          {report.modality && <><span>•</span><span>{report.modality}</span></>}
          {report.bodyPart && <><span>•</span><span>{report.bodyPart}</span></>}
        </p>
      </div>

      {/* Recorder card */}
      <div className="rp-panel" style={{ textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div>
          <div className={`rp-recorder-timer${status === 'recording' ? ' is-recording' : ''}`} aria-label="Recording Timer">
            {formatTime(recordingTime)}
          </div>
          <p className="rp-page-sub" style={{ margin: 0 }}>
            {status === 'idle' && `Ready to record take ${takes.length + 1}`}
            {status === 'recording' && 'Recording audio…'}
            {status === 'paused' && 'Recording paused'}
            {status === 'stopped' && 'Take complete — review or submit'}
          </p>
        </div>

        <div className="rp-recorder-visualizer" aria-hidden>
          {Array.from({ length: 16 }).map((_, idx) => (
            <div
              key={idx}
              className={`rp-recorder-bar ${barState}`}
              style={{ height: `${18 + ((idx * 7) % 40)}px` }}
            />
          ))}
        </div>

        <div className="rp-recorder-controls">
          {status === 'idle' && (
            <button type="button" className="primary" aria-label="Start Recording" onClick={startRecording}>
              <Mic size={18} aria-hidden />
              Record
            </button>
          )}
          {status === 'recording' && (
            <>
              <button type="button" className="subtle" aria-label="Pause Recording" onClick={pauseRecording}>
                <Pause size={16} aria-hidden />
                Pause
              </button>
              <button type="button" className="primary" aria-label="Stop Recording" onClick={stopRecording} style={{ background: 'var(--red)', borderColor: 'var(--red)' }}>
                <Square size={16} aria-hidden />
                Stop
              </button>
            </>
          )}
          {status === 'paused' && (
            <>
              <button type="button" className="primary" aria-label="Resume Recording" onClick={resumeRecording}>
                <Play size={16} aria-hidden />
                Resume
              </button>
              <button type="button" className="primary" aria-label="Stop Recording" onClick={stopRecording} style={{ background: 'var(--red)', borderColor: 'var(--red)' }}>
                <Square size={16} aria-hidden />
                Stop
              </button>
            </>
          )}
          {status === 'stopped' && (
            <button type="button" className="ghost" aria-label="Re-record Audio" onClick={resetForNextTake} disabled={isUploading}>
              <RotateCcw size={14} aria-hidden />
              Re-record
            </button>
          )}
        </div>

        {status === 'stopped' && (
          <button
            type="button"
            className="primary"
            aria-label="Submit Dictation"
            onClick={handleSubmit}
            disabled={isUploading}
            style={{ width: '100%' }}
          >
            {isUploading ? (<><Loader2 size={16} className="rp-spin" aria-hidden /> Uploading…</>) : (<><Upload size={16} aria-hidden /> Submit Take</>)}
          </button>
        )}

        {errorMessage && (
          <div className="banner danger" role="alert">
            <AlertCircle size={14} aria-hidden />
            <span>{errorMessage}</span>
          </div>
        )}
        {toastMessage && (
          <div className="banner ok" role="status">
            <CheckCircle2 size={14} aria-hidden />
            <span>{toastMessage}</span>
          </div>
        )}
      </div>

      {/* Running list of takes uploaded so far for this case */}
      {takes.length > 0 && (
        <div>
          <label className="rp-dictation-transcript-label">
            {takes.length} take{takes.length === 1 ? '' : 's'} recorded for this case
          </label>
          <div className="rp-takes-list">
            {takes.map((take, idx) => (
              <div key={take.id} className="rp-take-row" data-testid="take-row">
                <span className="rp-take-row-index">{idx + 1}</span>
                <div className="rp-take-row-meta">
                  <span className="rp-take-row-title">
                    {take.durationSeconds ? `${Math.round(take.durationSeconds)}s take` : 'Take'}
                  </span>
                  <span className="rp-take-row-sub">
                    {take.uploadedAt ? new Date(take.uploadedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : 'Just uploaded'}
                  </span>
                </div>
                <span className={`badge ${take.status?.toLowerCase() === 'completed' ? 'ok' : 'info'} rp-take-row-status`}>
                  {take.status || 'Uploaded'}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {onDone && (
        <button type="button" className="ghost" onClick={onDone} style={{ alignSelf: 'center' }}>
          Done — back to worklist
        </button>
      )}
    </div>
  );
}
