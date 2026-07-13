import { describe, it, expect } from 'vitest';
import {
  encodeSegmentFrames,
  createSegmentReassembler,
  CHUNK_PAYLOAD,
  type ReassembledSegment,
} from '@/lib/companionAudioFrames';

describe('companion audio frames', () => {
  it('round-trips a small single-chunk segment', () => {
    const data = new Uint8Array([1, 2, 3, 4, 5]);
    const frames = encodeSegmentFrames(data, 7);
    expect(frames.length).toBe(1);

    let got: ReassembledSegment | null = null;
    const r = createSegmentReassembler((s) => { got = s; });
    frames.forEach((f) => r.onFrame(f));
    expect(got!.seq).toBe(7);
    expect(Array.from(got!.bytes)).toEqual([1, 2, 3, 4, 5]);
  });

  it('round-trips a multi-chunk segment preserving bytes exactly', () => {
    const n = CHUNK_PAYLOAD * 2 + 123;
    const data = new Uint8Array(n);
    for (let i = 0; i < n; i += 1) data[i] = (i * 31) & 0xff;

    const frames = encodeSegmentFrames(data, 42);
    expect(frames.length).toBe(3);

    let got: ReassembledSegment | null = null;
    const r = createSegmentReassembler((s) => { got = s; });
    frames.forEach((f) => r.onFrame(f));
    expect(got!.seq).toBe(42);
    expect(got!.bytes.length).toBe(n);
    expect(Buffer.from(got!.bytes).equals(Buffer.from(data))).toBe(true);
  });

  it('reassembles chunks that arrive out of order', () => {
    const n = CHUNK_PAYLOAD * 3 + 7;
    const data = new Uint8Array(n);
    for (let i = 0; i < n; i += 1) data[i] = i % 251;

    const frames = encodeSegmentFrames(data, 1);
    let got: ReassembledSegment | null = null;
    const r = createSegmentReassembler((s) => { got = s; });
    [3, 0, 2, 1].forEach((i) => r.onFrame(frames[i]));

    expect(got!.bytes.length).toBe(n);
    expect(Buffer.from(got!.bytes).equals(Buffer.from(data))).toBe(true);
  });

  it('does not emit while a chunk is still missing', () => {
    const frames = encodeSegmentFrames(new Uint8Array(CHUNK_PAYLOAD * 2), 5);
    let emitted = 0;
    const r = createSegmentReassembler(() => { emitted += 1; });
    r.onFrame(frames[0]); // only the first of two chunks
    expect(emitted).toBe(0);
  });

  it('keeps two interleaved segments separate', () => {
    const a = encodeSegmentFrames(new Uint8Array([10, 11, 12]), 0);
    const b = encodeSegmentFrames(new Uint8Array([20, 21]), 1);
    const seen: ReassembledSegment[] = [];
    const r = createSegmentReassembler((s) => seen.push(s));
    r.onFrame(b[0]);
    r.onFrame(a[0]);
    expect(seen.map((s) => s.seq).sort()).toEqual([0, 1]);
    const byId = new Map(seen.map((s) => [s.seq, Array.from(s.bytes)]));
    expect(byId.get(0)).toEqual([10, 11, 12]);
    expect(byId.get(1)).toEqual([20, 21]);
  });
});
