// P0.3 — the dictation hotkey must fire on the configured chord, follow a rebind, and stop after
// uninstall. This is what makes the shortcut work on web + rebindable app-wide.
import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  matchesDictationHotkey,
  dictationBinding,
  installDictationHotkey,
} from '@/lib/dictationHotkey';
import { setBinding, resetAll } from '@/lib/hotkeys';

afterEach(() => {
  resetAll();
});

function keydown(init: KeyboardEventInit): KeyboardEvent {
  return new KeyboardEvent('keydown', init);
}

describe('matchesDictationHotkey', () => {
  it('defaults to Ctrl+Shift+D', () => {
    expect(dictationBinding()).toBe('Ctrl+Shift+D');
    expect(matchesDictationHotkey(keydown({ key: 'd', ctrlKey: true, shiftKey: true }))).toBe(true);
  });

  it('does not match a partial or different chord', () => {
    expect(matchesDictationHotkey(keydown({ key: 'd', ctrlKey: true }))).toBe(false); // no Shift
    expect(matchesDictationHotkey(keydown({ key: 'k', ctrlKey: true }))).toBe(false);
    expect(matchesDictationHotkey(keydown({ key: 'Shift', shiftKey: true }))).toBe(false); // modifier only
  });

  it('follows a rebind', () => {
    setBinding('dictation-toggle', 'Ctrl+Shift+M');
    expect(matchesDictationHotkey(keydown({ key: 'm', ctrlKey: true, shiftKey: true }))).toBe(true);
    expect(matchesDictationHotkey(keydown({ key: 'd', ctrlKey: true, shiftKey: true }))).toBe(false);
  });
});

describe('installDictationHotkey', () => {
  it('dispatches on the configured chord and stops after unlisten', () => {
    const dispatch = vi.fn();
    const uninstall = installDictationHotkey(dispatch);

    window.dispatchEvent(keydown({ key: 'd', ctrlKey: true, shiftKey: true }));
    expect(dispatch).toHaveBeenCalledTimes(1);

    window.dispatchEvent(keydown({ key: 'x' })); // unrelated key
    expect(dispatch).toHaveBeenCalledTimes(1);

    uninstall();
    window.dispatchEvent(keydown({ key: 'd', ctrlKey: true, shiftKey: true }));
    expect(dispatch).toHaveBeenCalledTimes(1); // no more after uninstall
  });
});
