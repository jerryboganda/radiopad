// P0.3 — rebindable dictation hotkey (in-app / focused window).
//
// The desktop shell ALSO registers a system-wide global shortcut Rust-side (fires even when
// RadioPad is unfocused). This JS listener honours the user's CONFIGURED binding whenever the
// window has focus — on every surface, including web, where there is no Rust shortcut at all — so
// rebinding the dictation chord in Settings actually takes effect.
//
// No double-fire: a chord the OS has claimed as a registered global shortcut is consumed before it
// reaches the webview, so only a non-global (i.e. rebound) chord ever reaches this listener.

import {
  HOTKEYS,
  bindingFromKeyboardEvent,
  normalizeBinding,
  getBindings,
} from './hotkeys';

const DICTATION_ID = 'dictation-toggle';
const DEFAULT_BINDING =
  HOTKEYS.find((h) => h.id === DICTATION_ID)?.defaultBinding ?? 'Ctrl+Shift+D';

export const DICTATE_EVENT = 'radiopad:dictate';

/** The effective (custom-or-default) dictation-toggle chord, normalised. */
export function dictationBinding(bindings: Record<string, string> = getBindings()): string {
  return normalizeBinding(bindings[DICTATION_ID] ?? DEFAULT_BINDING);
}

/** Whether a keydown matches the configured dictation-toggle chord. */
export function matchesDictationHotkey(
  e: KeyboardEvent,
  bindings: Record<string, string> = getBindings(),
): boolean {
  const chord = bindingFromKeyboardEvent(e); // already normalised
  if (!chord) return false;
  return chord === dictationBinding(bindings);
}

function defaultDispatch(): void {
  window.dispatchEvent(new CustomEvent(DICTATE_EVENT));
}

/**
 * Install a global keydown listener that fires the dictation toggle on the configured chord. Reads
 * the binding live on each keypress, so a rebind in Settings takes effect immediately with no
 * re-install. Returns an unlisten. `dispatch` is injectable for tests.
 */
export function installDictationHotkey(dispatch: () => void = defaultDispatch): () => void {
  if (typeof window === 'undefined') return () => {};
  const onKeyDown = (e: KeyboardEvent) => {
    if (matchesDictationHotkey(e)) {
      e.preventDefault();
      dispatch();
    }
  };
  window.addEventListener('keydown', onKeyDown);
  return () => window.removeEventListener('keydown', onKeyDown);
}
