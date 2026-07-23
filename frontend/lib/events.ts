/**
 * Server-push event manager (durable async-job platform, 2026-07-23).
 *
 * A React-free, ref-counted singleton that owns ONE SSE connection per app and
 * fans the decoded events out to subscribers (the JobsProvider today; the
 * notifications provider subscribes to the same instance for `notification`
 * events — this module is the shared seam). All network I/O is delegated to the
 * `connect` function (`api.events.stream`), so the manager is fully unit-testable
 * with a fake connector and fake timers.
 *
 * Deliberate non-goals (per the plan's architectural decisions):
 *  - No Last-Event-ID / server resume. The server bus is a drop-oldest channel
 *    that cannot replay; reconnect correctness comes from the consumer re-running
 *    its hydration on each `open` and the poll fallback covering `down`. The
 *    parser still captures `id`, but the manager ignores it.
 *  - No `document.hidden` handling. One idle socket while the tab is hidden beats
 *    the old 5s hidden-poll floor; the connection stays open.
 */

import { api, type JobKind, type JobSummary, type SseEvent } from './api';

/** A decoded, typed bus event fanned out to subscribers. `data` is already
 *  JSON-parsed; malformed events are dropped inside the manager and never reach
 *  a subscriber. */
export type AppEvent =
  | { type: 'job'; data: JobSummary }
  | {
      type: 'progress';
      data: {
        jobId: string;
        kind: JobKind;
        mode: string;
        reportId: string;
        tokens: number;
        percent?: number | null;
      };
    }
  | { type: 'partial'; data: { jobId: string; delta: string; tokens?: number } }
  | { type: 'notification'; data: unknown };

/**
 * Connection status.
 *  - `idle` — no subscribers (or stopped); no connection.
 *  - `connecting` — a connection attempt is in flight, no bytes yet.
 *  - `open` — HTTP 200 and at least one byte (event or keep-alive) received.
 *  - `down` — an attempt failed/ended (not an auth failure); a reconnect is
 *    scheduled. Consumers resume polling here.
 *  - `auth-error` — a 401/403 survived the connector's one silent re-auth; the
 *    manager stops entirely (no reconnect loop hammering a dead session) until a
 *    new `subscribe()` restarts it (e.g. AuthGate remounts on a fresh login).
 */
export type StreamStatus = 'idle' | 'connecting' | 'open' | 'down' | 'auth-error';

/** The SSE event names the server emits (contract B). Anything else (including
 *  the default `message`) is ignored. */
const KNOWN_EVENT_TYPES = new Set<AppEvent['type']>([
  'job',
  'progress',
  'partial',
  'notification',
]);

// Backoff / liveness tuning (documented in the plan §2.3).
const BACKOFF_BASE_MS = 1_000;
const BACKOFF_CAP_MS = 30_000;
/** A connection open for at least this long is a genuine success → reset backoff. */
const HEALTHY_RESET_MS = 30_000;
/** No bytes (event or comment) for this long → the socket is dead; reconnect. */
const LIVENESS_TIMEOUT_MS = 45_000;

export class EventStreamManager {
  private readonly connect: typeof api.events.stream;
  private readonly baseOverride?: string;
  private readonly path?: string;

  private readonly listeners = new Set<(e: AppEvent) => void>();
  private readonly statusListeners = new Set<(s: StreamStatus) => void>();

  private _status: StreamStatus = 'idle';
  /** True while the manager should hold/retry a connection. */
  private active = false;
  /** The current in-flight connection's abort controller. */
  private abort: AbortController | null = null;
  /** Backoff attempt counter (0 = base delay). */
  private attempt = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private livenessTimer: ReturnType<typeof setTimeout> | null = null;
  /** Timestamp of the current connection's first byte (null until `open`). */
  private connectedAt: number | null = null;
  /** Set when the liveness timer aborts the socket, so the settle handler
   *  reconnects immediately without treating it as a fresh success. */
  private livenessReconnect = false;

  constructor(opts: { connect: typeof api.events.stream; baseOverride?: string; path?: string }) {
    this.connect = opts.connect;
    this.baseOverride = opts.baseOverride;
    this.path = opts.path;
  }

  get status(): StreamStatus {
    return this._status;
  }

  /** Ref-counted: the first subscriber (or the first after an auth-error /
   *  idle stop) starts the connection; the last unsubscribe stops it. */
  subscribe(fn: (e: AppEvent) => void): () => void {
    this.listeners.add(fn);
    if (!this.active) this.start();
    return () => {
      this.listeners.delete(fn);
      if (this.listeners.size === 0) this.stop();
    };
  }

  onStatus(fn: (s: StreamStatus) => void): () => void {
    this.statusListeners.add(fn);
    return () => {
      this.statusListeners.delete(fn);
    };
  }

  // --- internals ----------------------------------------------------------

  private start(): void {
    this.active = true;
    this.attempt = 0;
    this.setStatus('connecting');
    void this.runConnection();
  }

  private stop(): void {
    this.active = false;
    this.clearReconnectTimer();
    this.clearLivenessTimer();
    if (this.abort) {
      this.abort.abort();
      this.abort = null;
    }
    this.connectedAt = null;
    this.livenessReconnect = false;
    this.setStatus('idle');
  }

  private async runConnection(): Promise<void> {
    if (!this.active) return;
    const abort = new AbortController();
    this.abort = abort;
    this.connectedAt = null;
    let sawByte = false;

    const markByte = (): void => {
      if (!sawByte) {
        sawByte = true;
        this.connectedAt = Date.now();
        this.setStatus('open');
      }
      this.armLiveness(abort);
    };

    try {
      await this.connect({
        signal: abort.signal,
        baseOverride: this.baseOverride,
        path: this.path,
        onEvent: (e) => {
          markByte();
          this.dispatchSse(e);
        },
        onComment: () => {
          markByte();
        },
      });
      this.clearLivenessTimer();
      this.afterConnection(sawByte, null);
    } catch (err) {
      this.clearLivenessTimer();
      this.afterConnection(sawByte, err);
    }
  }

  private afterConnection(sawByte: boolean, err: unknown): void {
    // A liveness-triggered abort reconnects immediately and keeps the backoff
    // where it is (this was not a fresh success).
    if (this.livenessReconnect) {
      this.livenessReconnect = false;
      if (!this.active) return;
      void this.runConnection();
      return;
    }
    if (!this.active) return; // stopped by the last unsubscribe

    const status =
      err && typeof err === 'object' ? (err as { status?: number }).status : undefined;
    if (status === 401 || status === 403) {
      // Survived the connector's one silent re-auth — stop entirely.
      this.active = false;
      this.clearReconnectTimer();
      this.connectedAt = null;
      this.setStatus('auth-error');
      return;
    }

    // A connection that stayed open long enough is a genuine success: reset the
    // backoff so the next drop reconnects promptly.
    if (sawByte && this.connectedAt != null && Date.now() - this.connectedAt >= HEALTHY_RESET_MS) {
      this.attempt = 0;
    }
    this.setStatus('down');
    this.scheduleReconnect();
  }

  private scheduleReconnect(): void {
    this.clearReconnectTimer();
    const baseDelay = Math.min(BACKOFF_BASE_MS * 2 ** this.attempt, BACKOFF_CAP_MS);
    // Jitter ±30% to avoid a reconnect thundering herd.
    const delay = baseDelay * (0.7 + Math.random() * 0.6);
    this.attempt += 1;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.active) return;
      this.setStatus('connecting');
      void this.runConnection();
    }, delay);
  }

  private armLiveness(abort: AbortController): void {
    this.clearLivenessTimer();
    this.livenessTimer = setTimeout(() => {
      this.livenessTimer = null;
      // No bytes for the timeout window — the socket is silently dead (a proxy
      // buffering SSE, say). Abort and reconnect immediately.
      this.livenessReconnect = true;
      abort.abort();
    }, LIVENESS_TIMEOUT_MS);
  }

  private clearLivenessTimer(): void {
    if (this.livenessTimer != null) {
      clearTimeout(this.livenessTimer);
      this.livenessTimer = null;
    }
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer != null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private dispatchSse(e: SseEvent): void {
    const type = e.event as AppEvent['type'];
    if (!KNOWN_EVENT_TYPES.has(type)) return; // ignore `message` / unknown names
    let data: unknown;
    try {
      data = JSON.parse(e.data);
    } catch {
      // A malformed payload is dropped, never thrown into subscribers.
      console.warn(`[events] dropping ${type} event with malformed JSON`);
      return;
    }
    const appEvent = { type, data } as unknown as AppEvent;
    for (const fn of this.listeners) fn(appEvent);
  }

  private setStatus(s: StreamStatus): void {
    if (this._status === s) return;
    this._status = s;
    for (const fn of this.statusListeners) fn(s);
  }
}

/** The default hosted instance — `GET /api/events/stream` against `apiBase()`. */
export const hostedEvents = new EventStreamManager({ connect: api.events.stream });

/**
 * A sidecar instance pointed at the desktop loopback base. Consumes the
 * sidecar's local-generation SSE (Tauri only, loopback, `[AllowAnonymous]`); its
 * failure silently falls back to the existing 2s local poll.
 */
export function createLocalEvents(base: string): EventStreamManager {
  return new EventStreamManager({
    connect: api.events.stream,
    baseOverride: base,
    path: '/api/local-generation/events',
  });
}
