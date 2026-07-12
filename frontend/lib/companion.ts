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

export type CompanionMessage =
  | { type: 'ack'; ok: boolean; message?: string }
  | { type: 'dictation'; text: string; isFinal: boolean }
  | { type: 'command'; command: CompanionCommand }
  | { type: 'section_context'; sectionKey: string; sectionTitle: string }
  | { type: 'peer_joined'; deviceName: string }
  | { type: 'peer_left' }
  | { type: 'session_ended' };

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
  send(message: Record<string, unknown>): void;
  close(): void;
  state(): CompanionConnectionState;
}

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
  const outbox: string[] = [];
  const ws = new WebSocket(buildWsUrl(wsPath));

  function flush() {
    while (outbox.length && ws.readyState === WebSocket.OPEN) {
      ws.send(outbox.shift() as string);
    }
  }

  function enqueue(payload: Record<string, unknown>) {
    outbox.push(JSON.stringify(payload));
    flush();
  }

  ws.addEventListener('open', () => {
    state = 'open';
    // First frame is always the hello handshake.
    ws.send(JSON.stringify({ type: 'hello', role: opts.role, sessionId: opts.sessionId }));
    flush();
    opts.onOpen?.();
  });

  ws.addEventListener('message', (ev) => {
    let msg: CompanionMessage | null = null;
    try {
      msg = JSON.parse(typeof ev.data === 'string' ? ev.data : '') as CompanionMessage;
    } catch {
      return; // ignore non-JSON frames
    }
    if (msg && typeof msg.type === 'string') opts.onMessage?.(msg);
  });

  ws.addEventListener('error', (ev) => {
    opts.onError?.(ev);
  });

  ws.addEventListener('close', (ev) => {
    state = 'closed';
    if (!closedByCaller) opts.onClose?.(ev);
  });

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
    send(message) {
      enqueue(message);
    },
    close() {
      closedByCaller = true;
      state = 'closed';
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
