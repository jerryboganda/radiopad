/**
 * Race a promise against a deadline. Used by the companion transcription
 * pipeline so a hung step (audio decode, engine call) becomes a visible,
 * skippable error instead of freezing the FIFO forever. The underlying work is
 * not cancelled (callers that can cancel should also pass an AbortSignal to the
 * work itself); this just guarantees the awaiting caller gets unstuck.
 */
export function raceTimeout<T>(work: Promise<T>, ms: number, message: string): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(message)), ms);
    work.then(
      (v) => { clearTimeout(timer); resolve(v); },
      (e) => { clearTimeout(timer); reject(e); },
    );
  });
}
