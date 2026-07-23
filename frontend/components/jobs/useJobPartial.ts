'use client';

/**
 * Read a job's live streamed partial text (durable async-job platform,
 * 2026-07-23). Subscribes to the module-level {@link jobPartials} store via
 * `useSyncExternalStore`, so a component re-renders only when that job's throttled
 * buffer notifies — never on every token, and never through the jobs reducer.
 *
 * Pass `null` to opt out (no subscription, always `undefined`) — handy when the
 * preview is conditionally mounted.
 */

import { useCallback, useSyncExternalStore } from 'react';
import { jobPartials, type JobStreamBuffer } from '@/lib/jobStream';

export function useJobPartial(jobId: string | null): JobStreamBuffer | undefined {
  const subscribe = useCallback(
    (onStoreChange: () => void) => {
      if (jobId == null) return () => {};
      return jobPartials.subscribe(jobId, onStoreChange);
    },
    [jobId],
  );
  const getSnapshot = useCallback(
    () => (jobId == null ? undefined : jobPartials.getSnapshot(jobId)),
    [jobId],
  );
  // The buffer is in-memory only, so the server snapshot is always `undefined`
  // (no data at SSR) — the same getter is safe for both.
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
