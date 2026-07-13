'use client';

/**
 * Desktop-side ordered transcription queue for companion audio.
 *
 * Audio segments arrive from the phone over the WebRTC data channel (already
 * reassembled by {@link ./companionRtc}). Transcription is async and the
 * on-device Parakeet sidecar is single-threaded, so segments MUST be transcribed
 * and inserted strictly in capture order (`seq`) or the report text scrambles.
 * This runs a single-consumer FIFO: it waits for the next expected `seq`,
 * transcribes it, inserts it, then advances — never processing a later phrase
 * before an earlier one. A dropped segment (a corrupt/incomplete one that
 * `companionRtc` discarded) is skipped after a short grace so the queue can't
 * stall forever.
 */

const SKIP_GRACE_MS = 4_000;

export interface AudioReceiverOptions {
  /** webm segment → transcript (→ blobToWav16kMono + the on-device STT sidecar). */
  transcribe: (webm: Blob) => Promise<string>;
  /** Commit a finished transcript into the focused report section. */
  insert: (text: string) => void;
  /** True while a segment is being transcribed (drives the "transcribing…" hint). */
  onBusyChange?: (transcribing: boolean) => void;
  onError?: (message: string) => void;
}

export interface AudioReceiver {
  /** Enqueue a reassembled segment (from `companionRtc` onSegment). */
  pushSegment: (blob: Blob, seq: number) => void;
  /** Drop all queued/expected segments (on unpair / session end). */
  reset: () => void;
}

export function createAudioReceiver(opts: AudioReceiverOptions): AudioReceiver {
  const pending = new Map<number, Blob>();
  let nextSeq = 0;
  let maxSeq = -1;
  let processing = false;
  let generation = 0; // bumped by reset() to abandon an in-flight transcription
  let skipTimer: ReturnType<typeof setTimeout> | null = null;

  function clearSkip() {
    if (skipTimer) { clearTimeout(skipTimer); skipTimer = null; }
  }

  function scheduleSkipIfStalled() {
    if (skipTimer) return;
    // Blocked waiting for `nextSeq`, but a later segment is already queued → the
    // expected one was likely dropped. Skip it after a grace period.
    if (!pending.has(nextSeq) && maxSeq >= nextSeq && pending.size > 0) {
      skipTimer = setTimeout(() => {
        skipTimer = null;
        if (!pending.has(nextSeq) && pending.size > 0) {
          nextSeq += 1;
          void drain();
        }
      }, SKIP_GRACE_MS);
    }
  }

  async function drain() {
    if (processing) return;
    processing = true;
    const gen = generation;
    try {
      while (gen === generation && pending.has(nextSeq)) {
        clearSkip();
        const blob = pending.get(nextSeq)!;
        pending.delete(nextSeq);
        opts.onBusyChange?.(true);
        try {
          const text = await opts.transcribe(blob);
          // reset() ran while transcribing — this belongs to a dead session; drop it.
          if (gen !== generation) return;
          if (text && text.trim()) opts.insert(text);
        } catch (e) {
          if (gen !== generation) return;
          // Surface the pipeline's own message (codec/timeout/engine errors are
          // actionable); fall back to the generic line for unknown throw shapes.
          const message = e instanceof Error && e.message ? e.message : 'Could not transcribe a phrase.';
          opts.onError?.(message);
        } finally {
          opts.onBusyChange?.(false);
        }
        nextSeq += 1;
      }
    } finally {
      processing = false;
    }
    if (gen === generation) scheduleSkipIfStalled();
  }

  return {
    pushSegment(blob, seq) {
      if (seq < nextSeq) return; // already processed / duplicate
      pending.set(seq, blob);
      if (seq > maxSeq) maxSeq = seq;
      void drain();
    },
    reset() {
      generation += 1; // abandon any in-flight transcription (its insert is dropped)
      clearSkip();
      pending.clear();
      nextSeq = 0;
      maxSeq = -1;
    },
  };
}
