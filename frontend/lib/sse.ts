/**
 * Incremental Server-Sent-Events parser (durable async-job platform, 2026-07-23).
 *
 * Deliberately React-free and dependency-free so it can be unit-tested in
 * isolation and reused by both the hosted event stream and the desktop sidecar
 * loopback stream. Feed it raw bytes off a `ReadableStream` reader; it emits one
 * `SseEvent` per complete event (blank-line separated) and invokes `onComment`
 * for every `:`-prefixed keep-alive line.
 *
 * Conformance notes (WHATWG event-stream parsing, the parts we rely on):
 *  - CRLF, LF are treated identically (a trailing `\r` is stripped per line).
 *  - Multiple `data:` fields in one event are joined with `\n`.
 *  - A line starting with `:` is a comment — surfaced via `onComment`, never an
 *    event (this is how the server's 15s `: keep-alive` heartbeats drive the
 *    manager's liveness timer).
 *  - An event is dispatched ONLY on a blank-line separator; a partial event is
 *    held in the buffers until then.
 *  - The default event name is `message` when no `event:` line was seen.
 *  - `id:` is captured into `SseEvent.id`. Nothing consumes it yet — the manager
 *    deliberately does NOT implement Last-Event-ID resume (the server bus is a
 *    drop-oldest channel that cannot replay) — but the parser stays spec-complete
 *    so a future resume needs no parser change.
 *  - UTF-8 multibyte sequences split across chunk boundaries are decoded
 *    correctly by keeping ONE streaming `TextDecoder` across `feed` calls
 *    (`{ stream: true }`); decoding each chunk independently would corrupt a
 *    codepoint straddling the boundary.
 */

export interface SseEvent {
  event: string;
  data: string;
  id?: string;
}

const NULL_CHAR = String.fromCharCode(0);

export class SseParser {
  private decoder = new TextDecoder('utf-8');
  /** Carry for an incomplete trailing line between `feed` calls. */
  private lineBuffer = '';
  /** Accumulated `data:` values for the event under construction. */
  private dataBuffer = '';
  /** The `event:` value for the event under construction ('' = default). */
  private eventType = '';
  /** Last-Event-ID buffer — persists across events per the spec ('' = none). */
  private lastEventId = '';

  constructor(
    private readonly onEvent: (e: SseEvent) => void,
    private readonly onComment?: () => void,
  ) {}

  feed(chunk: Uint8Array): void {
    // `stream: true` lets the decoder hold a partial multibyte sequence until
    // the next chunk supplies its remaining bytes.
    this.lineBuffer += this.decoder.decode(chunk, { stream: true });
    let newlineIndex: number;
    while ((newlineIndex = this.lineBuffer.indexOf('\n')) >= 0) {
      let line = this.lineBuffer.slice(0, newlineIndex);
      this.lineBuffer = this.lineBuffer.slice(newlineIndex + 1);
      if (line.endsWith('\r')) line = line.slice(0, -1); // normalise CRLF -> LF
      this.processLine(line);
    }
  }

  reset(): void {
    this.decoder = new TextDecoder('utf-8');
    this.lineBuffer = '';
    this.dataBuffer = '';
    this.eventType = '';
    this.lastEventId = '';
  }

  private processLine(line: string): void {
    if (line === '') {
      this.dispatch();
      return;
    }
    if (line.startsWith(':')) {
      this.onComment?.();
      return;
    }
    let field: string;
    let value: string;
    const colon = line.indexOf(':');
    if (colon === -1) {
      field = line;
      value = '';
    } else {
      field = line.slice(0, colon);
      value = line.slice(colon + 1);
      // A single leading space after the colon is part of the delimiter.
      if (value.startsWith(' ')) value = value.slice(1);
    }
    switch (field) {
      case 'event':
        this.eventType = value;
        break;
      case 'data':
        this.dataBuffer += value + '\n';
        break;
      case 'id':
        // Per spec, an id containing a U+0000 NULL is ignored.
        if (!value.includes(NULL_CHAR)) this.lastEventId = value;
        break;
      // `retry` and any unknown field are ignored (the manager owns backoff).
      default:
        break;
    }
  }

  private dispatch(): void {
    if (this.dataBuffer === '') {
      // No data => no event (this is the case for a lone keep-alive comment
      // followed by its blank line). Reset the event-type buffer per spec.
      this.eventType = '';
      return;
    }
    let data = this.dataBuffer;
    if (data.endsWith('\n')) data = data.slice(0, -1); // drop the trailing joiner
    const event: SseEvent = {
      event: this.eventType || 'message',
      data,
    };
    if (this.lastEventId !== '') event.id = this.lastEventId;
    // Reset the per-event buffers; lastEventId persists per spec.
    this.dataBuffer = '';
    this.eventType = '';
    this.onEvent(event);
  }
}
