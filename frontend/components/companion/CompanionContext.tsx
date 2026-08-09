'use client';

/**
 * Global Companion Provider.
 *
 * Hosts the desktop companion session lifecycle at the application shell level
 * so that radiologists can pair their phone/tablet upfront from the dedicated
 * Device Pairing page, navigate across worklist, patient records, and composer,
 * and maintain an uninterrupted low-latency microphone and remote connection.
 */

import type { ReactNode } from 'react';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import QRCode from 'qrcode';
import { api, companionBase } from '@/lib/api';
import { encodeCompanionPairing } from '@/lib/companionPairing';
import {
  connectCompanion,
  type CompanionConnection,
  type CompanionCommand,
} from '@/lib/companion';
import { createRtcPeer, type RtcPeer } from '@/lib/companionRtc';
import { createAudioReceiver, type AudioReceiver } from '@/lib/companionAudioReceiver';
import { blobToWav16kMono } from '@/lib/dictation/wavEncode';
import { formatDictation } from '@/lib/dictation/medicalFormat';
import { getSttMode } from '@/lib/dictation/sttMode';
import { raceTimeout } from '@/lib/asyncTimeout';
import { readQueryParam } from '@/lib/browserParams';
import {
  focusAdjacentSection,
  getLastFocusedSectionEditor,
  getSectionEditor,
  getSectionEditorsInOrder,
} from '@/lib/editor/sectionEditorRegistry';

export type CompanionPhase = 'idle' | 'advertising' | 'paired' | 'error';
export type CompanionLinkState = 'idle' | 'connecting' | 'connected' | 'failed';

export interface CompanionContextType {
  phase: CompanionPhase;
  link: CompanionLinkState;
  sessionId: string | null;
  pairingCode: string | null;
  pairingPayload: string | null;
  qrDataUrl: string | null;
  companionDeviceName: string | null;
  phoneListening: boolean;
  transcribing: boolean;
  slowTranscribe: boolean;
  error: string | null;
  lastCommand: CompanionCommand | null;
  lastTranscript: string | null;
  startPairing: () => Promise<void>;
  unpair: () => Promise<void>;
  retryRtc: () => void;
  clearError: () => void;
}

const DECODE_TIMEOUT_MS = 20_000;
const TRANSCRIBE_TIMEOUT_MS = 60_000;
const SLOW_TRANSCRIBE_HINT_MS = 8_000;

function hostDeviceName(): string {
  if (typeof navigator === 'undefined') return 'RadioPad desktop';
  if (/mac/i.test(navigator.platform)) return 'Mac desktop';
  if (/win/i.test(navigator.platform)) return 'Windows desktop';
  return 'RadioPad desktop';
}

const CompanionContext = createContext<CompanionContextType | null>(null);

export function CompanionProvider({ children }: { children: ReactNode }) {
  const [phase, setPhase] = useState<CompanionPhase>('idle');
  const [link, setLink] = useState<CompanionLinkState>('idle');
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [pairingCode, setPairingCode] = useState<string | null>(null);
  const [pairingPayload, setPairingPayload] = useState<string | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [companionDeviceName, setCompanionDeviceName] = useState<string | null>(null);
  const [phoneListening, setPhoneListening] = useState(false);
  const [transcribing, setTranscribing] = useState(false);
  const [slowTranscribe, setSlowTranscribe] = useState(false);
  const [phraseSeq, setPhraseSeq] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [lastCommand, setLastCommand] = useState<CompanionCommand | null>(null);
  const [lastTranscript, setLastTranscript] = useState<string | null>(null);

  const connRef = useRef<CompanionConnection | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const rtcRef = useRef<RtcPeer | null>(null);
  const receiverRef = useRef<AudioReceiver | null>(null);
  const transcribeAbortRef = useRef<AbortController | null>(null);
  const interimTargetRef = useRef<string | null>(null);

  const sendSectionContext = useCallback(() => {
    const current = getLastFocusedSectionEditor();
    if (current) connRef.current?.sendSectionContext(current.sectionKey, current.sectionKey);
  }, []);

  const clearInterimEverywhere = useCallback(() => {
    const prev = interimTargetRef.current;
    if (prev) getSectionEditor(prev)?.clearInterim?.();
    getLastFocusedSectionEditor()?.clearInterim?.();
    interimTargetRef.current = null;
  }, []);

  const stopRtc = useCallback(() => {
    rtcRef.current?.close();
    rtcRef.current = null;
    receiverRef.current?.reset();
    receiverRef.current = null;
    transcribeAbortRef.current?.abort();
    transcribeAbortRef.current = null;
    setLink('idle');
    setTranscribing(false);
  }, []);

  useEffect(() => {
    setSlowTranscribe(false);
    if (!transcribing) return undefined;
    const t = setTimeout(() => setSlowTranscribe(true), SLOW_TRANSCRIBE_HINT_MS);
    return () => clearTimeout(t);
  }, [transcribing, phraseSeq]);

  const checkEngineReady = useCallback(async () => {
    try {
      const peer = rtcRef.current;
      const res = await api.localModels.list();
      if (rtcRef.current !== peer || !peer) return;
      if (!res.enabled) return;
      const blobCapable = res.models.filter(
        (m) => m.kind === 'Stt' && !m.placeholder && m.provisioning !== 'BrowserWebSpeech',
      );
      if (blobCapable.length > 0 && !blobCapable.some((m) => m.available)) {
        const downloading = blobCapable.some(
          (m) =>
            m.progress?.state === 'Downloading' ||
            m.progress?.state === 'Verifying' ||
            m.progress?.state === 'Extracting' ||
            m.progress?.state === 'Installing',
        );
        setError(
          downloading
            ? 'The on-device speech model is still downloading — phone phrases will transcribe once it finishes (watch Settings → On-device models). Type mode on the phone works right now.'
            : 'Phone dictation needs the on-device speech engine. Open Settings → On-device models and download the speech model — or use Type mode on the phone meanwhile.',
        );
      }
    } catch {
      /* Sidecar probe fallback handled per phrase */
    }
  }, []);

  const startRtc = useCallback(() => {
    stopRtc();
    const receiver = createAudioReceiver({
      transcribe: async (webm) => {
        let wav: Blob;
        try {
          wav = await raceTimeout(blobToWav16kMono(webm), DECODE_TIMEOUT_MS, 'decode timed out');
        } catch {
          wav = webm;
        }
        if (receiverRef.current !== receiver) throw new Error('Session ended.');
        const reportId = readQueryParam('id') ?? '';
        const ctrl = new AbortController();
        transcribeAbortRef.current = ctrl;
        const timer = setTimeout(() => ctrl.abort(), TRANSCRIBE_TIMEOUT_MS);
        try {
          const res = await raceTimeout(
            api.reports.transcribe(reportId, wav, getSttMode(), ctrl.signal),
            TRANSCRIBE_TIMEOUT_MS + 5_000,
            'The speech engine timed out.',
          );
          return formatDictation(res.transcript ?? '');
        } catch (e) {
          if (ctrl.signal.aborted) {
            throw new Error('The speech engine timed out on a phrase — skipped it. It may still be warming up; keep dictating.');
          }
          const ex = e as { status?: number; body?: { error?: string; kind?: string }; message?: string };
          if (ex.status === 503 || ex.body?.kind === 'stt_unavailable') {
            throw new Error('The on-device speech engine isn’t ready — its model may still be downloading. Check Settings → On-device models, or use Type mode on the phone.');
          }
          if (e instanceof TypeError) {
            throw new Error('The on-device speech engine is still starting up — keep dictating, phrases will resume shortly.');
          }
          throw new Error(ex.body?.error ?? ex.message ?? 'Could not transcribe a phrase.');
        } finally {
          clearTimeout(timer);
          if (transcribeAbortRef.current === ctrl) transcribeAbortRef.current = null;
        }
      },
      insert: (text) => {
        if (!text) return;
        setLastTranscript(text);
        const target =
          getLastFocusedSectionEditor() ??
          getSectionEditor('findings') ??
          getSectionEditorsInOrder()[0];
        target?.insertAtCursor(text);
        setError(null);
      },
      onBusyChange: (busy) => {
        setTranscribing(busy);
        if (busy) setPhraseSeq((n) => n + 1);
      },
      onError: (m) => setError(m),
    });
    receiverRef.current = receiver;
    setLink('connecting');
    const peer = createRtcPeer({
      role: 'host',
      sendSignal: (s) => connRef.current?.sendSignal(s),
      onSegment: (blob, seq) => receiver.pushSegment(blob, seq),
      onState: (state) => {
        if (state === 'connected') {
          setLink('connected');
          void checkEngineReady();
        } else if (state === 'failed') setLink('failed');
      },
      onFailed: () => setLink('failed'),
    });
    rtcRef.current = peer;
    void peer.startAsHost();
  }, [stopRtc, checkEngineReady]);

  const handleCommand = useCallback(
    (command: CompanionCommand) => {
      setLastCommand(command);
      const isNav =
        command === 'next_section' ||
        command === 'prev_section' ||
        command === 'jump_findings' ||
        command === 'jump_impression';
      if (isNav) clearInterimEverywhere();

      switch (command) {
        case 'next_section':
          focusAdjacentSection(1);
          break;
        case 'prev_section':
          focusAdjacentSection(-1);
          break;
        case 'jump_findings':
          getSectionEditor('findings')?.focus();
          break;
        case 'jump_impression':
          getSectionEditor('impression')?.focus();
          break;
        case 'new_line':
          getLastFocusedSectionEditor()?.newLine?.();
          break;
        case 'undo':
          getLastFocusedSectionEditor()?.undo?.();
          break;
        case 'generate_impression':
          if (typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('radiopad:generate-impression'));
          }
          break;
        default:
          break;
      }
      sendSectionContext();
    },
    [sendSectionContext, clearInterimEverywhere],
  );

  const teardown = useCallback(() => {
    stopRtc();
    connRef.current?.close();
    connRef.current = null;
    clearInterimEverywhere();
    const id = sessionIdRef.current;
    sessionIdRef.current = null;
    if (id && api.companion?.endSession) {
      try {
        void Promise.resolve(api.companion.endSession(id)).catch(() => undefined);
      } catch {
        /* ignore */
      }
    }
  }, [clearInterimEverywhere, stopRtc]);

  useEffect(() => () => teardown(), [teardown]);

  const startPairing = useCallback(async () => {
    setError(null);
    setCompanionDeviceName(null);
    setPhase('advertising');
    try {
      const session = await api.companion.createSession(hostDeviceName());
      sessionIdRef.current = session.sessionId;
      setSessionId(session.sessionId);
      setPairingCode(session.pairingCode);
      try {
        const payload = session.companionToken
          ? encodeCompanionPairing({
              base: companionBase(),
              code: session.pairingCode,
              token: session.companionToken,
              tenant: session.tenantSlug ?? '',
              user: session.userEmail ?? '',
            })
          : session.pairingCode;
        setPairingPayload(payload);
        setQrDataUrl(
          await QRCode.toDataURL(payload, { margin: 1, width: 240, errorCorrectionLevel: 'M' }),
        );
      } catch {
        setQrDataUrl(null);
      }

      const conn = connectCompanion({
        sessionId: session.sessionId,
        role: 'host',
        onMessage: (msg) => {
          if (msg.type === 'dictation') {
            const resolved =
              getLastFocusedSectionEditor() ??
              getSectionEditor('findings') ??
              getSectionEditorsInOrder()[0] ??
              null;
            const targetKey = resolved?.sectionKey ?? null;
            const prevKey = interimTargetRef.current;
            if (prevKey && prevKey !== targetKey) getSectionEditor(prevKey)?.clearInterim?.();
            if (msg.isFinal) {
              if (msg.text) {
                resolved?.insertAtCursor(msg.text);
                setLastTranscript(msg.text);
              }
              resolved?.clearInterim?.();
              interimTargetRef.current = null;
            } else {
              resolved?.setInterim?.(msg.text);
              interimTargetRef.current = targetKey;
            }
          } else if (
            msg.type === 'rtc_answer' ||
            msg.type === 'rtc_ice' ||
            msg.type === 'rtc_offer' ||
            msg.type === 'rtc_bye'
          ) {
            void rtcRef.current?.handleSignal(msg);
          } else if (msg.type === 'command') {
            if (msg.command === 'ptt_start') {
              setPhoneListening(true);
              return;
            }
            if (msg.command === 'ptt_stop') {
              setPhoneListening(false);
              clearInterimEverywhere();
              return;
            }
            handleCommand(msg.command);
          } else if (msg.type === 'peer_joined') {
            setCompanionDeviceName(msg.deviceName || 'Phone companion');
            setPhase('paired');
            sendSectionContext();
            startRtc();
          } else if (msg.type === 'peer_left') {
            setPhase('advertising');
            setCompanionDeviceName(null);
            setPhoneListening(false);
            clearInterimEverywhere();
            stopRtc();
          } else if (msg.type === 'session_ended') {
            setPhase('idle');
            setPhoneListening(false);
            clearInterimEverywhere();
            stopRtc();
          }
        },
        onError: () => setError('Companion relay unreachable.'),
        onClose: () => setPhase('idle'),
      });
      connRef.current = conn;
    } catch (e) {
      const ex = e as { body?: { error?: string }; message?: string };
      setError(ex.body?.error ?? ex.message ?? 'Could not start pairing.');
      setPhase('error');
    }
  }, [handleCommand, sendSectionContext, clearInterimEverywhere, startRtc, stopRtc]);

  const unpair = useCallback(async () => {
    teardown();
    setPhase('idle');
    setSessionId(null);
    setPairingCode(null);
    setPairingPayload(null);
    setQrDataUrl(null);
    setCompanionDeviceName(null);
    setPhoneListening(false);
    setTranscribing(false);
    setSlowTranscribe(false);
    setError(null);
  }, [teardown]);

  const retryRtc = useCallback(() => {
    startRtc();
  }, [startRtc]);

  const clearError = useCallback(() => {
    setError(null);
  }, []);

  return (
    <CompanionContext.Provider
      value={{
        phase,
        link,
        sessionId,
        pairingCode,
        pairingPayload,
        qrDataUrl,
        companionDeviceName,
        phoneListening,
        transcribing,
        slowTranscribe,
        error,
        lastCommand,
        lastTranscript,
        startPairing,
        unpair,
        retryRtc,
        clearError,
      }}
    >
      {children}
    </CompanionContext.Provider>
  );
}

export function useCompanion(): CompanionContextType {
  const ctx = useContext(CompanionContext);
  if (!ctx) {
    // Graceful fallback if called outside provider (e.g. isolated test or web admin)
    return {
      phase: 'idle',
      link: 'idle',
      sessionId: null,
      pairingCode: null,
      pairingPayload: null,
      qrDataUrl: null,
      companionDeviceName: null,
      phoneListening: false,
      transcribing: false,
      slowTranscribe: false,
      error: null,
      lastCommand: null,
      lastTranscript: null,
      startPairing: async () => {},
      unpair: async () => {},
      retryRtc: () => {},
      clearError: () => {},
    };
  }
  return ctx;
}
