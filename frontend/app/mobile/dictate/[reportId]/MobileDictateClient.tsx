'use client';

// Iter-36 MOB — full-screen dictation page. Uses the Web Speech API
// (`window.SpeechRecognition || window.webkitSpeechRecognition`) when
// available, with a graceful fallback message otherwise. Transcript is
// kept in `localStorage` keyed by `reportId` so a network drop never
// loses the radiologist's spoken notes. Save calls
// `api.reports.appendFindings(reportId, transcript)` which preserves
// the existing findings prose.
//
// Locked design system: `.rp-mobile`, `.rp-mic-btn`, `.rp-transcript`,
// `.rp-page-title`, `.rp-page-sub`, `.banner.warn`, `.primary`,
// `.subtle`. No inline colour/border styles.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import { mobileReportEditHref } from '@/lib/routes';
import { detectCommand, stripCommand, type VoiceCommand } from '@/lib/voiceCommands';

type SpeechRecognitionLike = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult:
    | ((event: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string }>> }) => void)
    | null;
  onerror: ((event: { error?: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
};

type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

type NativeSpeechApi = {
  available?: () => Promise<{ available?: boolean }>;
  requestPermissions?: () => Promise<unknown>;
  addListener?: (
    eventName: 'partialResults',
    listenerFunc: (event: { matches?: string[] }) => void,
  ) => Promise<{ remove?: () => Promise<void> | void }>;
  start: (options: {
    language?: string;
    maxResults?: number;
    partialResults?: boolean;
    popup?: boolean;
    prompt?: string;
  }) => Promise<{ matches?: string[] } | void>;
  stop?: () => Promise<void>;
};

function getSpeechRecognitionCtor(): SpeechRecognitionCtor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function draftKey(reportId: string): string {
  return `radiopad.mobile.dictate.${reportId}`;
}

export default function MobileDictatePage() {
  const router = useRouter();
  const [reportId, setReportId] = useState<string | null>(null);

  const Ctor = useMemo(getSpeechRecognitionCtor, []);
  const [transcript, setTranscript] = useState('');
  const [recording, setRecording] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [nativeSpeech, setNativeSpeech] = useState<NativeSpeechApi | null>(null);
  const [commandBanner, setCommandBanner] = useState<string | null>(null);
  const [activeCommand, setActiveCommand] = useState<VoiceCommand | null>(null);
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null);
  const nativeListenerRef = useRef<{ remove?: () => Promise<void> | void } | null>(null);

  const supported = Ctor !== null || nativeSpeech !== null;

  const COMMAND_LABELS: Record<VoiceCommand, string> = {
    generate_impression: 'Generating impression…',
    make_concise: 'Rewriting concise…',
    make_formal: 'Rewriting formal…',
    patient_friendly: 'Rewriting patient-friendly…',
    validate_report: 'Validating report…',
    cleanup_dictation: 'Cleaning up dictation…',
  };

  const executeVoiceCommand = useCallback(async (command: VoiceCommand, rid: string) => {
    setActiveCommand(command);
    setCommandBanner(COMMAND_LABELS[command]);
    try {
      switch (command) {
        case 'generate_impression':
          await api.reports.runAi(rid, { mode: 'impression', providerId: '' });
          break;
        case 'make_concise':
          await api.reports.rewrite(rid, { mode: 'concise' });
          break;
        case 'make_formal':
          await api.reports.rewrite(rid, { mode: 'formal' });
          break;
        case 'patient_friendly':
          await api.reports.rewrite(rid, { mode: 'patient_friendly' });
          break;
        case 'validate_report':
          await api.reports.validate(rid);
          break;
        case 'cleanup_dictation': {
          const current = await api.reports.get(rid);
          await api.reports.cleanupDictation(rid, current.findings || '');
          break;
        }
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Command failed');
    } finally {
      setActiveCommand(null);
      setTimeout(() => setCommandBanner(null), 3000);
    }
  }, []);

  /** Process a new speech chunk: detect commands or append to transcript. */
  const processChunk = useCallback((chunk: string) => {
    if (!chunk) return;
    setTranscript((prev) => {
      const full = prev ? `${prev} ${chunk}` : chunk;
      const match = detectCommand(full);
      if (match && reportId) {
        const stripped = stripCommand(full, match);
        void executeVoiceCommand(match.command, reportId);
        return stripped;
      }
      return full;
    });
  }, [reportId, executeVoiceCommand]);

  useEffect(() => {
    setReportId(readQueryParam('reportId'));
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const mod: any = await import('@capacitor-community/speech-recognition').catch(() => null);
      const SpeechRecognition = mod?.SpeechRecognition as NativeSpeechApi | undefined;
      if (!SpeechRecognition?.start || cancelled) return;
      try {
        const available = await SpeechRecognition.available?.();
        if (available && available.available === false) return;
      } catch {
        /* availability probing is best-effort */
      }
      if (!cancelled) setNativeSpeech(SpeechRecognition);
    })();
    return () => { cancelled = true; };
  }, []);

  // Restore offline draft so a network drop never costs a dictation.
  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (!reportId) return;
    const cached = window.localStorage.getItem(draftKey(reportId));
    if (cached) setTranscript(cached);
  }, [reportId]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (!reportId) return;
    if (transcript) window.localStorage.setItem(draftKey(reportId), transcript);
  }, [reportId, transcript]);

  const start = useCallback(async () => {
    if (!Ctor && !nativeSpeech) return;
    setError(null);
    setSaved(false);
    if (Ctor) {
      const rec = new Ctor();
      rec.lang = 'en-US';
      rec.continuous = true;
      rec.interimResults = false;
      rec.onresult = (event) => {
        let chunk = '';
        for (let i = event.resultIndex; i < event.results.length; i += 1) {
          const r = event.results[i];
          if (r && r[0]) chunk += r[0].transcript;
        }
        if (chunk) processChunk(chunk);
      };
      rec.onerror = (e) => {
        setError(e.error ? `Speech error: ${e.error}` : 'Speech error');
        setRecording(false);
      };
      rec.onend = () => setRecording(false);
      recognitionRef.current = rec;
      try {
        rec.start();
        setRecording(true);
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Could not start recording');
        setRecording(false);
      }
      return;
    }

    try {
      await nativeSpeech?.requestPermissions?.();
      setRecording(true);
      const result = await nativeSpeech?.start({
        language: 'en-US',
        maxResults: 1,
        partialResults: false,
        popup: false,
        prompt: 'Dictate findings',
      });
      const chunk = result?.matches?.filter(Boolean).join(' ').trim();
      if (chunk) processChunk(chunk);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not start recording');
    } finally {
      setRecording(false);
      await nativeListenerRef.current?.remove?.();
      nativeListenerRef.current = null;
    }
  }, [Ctor, nativeSpeech, processChunk]);

  const stop = useCallback(async () => {
    recognitionRef.current?.stop();
    try { await nativeSpeech?.stop?.(); } catch { /* ignore */ }
    await nativeListenerRef.current?.remove?.();
    nativeListenerRef.current = null;
    setRecording(false);
  }, [nativeSpeech]);

  const onSave = useCallback(async () => {
    if (!reportId) { setError('Missing report id.'); return; }
    if (!transcript.trim()) return;
    setSaving(true);
    setError(null);
    try {
      await api.reports.appendFindings(reportId, transcript);
      if (typeof window !== 'undefined') window.localStorage.removeItem(draftKey(reportId));
      setSaved(true);
      setTranscript('');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save dictation');
    } finally {
      setSaving(false);
    }
  }, [reportId, transcript]);

  return (
    <section className="rp-mobile" aria-label="Dictate findings">
      <h1 className="rp-page-title">Dictate findings</h1>
      <p className="rp-page-sub">
        Speak naturally — RadioPad appends your transcript to the report&apos;s Findings
        section when you save.
      </p>

      {!supported && (
        <div className="banner warn" role="status">
          Speech recognition is not available in this browser. On iOS, open the page in
          Safari and grant microphone access; on Android Chrome / Capacitor it works
          out of the box. You can still type into the report editor.
        </div>
      )}

      {error && (
        <div className="banner danger" role="alert">
          {error}
        </div>
      )}

      {reportId === '' && (
        <div className="banner warn" role="alert">
          Missing report id.
        </div>
      )}

      {saved && (
        <div className="banner info" role="status">
          Saved to Findings.
        </div>
      )}

      {activeCommand && (
        <span className="badge" data-testid="voice-command-badge">{activeCommand}</span>
      )}

      {commandBanner && (
        <div className="banner ok" role="status" data-testid="voice-command-banner">
          Command detected: {commandBanner}
        </div>
      )}

      <button
        type="button"
        className={`rp-mic-btn${recording ? ' recording' : ''}`}
        onClick={() => { void (recording ? stop() : start()); }}
        disabled={!supported}
        aria-pressed={recording}
        aria-label={recording ? 'Stop recording' : 'Start recording'}
        data-testid="mic-btn"
      >
        {recording ? 'Stop recording' : 'Tap to record'}
      </button>

      <div
        className="rp-transcript"
        data-empty={transcript ? 'false' : 'true'}
        data-testid="transcript"
        aria-live="polite"
      >
        {transcript || 'Your transcript will appear here.'}
      </div>

      <div className="rp-row between">
        <button
          type="button"
          className="subtle"
          onClick={() => {
            setTranscript('');
            if (typeof window !== 'undefined' && reportId) window.localStorage.removeItem(draftKey(reportId));
          }}
          disabled={!transcript || saving}
        >
          Clear
        </button>
        <div className="rp-row rp-gap-sm">
          <button type="button" className="ghost" onClick={() => { if (reportId) router.push(mobileReportEditHref(reportId)); }}>
            Open editor
          </button>
          <button
            type="button"
            className="primary"
            onClick={onSave}
            disabled={!transcript.trim() || saving}
            data-testid="save-btn"
          >
            {saving ? 'Saving…' : 'Save as Findings'}
          </button>
        </div>
      </div>
    </section>
  );
}
