'use client';

// Feature flag for the rich (Tiptap) report editor. Now ON BY DEFAULT (opt-out):
// the cross-check feature is shipped. Disable per-build with
// `NEXT_PUBLIC_RICH_EDITOR=0`, or per-machine with localStorage
// `radiopad:rich-editor=0` (to fall back to the plain-textarea editor).

import { useEffect, useState } from 'react';

const KEY = 'radiopad:rich-editor';

export function isRichEditorEnabled(): boolean {
  if (process.env.NEXT_PUBLIC_RICH_EDITOR === '0') return false; // explicit opt-out
  if (process.env.NEXT_PUBLIC_RICH_EDITOR === '1') return true;
  if (typeof window === 'undefined') return true; // default on
  try {
    return window.localStorage.getItem(KEY) !== '0';
  } catch {
    return true;
  }
}

/**
 * Client hook for the flag. Resolves on the client (the report page is client-
 * rendered and the editor only mounts after the report loads, so there is no SSR
 * flash); seeds `false` only on the server to keep the first render deterministic.
 */
export function useRichEditorEnabled(): boolean {
  const [on, setOn] = useState(() => (typeof window === 'undefined' ? false : isRichEditorEnabled()));
  useEffect(() => {
    setOn(isRichEditorEnabled());
  }, []);
  return on;
}
