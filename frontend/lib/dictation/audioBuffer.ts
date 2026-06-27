'use client';

// In-memory retention of the last dictation session's audio (and the transcript
// it produced), keyed by report id, so the manual Cross Check can re-run the SAME
// audio through the extra engines. Deliberately NOT persisted to disk — that would
// create a new PHI-at-rest surface. ~1 MB/min for webm/opus 16k mono; capped at
// the backend's 32 MiB upload limit. Cleared on a new recording, a successful
// cross-check, or navigation away.

const MAX_BYTES = 33_554_432; // 32 MiB — mirrors the backend upload cap

export interface SessionAudio {
  blob: Blob;
  /** The (formatted) transcript this audio produced — the cross-check backbone. */
  transcript: string;
  /** Report section the transcript was inserted into (cross-check target). */
  sectionKey: string;
}

const store = new Map<string, SessionAudio>();

/** Retain the audio + its transcript for a report. Returns false if too large/empty. */
export function setSessionAudio(
  reportId: string,
  blob: Blob,
  transcript: string,
  sectionKey: string,
): boolean {
  if (!reportId || blob.size === 0 || blob.size > MAX_BYTES) return false;
  store.set(reportId, { blob, transcript, sectionKey });
  return true;
}

export function getSessionAudio(reportId: string): SessionAudio | null {
  return store.get(reportId) ?? null;
}

export function hasSessionAudio(reportId: string): boolean {
  return store.has(reportId);
}

export function clearSessionAudio(reportId: string): void {
  store.delete(reportId);
}

/** Test-only reset. */
export function _resetAudioBuffer(): void {
  store.clear();
}
