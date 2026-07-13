'use client';

/**
 * Phone-side "type to dictate" streamer.
 *
 * Second companion input mode alongside the Wi-Fi mic: the radiologist taps a
 * textarea and dictates with the PHONE KEYBOARD's own voice typing (Gboard mic,
 * iOS dictation, …) — the most accurate free recognizer available on the device,
 * and the OS keeps it real-time. Every keystroke/voice-word streams to the
 * desktop as a live interim preview; a natural pause (or the Insert button)
 * commits the formatted text into the focused report section.
 *
 * Rides the relay `dictation` messages (`{type:'dictation', text, isFinal}`),
 * which the desktop host has handled since v0.1.66 — interim → at-caret ghost
 * preview, final → insertAtCursor. Because it uses the relay (not the LAN data
 * channel), it also works when the direct Wi-Fi link can't form (AP isolation /
 * different subnets) — a graceful fallback for hostile hospital networks.
 *
 * Pure logic (timers + callbacks) — unit-tested with fake timers.
 */

export interface TypeDictationOptions {
  /** Relay sender — `CompanionConnection.sendDictation`. */
  send: (text: string, isFinal: boolean) => void;
  /** Final-commit formatter (spoken punctuation, sentence caps). Default: identity. */
  format?: (text: string) => string;
  /**
   * Called after a commit so the UI can clear the textarea. `raw` is the
   * pre-format text as it stood in the field — the UI uses it to strip the
   * committed prefix from a change event that raced the clear (voice typing
   * can land a word between the idle-commit and the re-render), which would
   * otherwise double-insert the phrase.
   */
  onCommitted?: (formatted: string, raw: string) => void;
  /** Pause (ms) with no input after which non-empty text auto-commits. */
  idleCommitMs?: number;
  /** Trailing throttle (ms) for interim (live preview) sends. */
  interimThrottleMs?: number;
  /**
   * When true at idle time, the auto-commit is postponed one idle window (checked
   * again each window). Used to never clear the textarea while the phone IME has
   * an active composition — clobbering the field mid-composition duplicates or
   * mangles the phrase on some keyboards. Manual commit()/dispose() ignore it.
   */
  deferIdleCommit?: () => boolean;
}

export interface TypeDictationStreamer {
  /** Feed the current full textarea value on every change. */
  onTextChange: (text: string) => void;
  /** Commit now (Insert button). Returns the committed text ('' if nothing). */
  commit: () => string;
  /** Cancel timers and clear any desktop-side interim preview. */
  dispose: () => void;
}

const DEFAULT_IDLE_COMMIT_MS = 1_600;
const DEFAULT_INTERIM_THROTTLE_MS = 150;

export function createTypeDictationStreamer(opts: TypeDictationOptions): TypeDictationStreamer {
  const idleMs = opts.idleCommitMs ?? DEFAULT_IDLE_COMMIT_MS;
  const throttleMs = opts.interimThrottleMs ?? DEFAULT_INTERIM_THROTTLE_MS;
  const format = opts.format ?? ((t: string) => t);

  let latest = '';
  let disposed = false;
  let interimTimer: ReturnType<typeof setTimeout> | null = null;
  let idleTimer: ReturnType<typeof setTimeout> | null = null;
  /** True when the desktop may be showing a non-empty interim preview. */
  let previewShown = false;

  function clearTimers() {
    if (interimTimer) { clearTimeout(interimTimer); interimTimer = null; }
    if (idleTimer) { clearTimeout(idleTimer); idleTimer = null; }
  }

  function sendInterim() {
    interimTimer = null;
    if (disposed) return;
    if (latest.trim()) {
      opts.send(latest, false);
      previewShown = true;
    } else if (previewShown) {
      // Field emptied while a preview is showing — clear the ghost (an
      // empty-text FINAL inserts nothing and drops the preview desktop-side;
      // an empty interim would render a blank widget instead).
      opts.send('', true);
      previewShown = false;
    }
  }

  function commit(): string {
    clearTimers();
    if (disposed) return '';
    const raw = latest;
    const text = format(raw).trim();
    latest = '';
    if (text) {
      // Final with text: the desktop inserts it AND drops the interim preview.
      opts.send(text, true);
    } else if (previewShown) {
      // Nothing worth inserting, but a stale preview may linger — clear it.
      // (Empty-text finals insert nothing; the desktop just clears the ghost.)
      opts.send('', true);
    }
    previewShown = false;
    if (text) opts.onCommitted?.(text, raw);
    return text;
  }

  function armIdle() {
    if (idleTimer) clearTimeout(idleTimer);
    idleTimer = setTimeout(() => {
      idleTimer = null;
      if (disposed) return;
      // Mid-composition: postpone a full window and check again, so the field
      // is never cleared out from under an active IME composition.
      if (opts.deferIdleCommit?.()) { armIdle(); return; }
      commit();
    }, idleMs);
  }

  return {
    onTextChange(text: string) {
      if (disposed) return;
      latest = text;
      // Live preview: trailing throttle so word-bursts from voice typing don't
      // flood the relay, while the desktop still updates in near-real-time.
      if (!interimTimer) interimTimer = setTimeout(sendInterim, throttleMs);
      // Auto-commit at a natural pause. An empty textarea just clears any
      // lingering preview instead of committing.
      armIdle();
    },
    commit,
    dispose() {
      if (disposed) return;
      clearTimers();
      // Drop any ghost preview left on the desktop before going away.
      if (previewShown) opts.send('', true);
      disposed = true;
      latest = '';
      previewShown = false;
    },
  };
}
