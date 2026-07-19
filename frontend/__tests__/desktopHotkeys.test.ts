import { afterEach, describe, expect, it, vi } from 'vitest';
import { installDesktopHotkeySync, syncDesktopHotkeys } from '@/lib/desktopHotkeys';
import { HOTKEYS_CHANGE_EVENT, resetAll, setBinding } from '@/lib/hotkeys';

/** Install a fake Tauri bridge and capture what the shell was asked to register. */
function mockTauri(supported: string[]) {
  const calls: { cmd: string; args?: Record<string, unknown> }[] = [];
  const invoke = vi.fn(async (cmd: string, args?: Record<string, unknown>) => {
    calls.push({ cmd, args });
    if (cmd === 'hotkeys_supported_actions') return supported;
    return [];
  });
  (window as unknown as { __TAURI__?: unknown }).__TAURI__ = { core: { invoke } };
  return { calls, invoke };
}

afterEach(() => {
  delete (window as unknown as { __TAURI__?: unknown }).__TAURI__;
  resetAll(); // clears both localStorage and the module's in-memory override cache
  vi.restoreAllMocks();
});

describe('syncDesktopHotkeys', () => {
  it('is a no-op without a Tauri bridge (web and mobile surfaces)', async () => {
    expect(await syncDesktopHotkeys()).toEqual([]);
  });

  it('sends only the actions the shell says it implements', async () => {
    const { calls } = mockTauri(['dictation-toggle', 'new-report']);

    await syncDesktopHotkeys();

    const apply = calls.find((c) => c.cmd === 'hotkeys_apply');
    expect(apply).toBeDefined();
    const bindings = apply!.args!.bindings as { id: string }[];
    expect(bindings.map((b) => b.id).sort()).toEqual(['dictation-toggle', 'new-report']);
  });

  it('never sends an unimplemented or in-page-only action', async () => {
    // 'command-palette' is implemented but in-page only, so the shell does not list it; the
    // "Coming soon" entries are not implemented at all. Sending either would claim a system-wide
    // chord that does nothing.
    const { calls } = mockTauri(['dictation-toggle']);

    await syncDesktopHotkeys();

    const bindings = calls.find((c) => c.cmd === 'hotkeys_apply')!.args!.bindings as { id: string }[];
    expect(bindings.map((b) => b.id)).not.toContain('command-palette');
    expect(bindings.map((b) => b.id)).not.toContain('toggle-theme');
  });

  it('sends the user override rather than the default when one is set', async () => {
    // Go through the public setter, as the settings UI does: hotkeys.ts memoizes overrides, so a
    // raw localStorage write would not be observed.
    setBinding('dictation-toggle', 'Ctrl+Alt+M');
    const { calls } = mockTauri(['dictation-toggle']);

    await syncDesktopHotkeys();

    const bindings = calls.find((c) => c.cmd === 'hotkeys_apply')!.args!.bindings as {
      id: string;
      accelerator: string;
    }[];
    expect(bindings.find((b) => b.id === 'dictation-toggle')?.accelerator).toBe('Ctrl+Alt+M');
  });

  it('swallows a bridge failure so a broken shell never breaks the app shell', async () => {
    const invoke = vi.fn(async () => {
      throw new Error('command not found');
    });
    (window as unknown as { __TAURI__?: unknown }).__TAURI__ = { core: { invoke } };

    await expect(syncDesktopHotkeys()).resolves.toEqual([]);
  });
});

describe('installDesktopHotkeySync', () => {
  it('re-syncs when the bindings change, and stops after disposal', async () => {
    const { invoke } = mockTauri(['dictation-toggle']);

    const dispose = installDesktopHotkeySync();
    await vi.waitFor(() => expect(invoke).toHaveBeenCalled());
    const afterInitial = invoke.mock.calls.length;

    window.dispatchEvent(new CustomEvent(HOTKEYS_CHANGE_EVENT));
    await vi.waitFor(() => expect(invoke.mock.calls.length).toBeGreaterThan(afterInitial));
    const afterChange = invoke.mock.calls.length;

    dispose();
    window.dispatchEvent(new CustomEvent(HOTKEYS_CHANGE_EVENT));
    await new Promise((r) => setTimeout(r, 20));
    expect(invoke.mock.calls.length).toBe(afterChange);
  });
});
