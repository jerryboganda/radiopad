/**
 * Companion relay WebSocket client (raw `WebSocket`, no external dependency).
 *
 * The desktop host and the phone companion each open a socket to the cloud
 * relay (`/ws/companion` on `companionBase()`), authenticate with the bearer in
 * the `access_token` query param (browsers can't set WebSocket headers), send a
 * `hello` frame naming their role + session, then exchange JSON messages that
 * the server forwards to the peer.
 *
 * Message contract (must match the backend CompanionController / relay):
 *   companion → host : {type:'dictation', text, isFinal} | {type:'command', command}
 *   host → companion : {type:'section_context', sectionKey, sectionTitle}
 *   server → either  : {type:'ack', ok, message?} | {type:'peer_joined', deviceName}
 *                      {type:'peer_left'} | {type:'session_ended'}
 *
 * No PHI is persisted by the relay — dictation is forwarded transiently and
 * applied on the desktop, where the usual validation / audit / signing rules
 * apply. RadioPad never auto-signs.
 */

import { companionWsBase, getActiveAuthToken } from './api';

export type CompanionRole = 'host' | 'companion';

export type CompanionCommand =
  | 'next_section'
  | 'prev_section'
  | 'jump_findings'
  | 'jump_impression'
  | 'new_line'
  | 'undo'
  | 'generate_impression'
  | 'insert' // legacy — dictation auto-inserts on final; kept for back-compat
  | 'read_back' // legacy — advisory, no-op on the host today
  | 'ptt_start'
  | 'ptt_stop';

/**
 * WebRTC signaling relayed (as plain JSON) between the two peers to establish a
 * direct, LAN-only data channel over which the phone streams voice audio to the
 * desktop. Only these tiny control messages touch the cloud relay — the audio
 * itself flows peer-to-peer and never leaves the local network. See
 * {@link ./companionRtc}.
 */
export type CompanionSignal =
  | { type: 'rtc_offer'; sdp: string }
  | { type: 'rtc_answer'; sdp: string }
  | { type: 'rtc_ice'; candidate: RTCIceCandidateInit | null } // null = end-of-candidates
  | { type: 'rtc_bye' };

export type CompanionMessage =
  | { type: 'ack'; ok: boolean; message?: string }
  | { type: 'pong' }
  | { type: 'dictation'; text: string; isFinal: boolean }
  | { type: 'command'; command: CompanionCommand }
  | { type: 'section_context'; sectionKey: string; sectionTitle: string }
  | { type: 'peer_joined'; deviceName: string }
  | { type: 'peer_left' }
  | { type: 'session_ended' }
  | CompanionSignal;

export type CompanionConnectionState = 'connecting' | 'open' | 'closed';

export interface CompanionConnectOptions {
  sessionId: string;
  role: CompanionRole;
  /** Relay path; defaults to `/ws/companion`. */
  wsPath?: string;
  onMessage?: (msg: CompanionMessage) => void;
  onOpen?: () => void;
  onClose?: (ev?: CloseEvent) => void;
  onError?: (ev: Event) => void;
}

export interface CompanionConnection {
  sendDictation(text: string, isFinal: boolean): void;
  sendCommand(command: CompanionCommand): void;
  sendSectionContext(sectionKey: string, sectionTitle: string): void;
  /** Relay a WebRTC signaling message to the peer (offer/answer/ICE). */
  sendSignal(signal: CompanionSignal): void;
  send(message: Record<string, unknown>): void;
  close(): void;
  state(): CompanionConnectionState;
}

const HEARTBEAT_INTERVAL_MS = 20_000;
const RECONNECT_DELAYS_MS = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000] as const;

function buildWsUrl(wsPath: string): string {
  const base = companionWsBase();
  const token = getActiveAuthToken();
  const q = token ? `?access_token=${encodeURIComponent(token)}` : '';
  // `base` is a ws(s):// origin; `wsPath` is an absolute path like /ws/companion.
  return `${base}${wsPath}${q}`;
}

/**
 * Open a companion relay connection. Returns immediately with a handle whose
 * send* methods buffer until the socket is open. Callbacks fire on the main
 * thread. Callers own the returned connection and must `close()` it.
 */
export function connectCompanion(opts: CompanionConnectOptions): CompanionConnection {
  const wsPath = opts.wsPath ?? '/ws/companion';
  let state: CompanionConnectionState = 'connecting';
  let closedByCaller = false;
  let reconnectAttempt = 0;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let terminalCloseNotified = false;
  const outbox: string[] = [];
  let heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  let ws: WebSocket;

  function stopHeartbeat() {
    if (heartbeatTimer !== null) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
  }

  function stopReconnect() {
    if (reconnectTimer !== null) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
  }

  function flush() {
    while (outbox.length && ws.readyState === WebSocket.OPEN) {
      ws.send(outbox.shift() as string);
    }
  }

  function enqueue(payload: Record<string, unknown>) {
    if (closedByCaller || terminalCloseNotified) return;
    outbox.push(JSON.stringify(payload));
    flush();
  }

  function notifyClosed(ev: CloseEvent) {
    if (terminalCloseNotified) return;
    terminalCloseNotified = true;
    state = 'closed';
    opts.onClose?.(ev);
  }

  function scheduleReconnect(ev: CloseEvent) {
    stopHeartbeat();
    if (closedByCaller) return;
    if (reconnectAttempt >= RECONNECT_DELAYS_MS.length) {
      notifyClosed(ev);
      return;
    }

    state = 'connecting';
    const delay = RECONNECT_DELAYS_MS[reconnectAttempt];
    reconnectAttempt += 1;
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      openSocket();
    }, delay);
  }

  function openSocket() {
    if (closedByCaller) return;
    const socket = new WebSocket(buildWsUrl(wsPath));
    ws = socket;

    socket.addEventListener('open', () => {
      reconnectAttempt = 0;
      state = 'open';
      // First frame is always the hello handshake.
      socket.send(JSON.stringify({ type: 'hello', role: opts.role, sessionId: opts.sessionId }));
      flush();
      heartbeatTimer = setInterval(() => {
        if (socket.readyState === WebSocket.OPEN) {
          socket.send('{"type":"ping"}');
        }
      }, HEARTBEAT_INTERVAL_MS);
      opts.onOpen?.();
    });

    socket.addEventListener('message', (ev) => {
      let msg: CompanionMessage | null = null;
      try {
        msg = JSON.parse(typeof ev.data === 'string' ? ev.data : '') as CompanionMessage;
      } catch {
        return; // ignore non-JSON frames
      }
      if (!msg || typeof msg.type !== 'string') return;

      opts.onMessage?.(msg);
      if (msg.type === 'session_ended' || (msg.type === 'ack' && !msg.ok)) {
        closedByCaller = true;
        stopReconnect();
        stopHeartbeat();
        state = 'closed';
        socket.close();
      }
    });

    socket.addEventListener('error', (ev) => {
      if (!closedByCaller) opts.onError?.(ev);
    });

    socket.addEventListener('close', (ev) => {
      scheduleReconnect(ev as CloseEvent);
    });
  }

  openSocket();

  return {
    sendDictation(text, isFinal) {
      enqueue({ type: 'dictation', text, isFinal });
    },
    sendCommand(command) {
      enqueue({ type: 'command', command });
    },
    sendSectionContext(sectionKey, sectionTitle) {
      enqueue({ type: 'section_context', sectionKey, sectionTitle });
    },
    sendSignal(signal) {
      enqueue(signal as unknown as Record<string, unknown>);
    },
    send(message) {
      enqueue(message);
    },
    close() {
      closedByCaller = true;
      state = 'closed';
      stopReconnect();
      stopHeartbeat();
      try {
        ws.close();
      } catch {
        /* already closing */
      }
    },
    state() {
      return state;
    },
  };
}
