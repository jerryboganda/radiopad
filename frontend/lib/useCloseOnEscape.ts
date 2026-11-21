'use client';

import { useEffect } from 'react';

/**
 * Escape closes a hand-rolled modal (a `role="dialog"` div rather than a native
 * `<dialog>`, which gets this for free). Pass `active = false` while the modal
 * is not shown, so the hook can sit unconditionally at the top of a component.
 */
export function useCloseOnEscape(onClose: () => void, active = true): void {
  useEffect(() => {
    if (!active) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose, active]);
}
