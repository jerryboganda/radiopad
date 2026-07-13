/**
 * Wire framing for audio segments sent over the companion WebRTC data channel.
 *
 * Each per-phrase webm segment is split into ≤16 KB chunks (a size every WebRTC
 * impl carries) with an 8-byte little-endian header so the desktop can reassemble
 * the exact bytes for a given `seq`, even if chunks interleave with another
 * segment's. Kept pure + separate from the RTCPeerConnection plumbing so the
 * byte math is unit-testable without a real WebRTC stack.
 *
 * Header layout: [seq: u32][index: u16][total: u16] then the chunk payload.
 */

export const HEADER_BYTES = 8;
export const CHUNK_PAYLOAD = 16000;
export const MAX_CHUNKS = 0xffff;

/** Split a segment's bytes into framed chunks (ready to `RTCDataChannel.send`). */
export function encodeSegmentFrames(bytes: Uint8Array, seq: number): ArrayBuffer[] {
  const total = Math.max(1, Math.ceil(bytes.length / CHUNK_PAYLOAD));
  const frames: ArrayBuffer[] = [];
  for (let index = 0; index < total; index += 1) {
    const slice = bytes.subarray(index * CHUNK_PAYLOAD, (index + 1) * CHUNK_PAYLOAD);
    const frame = new ArrayBuffer(HEADER_BYTES + slice.length);
    const view = new DataView(frame);
    view.setUint32(0, seq, true);
    view.setUint16(4, index, true);
    view.setUint16(6, total, true);
    new Uint8Array(frame).set(slice, HEADER_BYTES);
    frames.push(frame);
  }
  return frames;
}

export interface ReassembledSegment {
  seq: number;
  bytes: Uint8Array;
}

export interface SegmentReassembler {
  /** Feed one inbound framed chunk (the data-channel message bytes). */
  onFrame: (buf: ArrayBuffer) => void;
  clear: () => void;
}

/** Reassemble framed chunks back into complete segments, emitting each once all
 *  its chunks have arrived (in any order). A segment missing a chunk is dropped
 *  rather than emitted corrupt. */
export function createSegmentReassembler(
  onSegment: (segment: ReassembledSegment) => void,
): SegmentReassembler {
  const partials = new Map<number, { total: number; received: Map<number, Uint8Array> }>();
  return {
    onFrame(buf) {
      if (buf.byteLength < HEADER_BYTES) return;
      const view = new DataView(buf);
      const seq = view.getUint32(0, true);
      const index = view.getUint16(4, true);
      const total = view.getUint16(6, true);
      if (total === 0) return;
      const payload = new Uint8Array(buf.slice(HEADER_BYTES));

      let p = partials.get(seq);
      if (!p) { p = { total, received: new Map() }; partials.set(seq, p); }
      p.received.set(index, payload);
      if (p.received.size < p.total) return;

      partials.delete(seq);
      let length = 0;
      const parts: Uint8Array[] = [];
      for (let i = 0; i < p.total; i += 1) {
        const chunk = p.received.get(i);
        if (!chunk) return; // missing chunk — drop rather than corrupt
        parts.push(chunk);
        length += chunk.length;
      }
      const bytes = new Uint8Array(length);
      let offset = 0;
      for (const part of parts) { bytes.set(part, offset); offset += part.length; }
      onSegment({ seq, bytes });
    },
    clear() {
      partials.clear();
    },
  };
}
