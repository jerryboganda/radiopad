import { describe, it, expect, vi, afterEach } from 'vitest';

afterEach(() => {
  vi.resetModules();
  delete (window as typeof window & { __TAURI__?: unknown }).__TAURI__;
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
});
