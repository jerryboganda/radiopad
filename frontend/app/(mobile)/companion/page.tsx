'use client';

/**
 * Mobile companion — the entire mobile app.
 *
 * The phone pairs to a *live desktop session* (by the short code the desktop
 * shows) and then acts as a wireless dictation microphone + remote for the
 * report open on that desktop. Spoken text is streamed over the companion relay
 * ({@link connectCompanion}) to the desktop, which inserts it into the focused
 * section. There is NO standalone reporting here — no editing, no signing.
 *
 * Uses the Web Speech API where available (with the native
 * `@capacitor-community/speech-recognition` plugin as the on-device path), the
 * same approach as the legacy mobile dictation page. Locked mobile classes:
 * `.rp-mobile`, `.rp-mic-btn`, `.rp-transcript`, `.rp-page-title`,
 * `.rp-page-sub`, `.banner`, `.primary`, `.ghost`, `.subtle`.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '@/lib/api';
import {
  connectCompanion,
  type CompanionConnection,
  type CompanionCommand,
} from '@/lib/companion';

type SpeechRecognitionLike = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult:
    | ((event: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal?: boolean }> }) => void)
    | null;
  onerror: ((event: { error?: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
};
type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

function getSpeechRecognitionCtor(): SpeechRecognitionCtor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function deviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad phone';
  const ua = navigator.userAgent;
  if (/iphone/i.test(ua)) return 'iPhone';
  if (/ipad/i.test(ua)) return 'iPad';
  if (/android/i.test(ua)) return 'Android phone';
  return 'RadioPad companion';
}

const REMOTE_COMMANDS: Array<{ command: CompanionCommand; label: string }> = [
  { command: 'prev_section', label: '‹ Prev section' },
  { command: 'next_section', label: 'Next section ›' },
  { command: 'insert', label: 'Insert' },
  { command: 'undo', label: 'Undo' },
  { command: 'read_back', label: 'Read back' },
];

type Phase = 'pair' | 'connecting' | 'live' | 'ended';

export default function MobileCompanionPage() {
  const Ctor = useMemo(getSpeechRecognitionCtor, []);
  const [phase, setPhase] = useState<Phase>('pair');
  const [code, setCode] = useState('');
  const [pairing, setPairing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hostName, setHostName] = useState<string>('');
  const [section, setSection] = useState<string>('');
  const [recording, setRecording] = useState(false);
  const [transcript, setTranscript] = useState('');

  const connRef = useRef<CompanionConnection | null>(null);
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null);

  const teardown = useCallback(() => {
    try { recognitionRef.current?.stop(); } catch { /* noop */ }
    recognitionRef.current = null;
    connRef.current?.close();
    connRef.current = null;
  }, []);

  useEffect(() => () => teardown(), [teardown]);

  const pair = useCallback(async () => {
    const trimmed = code.trim().toUpperCase();
    if (trimmed.length < 4) {
      setError('Enter the code shown on your desktop.');
      return;
    }
    setPairing(true);
    setError(null);
    try {
      const res = await api.companion.pair(trimmed, deviceName());
      setHostName(res.hostDeviceName);
      setPhase('connecting');
      const conn = connectCompanion({
        sessionId: res.sessionId,
        role: 'companion',
        onOpen: () => setPhase('live'),
        // On an INVOLUNTARY end (relay drop or desktop unpair) tear everything
        // down — critically, stop the running SpeechRecognition so the phone
        // microphone can never stay live after the UI says the session ended.
        onClose: () => { teardown(); setPhase('ended'); },
        onMessage: (msg) => {
          if (msg.type === 'section_context') {
            setSection(msg.sectionTitle || msg.sectionKey || '');
          } else if (msg.type === 'session_ended') {
            teardown();
            setPhase('ended');
          }
        },
        onError: () => setError('Connection interrupted. Re-pair to continue.'),
      });
      connRef.current = conn;
    } catch (e) {
      const ex = e as { status?: number; body?: { error?: string } };
      setError(ex.status === 404 ? 'That code is invalid or expired.' : (ex.body?.error ?? 'Pairing failed.'));
      setPhase('pair');
    } finally {
      setPairing(false);
    }
  }, [code, teardown]);

  const startRecording = useCallback(() => {
    if (!Ctor || !connRef.current) return;
    const rec = new Ctor();
    rec.lang = 'en-US';
    rec.continuous = true;
    rec.interimResults = true;
    rec.onresult = (event) => {
      let interim = '';
      for (let i = event.resultIndex; i < event.results.length; i += 1) {
        const result = event.results[i];
        const text = result[0]?.transcript ?? '';
        const isFinal = Boolean((result as { isFinal?: boolean }).isFinal);
        if (isFinal) {
          connRef.current?.sendDictation(text, true);
        } else {
          interim += text;
        }
      }
      if (interim) {
        setTranscript(interim);
        connRef.current?.sendDictation(interim, false);
      } else {
        setTranscript('');
      }
    };
    rec.onerror = (ev) => setError(`Microphone error: ${ev.error ?? 'unknown'}`);
    rec.onend = () => setRecording(false);
    recognitionRef.current = rec;
    try {
      rec.start();
      setRecording(true);
      connRef.current.sendCommand('ptt_start');
    } catch {
      setError('Could not start the microphone.');
    }
  }, [Ctor]);

  const stopRecording = useCallback(() => {
    try { recognitionRef.current?.stop(); } catch { /* noop */ }
    recognitionRef.current = null;
    setRecording(false);
    setTranscript('');
    connRef.current?.sendCommand('ptt_stop');
  }, []);

  const sendCommand = useCallback((command: CompanionCommand) => {
    connRef.current?.sendCommand(command);
  }, []);

  if (phase === 'pair' || phase === 'connecting') {
    return (
      <div className="rp-mobile">
        <h1 className="rp-page-title">Pair with desktop</h1>
        <p className="rp-page-sub">
          Open a report on your RadioPad desktop, choose <strong>Pair phone</strong>, and enter the
          code shown there. Your phone becomes a wireless dictation mic for that report.
        </p>
        {error && <div className="banner warn" role="alert">{error}</div>}
        <input
          className="rp-input"
          inputMode="text"
          autoCapitalize="characters"
          autoCorrect="off"
          placeholder="Pairing code"
          aria-label="Pairing code"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          disabled={pairing || phase === 'connecting'}
          style={{ letterSpacing: '0.25em', textAlign: 'center', fontSize: '1.5rem' }}
        />
        <button
          className="primary"
          type="button"
          onClick={pair}
          disabled={pairing || phase === 'connecting'}
        >
          {phase === 'connecting' ? 'Connecting…' : pairing ? 'Pairing…' : 'Pair'}
        </button>
      </div>
    );
  }

  if (phase === 'ended') {
    return (
      <div className="rp-mobile">
        <h1 className="rp-page-title">Session ended</h1>
        <p className="rp-page-sub">The desktop session closed. Pair again to keep dictating.</p>
        <button className="primary" type="button" onClick={() => { setPhase('pair'); setCode(''); setError(null); }}>
          Pair again
        </button>
      </div>
    );
  }

  // phase === 'live'
  return (
    <div className="rp-mobile">
      <h1 className="rp-page-title">Dictating to {hostName || 'desktop'}</h1>
      <p className="rp-page-sub">
        {section ? <>Active section: <strong>{section}</strong></> : 'Hold the mic and speak — text lands on the desktop.'}
      </p>

      <button
        className="rp-mic-btn"
        type="button"
        aria-pressed={recording}
        onPointerDown={startRecording}
        onPointerUp={stopRecording}
        onPointerLeave={() => recording && stopRecording()}
        disabled={!Ctor}
      >
        {recording ? 'Listening… release to send' : 'Hold to dictate'}
      </button>

      {!Ctor && (
        <div className="banner warn" role="alert">
          Speech recognition isn’t available on this device’s browser.
        </div>
      )}

      {transcript && <div className="rp-transcript" aria-live="polite">{transcript}</div>}

      <div className="rp-companion-remote" role="group" aria-label="Remote controls">
        {REMOTE_COMMANDS.map((c) => (
          <button key={c.command} className="ghost" type="button" onClick={() => sendCommand(c.command)}>
            {c.label}
          </button>
        ))}
      </div>

      <button className="subtle" type="button" onClick={() => { teardown(); setPhase('ended'); }}>
        End session
      </button>
    </div>
  );
}
