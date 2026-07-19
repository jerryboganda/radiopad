// Push the user's effective hotkey bindings down to the desktop shell's OS-level registration.
//
// `lib/hotkeys.ts` owns the catalog, the defaults, and the per-device overrides. Those overrides
// already drive the in-page keydown listeners, but the SYSTEM-WIDE shortcuts were registered from a
// hardcoded list in Rust — so rebinding "Start dictation" left the old chord firing whenever the
// RadioPad window was unfocused, which is precisely when a global hotkey matters. This module keeps
// the two in step: on load, and again on every `rp-hotkeys-change`, it hands the current bindings
// to the `hotkeys_apply` Tauri command, which re-registers them with the OS.
//
// No-op off the desktop surface (web/mobile have no Tauri bridge and no global shortcuts).

import { HOTKEYS, HOTKEYS_CHANGE_EVENT, getBindings } from './hotkeys';

type TauriInvoke = <T>(cmd: string, args?: Record<string, unknown>) => Promise<T>;

/** Per-binding outcome from the shell; `ok: false` means the OS refused the chord. */
export interface DesktopHotkeyResult {
  id: string;
  accelerator: string;
  ok: boolean;
  error: string | null;
}

function tauriInvoke(): TauriInvoke | undefined {
  const tauri = (
    window as unknown as {
      __TAURI__?: { core?: { invoke?: TauriInvoke }; invoke?: TauriInvoke };
    }
  ).__TAURI__;
  return tauri?.core?.invoke ?? tauri?.invoke;
}

/**
 * Register the current bindings with the OS. Resolves to the per-binding results, or `[]` when
 * there is no desktop shell to talk to.
 *
 * Only actions the shell actually implements are sent: the shell is asked which those are rather
 * than us duplicating the list here, so an id that exists only in the frontend catalog (the
 * "Coming soon" entries, or the in-page-only command palette) never claims a system-wide chord
 * that would then do nothing.
 */
export async function syncDesktopHotkeys(): Promise<DesktopHotkeyResult[]> {
  const invoke = tauriInvoke();
  if (!invoke) return [];

  try {
    const supported = await invoke<string[]>('hotkeys_supported_actions');
    const supportedSet = new Set(supported);
    const effective = getBindings();

    const bindings = HOTKEYS.filter((h) => h.implemented && supportedSet.has(h.id))
      .map((h) => ({ id: h.id, accelerator: effective[h.id] }))
      .filter((b) => Boolean(b.accelerator));

    if (bindings.length === 0) return [];
    return await invoke<DesktopHotkeyResult[]>('hotkeys_apply', { bindings });
  } catch {
    // An older shell without these commands, or a transient bridge failure. The shell keeps its
    // built-in defaults registered, so shortcuts still work — just not the custom ones.
    return [];
  }
}

/**
 * Sync now and on every subsequent binding change. Returns a disposer for effect cleanup.
 */
export function installDesktopHotkeySync(): () => void {
  void syncDesktopHotkeys();
  const onChange = () => {
    void syncDesktopHotkeys();
  };
  window.addEventListener(HOTKEYS_CHANGE_EVENT, onChange);
  return () => window.removeEventListener(HOTKEYS_CHANGE_EVENT, onChange);
}
