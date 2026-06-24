import { describe, it, expect, vi, afterEach } from 'vitest';

// A working in-memory `localStorage` is installed centrally in
// `__tests__/setup.ts` (jsdom here is launched with a broken
// `--localstorage-file` flag, so the built-in Storage methods are missing).
// The secureAuth web fallback below relies on it.

afterEach(() => {
  vi.resetModules();
  delete (window as typeof window & { __TAURI__?: unknown }).__TAURI__;
  delete (globalThis as typeof globalThis & { Capacitor?: unknown }).Capacitor;
  localStorage.clear();
});

describe('secureAuth desktop backend', () => {
  it('prefers Tauri keyring commands before mobile or web fallbacks', async () => {
    const invoke = vi.fn(async (cmd: string, args?: unknown) => {
      if (cmd === 'device_pairing_token_get') return 'rp_existing';
      if (cmd === 'device_pairing_token_set') {
        expect(args).toEqual({ token: 'rp_new' });
        return null;
      }
      if (cmd === 'device_pairing_token_clear') return null;
      throw new Error(`unexpected command ${cmd}`);
    });
    (window as typeof window & { __TAURI__?: unknown }).__TAURI__ = {
      core: { invoke },
    };

    const secureAuth = await import('../lib/secureAuth');
    expect(await secureAuth.isAuthTokenSecure()).toBe(true);
    expect(await secureAuth.getAuthToken()).toBe('rp_existing');

    await secureAuth.setAuthToken('rp_new');
    await secureAuth.clearAuthToken();

    expect(invoke).toHaveBeenCalledWith('device_pairing_token_get');
    expect(invoke).toHaveBeenCalledWith('device_pairing_token_set', { token: 'rp_new' });
    expect(invoke).toHaveBeenCalledWith('device_pairing_token_clear');
  });

  it('uses the web fallback without probing Capacitor plugins in regular browsers', async () => {
    const secureAuth = await import('../lib/secureAuth');

    expect(await secureAuth.isAuthTokenSecure()).toBe(false);

    await secureAuth.setAuthToken('rp_web');
    expect(await secureAuth.getAuthToken()).toBe('rp_web');

    await secureAuth.clearAuthToken();
    expect(await secureAuth.getAuthToken()).toBeNull();
  });
});
