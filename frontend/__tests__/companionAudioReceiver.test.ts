import { describe, it, expect, vi } from 'vitest';
import { createAudioReceiver } from '@/lib/companionAudioReceiver';

const WORDS = ['alpha', 'bravo', 'charlie', 'delta', 'echo'];

/** Build a segment blob whose seq we track by reference (jsdom Blob lacks .text()). */
function makeSegments(n: number): { blobs: Blob[]; seqOf: Map<Blob, number> } {
  const seqOf = new Map<Blob, number>();
  const blobs = Array.from({ length: n }, (_, i) => {
    const b = new Blob([`seg-${i}`]);
    seqOf.set(b, i);
    return b;
  });
  return { blobs, seqOf };
}

describe('companion audio receiver — FIFO transcription order', () => {
  it('inserts strictly in seq order despite out-of-order arrival AND inverse latency', async () => {
    const { blobs, seqOf } = makeSegments(5);
    const inserted: string[] = [];
    const receiver = createAudioReceiver({
      // Later segments "transcribe" FASTER — the queue must still commit in
      // capture order, never letting a later phrase overtake an earlier one.
      transcribe: async (b) => {
        const seq = seqOf.get(b)!;
        await new Promise((r) => setTimeout(r, (WORDS.length - seq) * 8));
        return WORDS[seq];
      },
      insert: (t) => inserted.push(t),
    });

    [2, 0, 4, 1, 3].forEach((s) => receiver.pushSegment(blobs[s], s));

    await vi.waitFor(() => expect(inserted.length).toBe(5), { timeout: 3000 });
    expect(inserted).toEqual(WORDS);
  });

  it('skips blank transcripts and ends not-busy', async () => {
    const { blobs, seqOf } = makeSegments(3);
    const inserted: string[] = [];
    const busy: boolean[] = [];
    const receiver = createAudioReceiver({
      transcribe: async (b) => { const s = seqOf.get(b)!; return s === 1 ? '   ' : `w${s}`; },
      insert: (t) => inserted.push(t),
      onBusyChange: (b) => busy.push(b),
    });
    [0, 1, 2].forEach((s) => receiver.pushSegment(blobs[s], s));
    await vi.waitFor(() => expect(inserted.length).toBe(2), { timeout: 2000 });
    expect(inserted).toEqual(['w0', 'w2']); // seq 1 (blank) inserted nothing
    expect(busy).toContain(true);
    expect(busy[busy.length - 1]).toBe(false);
  });

  it('drops a phrase whose transcription resolves AFTER reset() (dead session)', async () => {
    const { blobs } = makeSegments(1);
    const inserted: string[] = [];
    let resolveTranscribe: (v: string) => void = () => {};
    const receiver = createAudioReceiver({
      transcribe: () => new Promise<string>((res) => { resolveTranscribe = res; }),
      insert: (t) => inserted.push(t),
    });
    receiver.pushSegment(blobs[0], 0);
    await new Promise((r) => setTimeout(r, 10)); // let drain reach `await transcribe`
    receiver.reset(); // session torn down mid-transcription
    resolveTranscribe('late phrase');
    await new Promise((r) => setTimeout(r, 20));
    expect(inserted).toEqual([]); // must NOT insert into the new/ended session
  });

  it('ignores duplicate / already-processed segments', async () => {
    const { blobs, seqOf } = makeSegments(1);
    const inserted: string[] = [];
    const receiver = createAudioReceiver({
      transcribe: async (b) => `t${seqOf.get(b)}`,
      insert: (t) => inserted.push(t),
    });
    receiver.pushSegment(blobs[0], 0);
    await vi.waitFor(() => expect(inserted.length).toBe(1), { timeout: 2000 });
    receiver.pushSegment(blobs[0], 0); // duplicate of an already-processed seq
    await new Promise((r) => setTimeout(r, 30));
    expect(inserted).toEqual(['t0']);
  });
});
