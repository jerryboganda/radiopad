'use client';

// Global floating dictation overlay. Replaces the old full-screen
// `/mobile/dictate` navigation in the report editor: a persistent mic icon the
// radiologist toggles on/off. While ON, each finalised utterance is
// auto-formatted and inserted at the caret of whichever text field they last
// focused — so they can click between Findings / Impression / etc. and keep
// dictating without leaving the editor.
//
// Live "Dictate" engine = the browser Web Speech API (Chromium/Chrome/Edge). On
// modern Windows WebView2 this is backed by Microsoft Edge's (online) speech
// service and works — it is the highly-accurate "Microsoft Edge Speech" engine
// surfaced in the On-Device Models manager. When it is unavailable (older runtime
// / no recognizer), the mic is disabled and the radiologist uses the "HQ" button,
// which records and transcribes through the bundled on-device sidecar engine
// (Windows Speech / Parakeet). The "Fix" button asks the host page to
// clean the dictation into medical phrasing via UBAG.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { startFocusTracking, getLastFocusedEditable } from '@/lib/dictation/focusTracker';
import { insertAtCursor } from '@/lib/dictation/insertText';
import { getLastFocusedSectionEditor } from '@/lib/editor/sectionEditorRegistry';
import { setSessionAudio } from '@/lib/dictation/audioBuffer';
import { useCrossCheckEnabled, useUseUbag } from '@/lib/dictation/crossCheckPrefs';
import { formatDictation } from '@/lib/dictation/medicalFormat';
import { resolveCorrections, applyCorrections, type CorrectionRule } from '@/lib/dictation/resolveCorrections';
import { parseVoiceEditCommand } from '@/lib/dictation/voiceEditCommands';
import { createPushToTalk } from '@/lib/dictation/pushToTalk';
import { useFootPedal } from '@/lib/dictation/footPedal';
import { startInterimDecode } from '@/lib/dictation/interimDecode';
import { blobToWav16kMono } from '@/lib/dictation/wavEncode';
import {
  getSpeechRecognitionCtor,
  parseSpeechResults,
  type SpeechRecognitionLike,
} from '@/lib/dictation/speech';
import { api, localSttBase, type SttMode, type SttSpan } from '@/lib/api';
import { STT_MODES, useSttMode } from '@/lib/dictation/sttMode';
import { readQueryParam } from '@/lib/browserParams';

const DICTATE_EVENT = 'radiopad:dictate';
const DICTATE_STATE_EVENT = 'radiopad:dictate-listening';
const CLEANUP_EVENT = 'radiopad:dictation-cleanup';
const CLEANUP_RESULT_EVENT = 'radiopad:dictation-cleanup-result';

// Lifecycle of the "Fix" (dictation cleanup) action, broadcast by the report
// editor (ReportClient) so this overlay can render a live spinner + result.
type FixStatus = { status: 'busy' | 'success' | 'no-changes' | 'empty' | 'error'; message?: string };

function fixStatusLabel(s: FixStatus): string {
  if (s.message) return s.message;
  if (s.status === 'busy') return 'Cleaning dictation…';
  if (s.status === 'success') return 'Done.';
  return '';
}

// Insert dictated text into whichever field the radiologist last focused —
// preferring a rich SectionEditor (cross-check editor) and falling back to a
// plain textarea/input. Returns true if a target was found.
function insertDictation(text: string): boolean {
  const handle = getLastFocusedSectionEditor();
  if (handle) {
    handle.insertAtCursor(text);
    return true;
  }
  const target = getLastFocusedEditable();
  if (target) {
    insertAtCursor(target, text);
    return true;
  }
  return false;
}

export default function DictationOverlay() {
  // DESK-020 — foot-pedal control (hold-to-talk / toggle / next field). The
  // pedal module drives this overlay through the same toggle event the
  // ribbon's Dictate button uses, so the overlay stays the single owner.
  useFootPedal();
  const [supported, setSupported] = useState(false);
  const [listening, setListening] = useState(false);
  const [interim, setInterim] = useState('');
  const recognizerRef = useRef<SpeechRecognitionLike | null>(null);
  const listeningRef = useRef(false);

  // F7 — the effective correction dictionary (org lexicon under the user's personal corrections).
  //
  // The mic path inserts its transcript straight into the editor, so nothing downstream ever
  // applies these: dictating through the microphone silently ignored every correction the
  // radiologist had configured, while the dictation-draft panel honoured them. Held in a ref
  // because the Web Speech `onresult` handler inserts synchronously, and refreshed whenever
  // dictation starts so edits made in Settings take effect on the next utterance.
  const correctionsRef = useRef<CorrectionRule[]>([]);
  const refreshCorrections = useCallback(async () => {
    try {
      const [lexicon, userCorrections] = await Promise.all([
        api.lexicon.list().catch(() => []),
        api.userCorrections.list().catch(() => []),
      ]);
      correctionsRef.current = resolveCorrections(lexicon, userCorrections);
    } catch {
      // Never block dictation on the dictionary — uncorrected text beats no text.
    }
  }, []);
  useEffect(() => {
    void refreshCorrections();
  }, [refreshCorrections]);

  /** Correct, then format — the same order as the backend's deterministic pass-through. */
  const prepareDictated = useCallback(
    (text: string) => formatDictation(applyCorrections(text, correctionsRef.current)),
    [],
  );
  listeningRef.current = listening;

  // Broadcast so other controls (the ribbon's own Dictate button) can mirror
  // this state without owning it — this overlay stays the single source of truth.
  useEffect(() => {
    window.dispatchEvent(new CustomEvent(DICTATE_STATE_EVENT, { detail: { listening } }));
  }, [listening]);

  // High-accuracy (HQ) path: record mic audio with MediaRecorder, send to the
  // backend transcribe endpoint (UBAG-routed), and insert the transcript. This
  // is the path that works in the desktop WebView2 shell, where Web Speech is
  // unavailable.
  const [transcribing, setTranscribing] = useState(false);
  const [audioRecording, setAudioRecording] = useState(false);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const audioStreamRef = useRef<MediaStream | null>(null);

  // On-device engine mode — shared with the profile-menu dual-check toggle so
  // the two controls stay in sync — plus the ensemble's flagged review spans.
  const [mode, setMode] = useSttMode();
  const [crossCheckEnabled] = useCrossCheckEnabled();
  const [useUbag] = useUseUbag();
  const [reviewSpans, setReviewSpans] = useState<SttSpan[]>([]);
  // Transient transcription error (e.g. on-device engine still downloading its
  // model on first run → HTTP 503). Surfaced so the radiologist gets feedback
  // instead of a silent no-op; cleared on the next attempt.
  const [sttError, setSttError] = useState<string | null>(null);

  // "Fix" cleanup progress/result, driven by CLEANUP_RESULT_EVENT from the
  // editor. `busy` shows the spinner; terminal states show a message so the
  // action is never a silent no-op.
  const [fixStatus, setFixStatus] = useState<FixStatus | null>(null);
  const fixDismissRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const fixBusy = fixStatus?.status === 'busy';

  // Live input level (0..1) for the RC-08 meter — real AnalyserNode data on
  // the HQ recording path (the only path that owns the MediaStream).
  const [level, setLevel] = useState(0);
  const meterRafRef = useRef<number | null>(null);
  const meterCtxRef = useRef<AudioContext | null>(null);
  /** Disposer for the live interim-preview tap (P0.3); null when not previewing. */
  const stopInterimPreviewRef = useRef<(() => void) | null>(null);

  const stopMeter = useCallback(() => {
    if (meterRafRef.current !== null) cancelAnimationFrame(meterRafRef.current);
    meterRafRef.current = null;
    void meterCtxRef.current?.close().catch(() => undefined);
    meterCtxRef.current = null;
    setLevel(0);
  }, []);

  const startMeter = useCallback((stream: MediaStream) => {
    try {
      const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!Ctx) return;
      const ctx = new Ctx();
      meterCtxRef.current = ctx;
      const source = ctx.createMediaStreamSource(stream);
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      source.connect(analyser);
      const buf = new Uint8Array(analyser.fftSize);
      const tick = () => {
        analyser.getByteTimeDomainData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) {
          const v = (buf[i] - 128) / 128;
          sum += v * v;
        }
        // RMS → perceptual-ish scale; clamp for a stable meter.
        setLevel(Math.min(1, Math.sqrt(sum / buf.length) * 3));
        meterRafRef.current = requestAnimationFrame(tick);
      };
      meterRafRef.current = requestAnimationFrame(tick);
    } catch {
      /* meter is progressive enhancement — recording continues without it */
    }
  }, []);

  useEffect(() => {
    setSupported(getSpeechRecognitionCtor() !== null);
    const stopTracking = startFocusTracking();
    return () => {
      stopTracking();
      recognizerRef.current?.stop();
      if (meterRafRef.current !== null) cancelAnimationFrame(meterRafRef.current);
      void meterCtxRef.current?.close().catch(() => undefined);
    };
  }, []);

  const stop = useCallback(() => {
    recognizerRef.current?.stop();
    recognizerRef.current = null;
    setListening(false);
    setInterim('');
  }, []);

  const start = useCallback(() => {
    // Pick up corrections edited in Settings since this overlay mounted.
    void refreshCorrections();
    const Ctor = getSpeechRecognitionCtor();
    if (!Ctor) return;
    const rec = new Ctor();
    rec.lang = 'en-US';
    rec.continuous = true;
    rec.interimResults = true;
    rec.onresult = (event) => {
      const { finalText, interimText } = parseSpeechResults(event);
      setInterim(interimText);
      if (finalText) {
        // F1 — a whole-utterance editing command ("scratch that") undoes the last dictation in
        // the focused rich editor instead of being typed into the report. Anything else is
        // ordinary prose and is inserted. If the focused target can't undo, the command is simply
        // swallowed — never typed, so "scratch that" can't end up in the report.
        if (parseVoiceEditCommand(finalText)?.kind === 'undo') {
          getLastFocusedSectionEditor()?.undo?.();
        } else {
          insertDictation(prepareDictated(finalText));
        }
        setInterim('');
      }
    };
    rec.onerror = (e) => {
      setListening(false);
      // Surface the common Edge/WebView2 failures instead of a silent stop so the
      // radiologist knows to grant the mic or fall back to the on-device (HQ) path.
      const err = (e as { error?: string })?.error;
      if (err === 'network') {
        // Deliberately does not name the underlying vendor engine: the platform speech
        // providers are hidden from the UI, so surfacing one only in an error would be
        // the one place a radiologist ever sees it. The remedy is what matters.
        setSttError(
          'Live speech could not reach the speech service. Use the HQ button for on-device dictation, or check this machine’s connection.',
        );
      } else if (err === 'not-allowed' || err === 'service-not-allowed') {
        setSttError('Microphone access is blocked. Allow the microphone to use live dictation.');
      }
    };
    rec.onend = () => {
      setListening(false);
      setInterim('');
    };
    recognizerRef.current = rec;
    try {
      rec.start();
      setListening(true);
    } catch {
      setListening(false);
    }
  }, [prepareDictated, refreshCorrections]);

  const toggle = useCallback(() => {
    if (listeningRef.current) stop();
    else start();
  }, [start, stop]);

  // P0.3 — hold-to-talk push-to-talk, coexisting with tap-to-toggle. A quick tap toggles
  // hands-free listening; pressing and holding the mic dictates only while held.
  const ptt = useMemo(
    () => createPushToTalk({ isActive: () => listeningRef.current, start, stop }),
    [start, stop],
  );

  const transcribeAudio = useCallback(async (blob: Blob) => {
    const reportId = readQueryParam('id');
    const hasTarget = !!getLastFocusedSectionEditor() || !!getLastFocusedEditable();
    // Guard failures must SAY why the recording went nowhere — the audio the
    // radiologist just spoke is otherwise silently discarded.
    //
    // The on-device sidecar (desktop) transcribes anonymously — it needs only
    // the audio, no report id (see `api.reports.transcribe`) — so the New
    // Report wizard can still use HQ while dictating findings/clinical history
    // before a report exists. Only the cloud/web fallback is report-scoped, so
    // that's the only case a missing id should block on.
    if (!reportId && !(await localSttBase())) {
      setSttError('HQ dictation needs an open report — the recording was not transcribed.');
      return;
    }
    if (!hasTarget) {
      setSttError('Click into a report section first so the transcript has somewhere to go — the recording was not transcribed.');
      return;
    }
    if (blob.size === 0) {
      setSttError('No audio was captured (empty recording). Check the microphone and try again.');
      return;
    }
    setTranscribing(true);
    setSttError(null);
    try {
      // Desktop runs an on-device STT engine that needs 16 kHz mono WAV; the
      // WebView2 Chromium engine has the Opus codec, so convert here and the
      // server decodes WAV in-process (no ffmpeg). The web keeps the original
      // recording (cloud transcription accepts it). If conversion fails, send the
      // original and let the backend fall back to the cloud path.
      let payload = blob;
      if (typeof window !== 'undefined' && '__TAURI__' in window) {
        try {
          payload = await blobToWav16kMono(blob);
        } catch {
          payload = blob;
        }
      }
      const res = await api.reports.transcribe(reportId, payload, mode);
      if (res.transcript) {
        const formatted = prepareDictated(res.transcript);
        insertDictation(formatted);
        // Retain the audio + its transcript (and which section it went into) so the
        // manual Cross Check can re-run the same audio through the extra engines.
        const sectionKey = getLastFocusedSectionEditor()?.sectionKey ?? 'findings';
        setSessionAudio(reportId, payload, formatted, sectionKey);
      }
      // Surface ensemble disagreements / safety tokens for the radiologist to
      // eye-confirm (null on single-engine / cloud paths).
      setReviewSpans((res.spans ?? []).filter((s) => s.flagged));
    } catch (e) {
      // Surface a clear message instead of a silent no-op. Two warm-up cases on
      // the desktop: the engine is still provisioning its model on first run
      // (HTTP 503 stt_unavailable), or the loopback sidecar has not finished
      // binding yet (fetch throws a TypeError / "Failed to fetch"). Both
      // self-heal — tell the radiologist to retry. Nothing is inserted on
      // failure, and there is deliberately no cloud fallback (PHI stays local).
      const err = e as { status?: number; body?: { kind?: string }; message?: string };
      const warmingUp =
        err.status === 503 ||
        err.body?.kind === 'stt_unavailable' ||
        e instanceof TypeError ||
        /failed to fetch/i.test(err.message ?? '');
      if (warmingUp) {
        setSttError('On-device dictation engine is starting up (it downloads its model on first run). Give it a moment and try again. If this persists, check that the machine can reach the model host.');
      } else {
        setSttError('Transcription failed. Please try again.');
      }
    } finally {
      setTranscribing(false);
    }
  }, [mode]);

  // P0.3 — live preview while dictating. DISPLAY ONLY: it feeds `setInterim` and nothing else.
  // The transcript that reaches the editor, the formatter and the audit is always the whole-buffer
  // decode in `rec.onstop`, because a segment boundary can split a spoken measurement.
  //
  // Desktop only: segments are decoded by the on-device engine at no marginal cost, whereas on the
  // web each one would be a billed cloud transcription of audio we are about to send in full anyway.
  const startInterimPreview = useCallback((stream: MediaStream) => {
    if (typeof window === 'undefined' || !('__TAURI__' in window)) return;
    // No report-id bail here: this only ever runs inside the desktop shell
    // (checked above), where the on-device sidecar transcribes anonymously —
    // an empty id (e.g. dictating in the New Report wizard, before a report
    // exists) still resolves via the local sidecar in `api.reports.transcribe`.
    const reportId = readQueryParam('id');

    stopInterimPreviewRef.current?.();
    stopInterimPreviewRef.current = startInterimDecode(stream, {
      transcribeSegment: async (wav) => (await api.reports.transcribe(reportId, wav, mode)).transcript ?? '',
      onPreview: setInterim,
      // A failed preview must never disrupt the recording in progress; the authoritative decode
      // still happens on release.
      onError: () => {},
    });
  }, [mode]);

  const stopInterimPreview = useCallback(() => {
    stopInterimPreviewRef.current?.();
    stopInterimPreviewRef.current = null;
    setInterim('');
  }, []);

  const startAudioCapture = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) return;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioStreamRef.current = stream;
      audioChunksRef.current = [];
      startMeter(stream);
      startInterimPreview(stream);
      const rec = new MediaRecorder(stream, { mimeType: 'audio/webm' });
      rec.ondataavailable = (ev) => {
        if (ev.data && ev.data.size > 0) audioChunksRef.current.push(ev.data);
      };
      rec.onstop = () => {
        const blob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
        audioStreamRef.current?.getTracks().forEach((t) => t.stop());
        audioStreamRef.current = null;
        stopMeter();
        stopInterimPreview();
        // The AUTHORITATIVE transcript: the whole buffer, decoded once. The live preview above is
        // never used for this — a segment boundary can fall inside a spoken measurement.
        void transcribeAudio(blob);
      };
      mediaRecorderRef.current = rec;
      rec.start();
      setAudioRecording(true);
    } catch (e) {
      setAudioRecording(false);
      stopMeter();
      stopInterimPreview();
      // A denied/absent microphone must not look like a button that does
      // nothing — say what blocked the HQ recording.
      const name = (e as { name?: string })?.name;
      setSttError(
        name === 'NotAllowedError' || name === 'SecurityError'
          ? 'Microphone access is blocked. Allow the microphone to record HQ dictation.'
          : name === 'NotFoundError'
            ? 'No microphone was found on this machine.'
            : 'Could not start the HQ recording. Check the microphone and try again.',
      );
    }
  }, [transcribeAudio, startMeter, stopMeter, startInterimPreview, stopInterimPreview]);

  const stopAudioCapture = useCallback(() => {
    const rec = mediaRecorderRef.current;
    if (rec && rec.state !== 'inactive') rec.stop();
    mediaRecorderRef.current = null;
    // Defensive: `rec.onstop` normally tears the preview down, but it never fires if the recorder
    // failed to start or was already inactive — and an orphaned tap would hold the AudioContext
    // and the microphone open.
    stopInterimPreview();
    setAudioRecording(false);
  }, [stopInterimPreview]);

  const toggleAudio = useCallback(() => {
    if (audioRecording) stopAudioCapture();
    else void startAudioCapture();
  }, [audioRecording, startAudioCapture, stopAudioCapture]);

  // Desktop hotkey (Ctrl+Shift+D) and the editor's Dictate button both dispatch
  // `radiopad:dictate`; this is the single owner that toggles the overlay.
  useEffect(() => {
    const handler = () => toggle();
    window.addEventListener(DICTATE_EVENT, handler);
    return () => window.removeEventListener(DICTATE_EVENT, handler);
  }, [toggle]);

  // Reflect the editor's cleanup lifecycle on the Fix button. Success / no-change
  // / empty outcomes auto-clear after a few seconds; errors stay until the next
  // attempt so the radiologist can read them.
  useEffect(() => {
    const onResult = (e: Event) => {
      const detail = (e as CustomEvent<FixStatus>).detail;
      if (!detail) return;
      if (fixDismissRef.current) {
        clearTimeout(fixDismissRef.current);
        fixDismissRef.current = null;
      }
      setFixStatus(detail);
      if (detail.status === 'success' || detail.status === 'no-changes' || detail.status === 'empty') {
        fixDismissRef.current = setTimeout(() => setFixStatus(null), 6000);
      }
    };
    window.addEventListener(CLEANUP_RESULT_EVENT, onResult);
    return () => {
      window.removeEventListener(CLEANUP_RESULT_EVENT, onResult);
      if (fixDismissRef.current) clearTimeout(fixDismissRef.current);
    };
  }, []);

  // Truthful HQ tooltip: the desktop shell (same `__TAURI__` signal the
  // transcribe path keys on) transcribes fully on-device via the bundled STT
  // sidecar — audio never leaves the machine; only the web surface uses the
  // report-scoped cloud transcription path. Resolved in an effect so the
  // prerendered HTML stays hydration-safe.
  const [onDeviceStt, setOnDeviceStt] = useState(false);
  useEffect(() => {
    setOnDeviceStt(typeof window !== 'undefined' && '__TAURI__' in window);
  }, []);
  const hqTitle = onDeviceStt
    ? 'Record and transcribe with high accuracy (on-device — audio never leaves this machine)'
    : 'Record and transcribe with high accuracy (cloud transcription)';

  const title = !supported
    ? 'Dictation needs the audio engine — unavailable in this build'
    : listening
      ? 'Stop dictation'
      : 'Start dictation';

  // RC-08 state machine → color-coded bar states (labels + icons carry the
  // meaning alongside color; never hue-only).
  const barState = !supported
      && (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia)
    ? 'disconnected'
    : sttError
      ? 'error'
      : transcribing
        ? 'processing'
        : audioRecording
          ? 'listening'
          : listening
            ? 'listening'
            : 'idle';
  const statusLine =
    barState === 'processing'
      ? 'Processing your dictation…'
      : audioRecording
        ? 'Recording (HQ)… press Stop to transcribe'
        : listening
          ? 'Listening… Speak now'
          : barState === 'disconnected'
            ? 'No dictation engine available on this build'
            : barState === 'error'
              ? 'Dictation needs attention'
              : 'Ready to dictate';

  return (
    <div
      className="rp-dictation"
      data-testid="dictation-overlay"
      data-state={barState}
      onKeyDown={(e) => {
        // RC-08: Escape stops dictation — scoped to the bar itself so typing
        // elsewhere is never hijacked (buttons keep native Space/Enter).
        if (e.key === 'Escape') {
          if (listeningRef.current) stop();
          if (audioRecording) stopAudioCapture();
        }
      }}
    >
      {listening && interim && (
        <div className="rp-dictation-interim" data-testid="dictation-interim">
          {interim}
        </div>
      )}
      {sttError && (
        <div className="rp-dictation-interim" data-testid="dictation-error" role="alert">
          {sttError}
          <button
            type="button"
            className="subtle"
            data-testid="dictation-error-dismiss"
            aria-label="Dismiss error"
            onClick={() => setSttError(null)}
          >
            ×
          </button>
        </div>
      )}
      {reviewSpans.length > 0 && (
        <div className="rp-dictation-interim" data-testid="dictation-review" role="status">
          {reviewSpans.length} to verify:{' '}
          {reviewSpans.map((s, i) => (
            <span key={i} className="ai-mark" title={s.reason ?? 'review'}>
              {s.text}
            </span>
          ))}
          <button
            type="button"
            className="subtle"
            data-testid="dictation-review-dismiss"
            aria-label="Dismiss review"
            onClick={() => setReviewSpans([])}
          >
            ×
          </button>
        </div>
      )}
      {fixStatus && (
        <div
          className={`rp-dictation-interim rp-dictation-status rp-dictation-status-${fixStatus.status}`}
          data-testid="dictation-fix-status"
          role="status"
          aria-live="polite"
        >
          {fixBusy && <span className="rp-dictation-spinner" aria-hidden="true" />}
          <span>{fixStatusLabel(fixStatus)}</span>
          {!fixBusy && (
            <button
              type="button"
              className="subtle"
              data-testid="dictation-fix-status-dismiss"
              aria-label="Dismiss"
              onClick={() => setFixStatus(null)}
            >
              ×
            </button>
          )}
        </div>
      )}
      <div className="rp-dictation-controls">
        <button
          type="button"
          data-testid="dictation-fab"
          className={`rp-dictation-fab${listening ? ' recording' : ''}`}
          aria-pressed={listening}
          aria-label={title}
          title={title}
          disabled={!supported}
          onMouseDown={(e) => e.preventDefault()}
          onPointerDown={(e) => {
            e.preventDefault();
            // Capture the pointer so a release that drifts off the button still ends push-to-talk.
            try {
              e.currentTarget.setPointerCapture(e.pointerId);
            } catch {
              /* jsdom / unsupported — release is handled by onPointerUp on the button */
            }
            ptt.pointerDown();
          }}
          onPointerUp={() => ptt.pointerUp()}
          onPointerCancel={() => ptt.pointerUp()}
          // Pointer taps are handled above; onClick only toggles for keyboard (Enter/Space).
          onClick={() => {
            if (!ptt.claimClick()) toggle();
          }}
        >
          <span className="rp-dictation-dot" aria-hidden="true" />
          <span className="rp-dictation-label">{listening ? 'Listening…' : 'Dictate'}</span>
        </button>
        {/* RC-08: live input meter (real AnalyserNode level while the HQ path
            owns the mic; decorative pulse while the live engine listens). */}
        <span className={`rp-dictation-meter${listening || audioRecording ? ' active' : ''}`} aria-hidden="true">
          {Array.from({ length: 8 }, (_, i) => (
            <span
              key={i}
              className="rp-dictation-meter-bar"
              data-on={audioRecording ? level * 8 > i : undefined}
            />
          ))}
        </span>
        <span className="rp-dictation-statusline" role="status">{statusLine}</span>
        <button
          type="button"
          data-testid="dictation-hq"
          className={`rp-dictation-fix subtle${audioRecording ? ' recording' : ''}`}
          aria-pressed={audioRecording}
          title={hqTitle}
          disabled={transcribing}
          onMouseDown={(e) => e.preventDefault()}
          onClick={toggleAudio}
        >
          {transcribing ? '…' : audioRecording ? 'Stop' : 'HQ'}
        </button>
        <button
          type="button"
          data-testid="dictation-fix"
          className={`rp-dictation-fix subtle${fixBusy ? ' recording' : ''}`}
          title="Clean up the dictation into medical phrasing (UBAG)"
          aria-busy={fixBusy}
          disabled={fixBusy}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => {
            // Cancelable dispatch: the report editor claims the event with
            // preventDefault. If nothing claims it (e.g. on /reports/new),
            // say so instead of silently doing nothing.
            const claimed = !window.dispatchEvent(new CustomEvent(CLEANUP_EVENT, { cancelable: true }));
            if (!claimed) {
              setFixStatus({ status: 'error', message: 'Open a report to use Fix — it cleans up the report editor’s dictation.' });
            }
          }}
        >
          {fixBusy ? (
            <>
              <span className="rp-dictation-spinner" aria-hidden="true" />
              Fixing…
            </>
          ) : (
            'Fix'
          )}
        </button>
        {crossCheckEnabled && (
          <button
            type="button"
            data-testid="dictation-crosscheck"
            className="rp-dictation-fix subtle"
            title="Re-run the dictation through the cross-check engines and verify the wording"
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => {
              if (
                useUbag &&
                !window.confirm(
                  'Cross-check via UBAG sends report text to a cloud AI service. ' +
                    'Confirm this report contains NO patient-identifying information (PHI).',
                )
              ) {
                return;
              }
              const claimed = !window.dispatchEvent(new CustomEvent('radiopad:cross-check', { cancelable: true }));
              if (!claimed) {
                setFixStatus({ status: 'error', message: 'Open a report to use Cross Check — it verifies the report editor’s dictation.' });
              }
            }}
          >
            Cross Check
          </button>
        )}
        <select
          className="subtle"
          data-testid="dictation-mode"
          aria-label="On-device transcription engine mode"
          title="On-device engine: Auto, Single or Ensemble (two on-device engines, cross-checked)"
          value={mode}
          onChange={(e) => setMode(e.target.value as SttMode)}
        >
          {STT_MODES.map((m) => (
            <option key={m} value={m}>
              {m === 'auto' ? 'Auto' : m === 'single' ? 'Single' : 'Ensemble'}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
