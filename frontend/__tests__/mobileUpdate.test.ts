import { describe, it, expect, vi, afterEach } from 'vitest';
import { isNewerVersion, checkMobileUpdate } from '@/lib/mobileUpdate';

describe('isNewerVersion', () => {
  it('detects a newer version across each component', () => {
    expect(isNewerVersion('0.1.67', '0.1.66')).toBe(true);
    expect(isNewerVersion('0.2.0', '0.1.99')).toBe(true);
    expect(isNewerVersion('1.0.0', '0.9.9')).toBe(true);
  });
  it('is false for equal or older versions', () => {
    expect(isNewerVersion('0.1.66', '0.1.66')).toBe(false);
    expect(isNewerVersion('0.1.65', '0.1.66')).toBe(false);
    expect(isNewerVersion('0.0.9', '0.1.0')).toBe(false);
  });
  it('tolerates a leading v and uneven lengths', () => {
    expect(isNewerVersion('v0.1.67', '0.1.66')).toBe(true);
    expect(isNewerVersion('0.1', '0.1.0')).toBe(false);
    expect(isNewerVersion('0.1.0.1', '0.1.0')).toBe(true);
  });
});

describe('checkMobileUpdate', () => {
  afterEach(() => vi.unstubAllGlobals());

  function stubBackend(body: unknown, ok = true) {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok,
      status: ok ? 200 : 503,
      json: async () => body,
    })));
  }

  it('reports an available update with the APK download URL', async () => {
    stubBackend({ version: '0.1.67', apkUrl: 'https://x/apk', releaseUrl: 'https://x/rel' });
    const info = await checkMobileUpdate('0.1.66');
    expect(info.updateAvailable).toBe(true);
    expect(info.latest).toBe('0.1.67');
    expect(info.downloadUrl).toBe('https://x/apk');
  });

  it('reports up-to-date when the release matches the current build', async () => {
    stubBackend({ version: '0.1.66', apkUrl: null });
    const info = await checkMobileUpdate('0.1.66');
    expect(info.updateAvailable).toBe(false);
    expect(info.downloadUrl).toBeNull();
  });

  it('throws when the backend update check is unavailable', async () => {
    stubBackend({}, false);
    await expect(checkMobileUpdate('0.1.66')).rejects.toThrow();
  });
});
