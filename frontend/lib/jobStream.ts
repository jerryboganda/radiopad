/**
 * Streamed partial-text store for live AI token streaming (durable async-job
 * platform, 2026-07-23).
 *
 * Deliberately OUTSIDE the `jobsReducer` / React state tree (architectural
 * decision 4): a stream can emit dozens of `partial` events per second, and
 * routing every token through `dispatch` would re-render the whole jobs tree on
 * each one. Instead this module holds per-job streamed text in a plain `Map`
 * with per-job subscriber sets; components read it through `useJobPartial`
 * (`useSyncExternalStore`). It notifies at most once per ~100 ms per job to keep
 * the preview UI smooth while still guaranteeing the final characters of a burst
 * eventually render.
 *
 * Clinical-safety note: in-memory only. Nothing here is ever persisted to
 * localStorage or included in `writeSeed` — it carries raw model output (PHI),
 * so it lives and dies with the tab, exactly like the "no clinical text at rest"
 * doctrine in `lib/jobs.ts`.
 */

/** The immutable view a subscriber reads. A fresh object is published only when
 *  the store notifies (never on every append), so `getSnapshot` returns a stable
 *  reference between notifies — the `useSyncExternalStore` contract. */
export interface JobStreamBuffer {
  /** The accumulated model output for this job, tail-capped (see below). */
  text: string;
  /** Cumulative output-token count for the current attempt (always honest). */
  tokens: number;
  /** True once the job has settled (terminal). */
  done: boolean;
}

/** Only the tail is ever shown in the preview, so a very long stream keeps just
 *  its last `MAX_BUFFER_CHARS` characters — bounds memory without losing the
 *  visible portion. */
const MAX_BUFFER_CHARS = 20_000;

/** At most one subscriber notification per this window, per job. */
const NOTIFY_THROTTLE_MS = 100;

interface Entry {
  /** Live, mutable accumulator (may be ahead of `snapshot` inside a throttle window). */
  text: string;
  tokens: number;
  done: boolean;
  /** Last published, immutable view — what `getSnapshot` returns (stable ref). */
  snapshot: JobStreamBuffer;
  /** Timestamp of the last notification; `-Infinity` so the first append fires immediately. */
  lastNotifyAt: number;
  /** Set while a trailing flush is queued to guarantee the burst's tail renders. */
  pendingTimer: ReturnType<typeof setTimeout> | null;
}

class JobPartialStore {
  /** Streamed data per job. Cleared on `clear` (terminal apply / dismiss / CLEAR_ALL). */
  private readonly data = new Map<string, Entry>();
  /** Subscribers per job, kept SEPARATE from `data` so `clear` can delete the
   *  data entry while still notifying live subscribers (they re-read → undefined). */
  private readonly subs = new Map<string, Set<() => void>>();

  /** Append one streamed delta. Concatenates, tail-caps, records the cumulative
   *  token count, and notifies subscribers at most once per ~100 ms per job —
   *  scheduling a trailing flush when throttled so the tail is never dropped. */
  append(jobId: string, delta: string, tokens?: number): void {
    const entry = this.ensure(jobId);
    entry.text += delta;
    if (entry.text.length > MAX_BUFFER_CHARS) {
      entry.text = entry.text.slice(-MAX_BUFFER_CHARS);
    }
    // `tokens` is the server's cumulative count for this attempt, so the latest
    // value wins (never additive).
    if (tokens != null) entry.tokens = tokens;
    this.scheduleNotify(jobId, entry);
  }

  /** Mark the stream terminal. Bypasses the throttle so the "done" transition is
   *  never delayed, and cancels any queued trailing flush. */
  markDone(jobId: string): void {
    const entry = this.ensure(jobId);
    if (entry.done) return;
    entry.done = true;
    this.flushNow(jobId, entry);
  }

  /** Drop a job's buffer entirely and notify subscribers (they see `undefined`). */
  clear(jobId: string): void {
    const entry = this.data.get(jobId);
    if (entry?.pendingTimer != null) clearTimeout(entry.pendingTimer);
    if (this.data.delete(jobId)) this.notify(jobId);
  }

  /** Current published view (stable reference); `undefined` if the job has no buffer. */
  get(jobId: string): JobStreamBuffer | undefined {
    return this.data.get(jobId)?.snapshot;
  }

  /** `useSyncExternalStore` snapshot — MUST return the same reference between
   *  notifies. See {@link JobStreamBuffer}. */
  getSnapshot(jobId: string): JobStreamBuffer | undefined {
    return this.data.get(jobId)?.snapshot;
  }

  /** Subscribe to change notifications for one job. Returns an unsubscribe fn. */
  subscribe(jobId: string, fn: () => void): () => void {
    let set = this.subs.get(jobId);
    if (!set) {
      set = new Set();
      this.subs.set(jobId, set);
    }
    set.add(fn);
    return () => {
      const s = this.subs.get(jobId);
      if (!s) return;
      s.delete(fn);
      if (s.size === 0) this.subs.delete(jobId);
    };
  }

  // --- internals ------------------------------------------------------------

  private ensure(jobId: string): Entry {
    let entry = this.data.get(jobId);
    if (!entry) {
      entry = {
        text: '',
        tokens: 0,
        done: false,
        snapshot: { text: '', tokens: 0, done: false },
        lastNotifyAt: Number.NEGATIVE_INFINITY,
        pendingTimer: null,
      };
      this.data.set(jobId, entry);
    }
    return entry;
  }

  /** Publish the live values into a fresh snapshot object (the only place a new
   *  reference is minted, so `getSnapshot` stays stable between notifies). */
  private publish(entry: Entry): void {
    entry.snapshot = { text: entry.text, tokens: entry.tokens, done: entry.done };
  }

  private notify(jobId: string): void {
    const set = this.subs.get(jobId);
    if (!set) return;
    for (const fn of set) fn();
  }

  /** Notify immediately, bypassing the throttle and cancelling any pending flush. */
  private flushNow(jobId: string, entry: Entry): void {
    if (entry.pendingTimer != null) {
      clearTimeout(entry.pendingTimer);
      entry.pendingTimer = null;
    }
    entry.lastNotifyAt = Date.now();
    this.publish(entry);
    this.notify(jobId);
  }

  /** Throttled notify: fire now if the window has elapsed, else schedule a single
   *  trailing flush at the window's close (picking up every append that lands in
   *  the meantime — eventual consistency, no lost tail). */
  private scheduleNotify(jobId: string, entry: Entry): void {
    const now = Date.now();
    const since = now - entry.lastNotifyAt;
    if (since >= NOTIFY_THROTTLE_MS) {
      this.flushNow(jobId, entry);
      return;
    }
    if (entry.pendingTimer != null) return; // a trailing flush is already queued
    entry.pendingTimer = setTimeout(() => {
      entry.pendingTimer = null;
      entry.lastNotifyAt = Date.now();
      this.publish(entry);
      this.notify(jobId);
    }, NOTIFY_THROTTLE_MS - since);
  }
}

/** The single app-wide store. Fed by `JobsProvider` from `partial` bus events. */
export const jobPartials = new JobPartialStore();
