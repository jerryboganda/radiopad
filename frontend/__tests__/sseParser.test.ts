import { describe, it, expect, vi } from 'vitest';
import { SseParser, type SseEvent } from '@/lib/sse';

// Pure incremental SSE parser (durable async-job platform). No React, no network:
// feed raw bytes, assert the events/comments it emits. `TextEncoder` gives us the
// exact byte stream a `ReadableStream` reader would hand `feed`.

const enc = new TextEncoder();

/** Collect events + comment counts from a fresh parser driven by `feed`. */
function drive(feed: (p: SseParser) => void): { events: SseEvent[]; comments: number } {
  const events: SseEvent[] = [];
  let comments = 0;
  const parser = new SseParser((e) => events.push(e), () => {
    comments += 1;
  });
  feed(parser);
  return { events, comments };
}

describe('SseParser', () => {
  it('parses a single event with event/data/id', () => {
    const { events } = drive((p) => p.feed(enc.encode('id: 42\nevent: job\ndata: {"jobId":"j1"}\n\n')));
    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: 'job', data: '{"jobId":"j1"}', id: '42' });
  });

  it('joins multi-line data fields with newlines', () => {
    const { events } = drive((p) => p.feed(enc.encode('data: a\ndata: b\n\n')));
    expect(events).toHaveLength(1);
    expect(events[0].data).toBe('a\nb');
  });

  it('handles CRLF and LF line endings identically', () => {
    const crlf = drive((p) => p.feed(enc.encode('event: ping\r\ndata: hello\r\n\r\n')));
    const lf = drive((p) => p.feed(enc.encode('event: ping\ndata: hello\n\n')));
    expect(crlf.events).toEqual(lf.events);
    expect(crlf.events[0]).toEqual({ event: 'ping', data: 'hello' });
  });

  it('dispatches only on a blank line — a partial event is held', () => {
    const events: SseEvent[] = [];
    const parser = new SseParser((e) => events.push(e));
    parser.feed(enc.encode('event: job\ndata: partial'));
    expect(events).toHaveLength(0); // no separator yet
    parser.feed(enc.encode('\n\n'));
    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: 'job', data: 'partial' });
  });

  it('handles a UTF-8 multibyte sequence split across chunk boundaries', () => {
    // 'café 😀' mixes a 2-byte (é) and a 4-byte (😀) codepoint; feeding one byte at
    // a time forces every multibyte sequence to straddle a chunk boundary.
    const bytes = enc.encode('data: café 😀\n\n');
    const events: SseEvent[] = [];
    const parser = new SseParser((e) => events.push(e));
    for (const b of bytes) parser.feed(new Uint8Array([b]));
    expect(events).toHaveLength(1);
    expect(events[0].data).toBe('café 😀');
  });

  it('treats a `:`-prefixed line as a keep-alive comment, never an event', () => {
    const onEvent = vi.fn();
    let comments = 0;
    const parser = new SseParser(onEvent, () => {
      comments += 1;
    });
    parser.feed(enc.encode(': keep-alive\n\n'));
    expect(comments).toBe(1);
    expect(onEvent).not.toHaveBeenCalled();
  });

  it('defaults the event name to "message" when no event: line was seen', () => {
    const { events } = drive((p) => p.feed(enc.encode('data: bare\n\n')));
    expect(events).toHaveLength(1);
    expect(events[0].event).toBe('message');
    expect(events[0].data).toBe('bare');
  });

  it('carries the last-seen id forward and omits id when never set', () => {
    const { events } = drive((p) =>
      p.feed(enc.encode('id: 7\ndata: first\n\ndata: second\n\n')),
    );
    expect(events).toHaveLength(2);
    expect(events[0].id).toBe('7');
    // Second event has no id: line — spec keeps the last id.
    expect(events[1].id).toBe('7');
    const bare = drive((p) => p.feed(enc.encode('data: x\n\n')));
    expect(bare.events[0].id).toBeUndefined();
  });
});
