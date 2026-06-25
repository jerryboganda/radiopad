// Tracks the last text field the user focused. The dictation overlay needs this
// because clicking its mic button moves focus to the button — so at insertion
// time `document.activeElement` is the button, not the field the radiologist was
// typing in. We listen on `focusin` (capture) and remember the most recent
// editable target instead.

type EditableEl = HTMLTextAreaElement | HTMLInputElement;

// type="" defaults to "text"; these are the input types that accept free prose.
const TEXTUAL_INPUT_TYPES = new Set([
  'text', 'search', 'url', 'tel', 'email', 'password', 'number',
]);

let lastEditable: EditableEl | null = null;

export function isDictationTarget(el: EventTarget | null): el is EditableEl {
  if (el instanceof HTMLTextAreaElement) return true;
  if (el instanceof HTMLInputElement) {
    return TEXTUAL_INPUT_TYPES.has((el.type || 'text').toLowerCase());
  }
  return false;
}

function handleFocusIn(event: FocusEvent): void {
  if (isDictationTarget(event.target)) {
    lastEditable = event.target;
  }
}

export function startFocusTracking(): () => void {
  if (typeof document === 'undefined') return () => {};
  document.addEventListener('focusin', handleFocusIn, true);
  return () => document.removeEventListener('focusin', handleFocusIn, true);
}

/** The last focused editable, or null if there isn't one (or it left the DOM). */
export function getLastFocusedEditable(): EditableEl | null {
  if (lastEditable && !lastEditable.isConnected) lastEditable = null;
  return lastEditable;
}

/** Test-only reset of module state. */
export function _resetFocusTracker(): void {
  lastEditable = null;
}
