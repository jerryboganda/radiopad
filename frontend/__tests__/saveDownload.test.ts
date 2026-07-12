import { afterEach, describe, expect, it, vi } from 'vitest';
import { saveDownload } from '@/lib/saveDownload';

afterEach(() => {
  delete (window as typeof window & { __TAURI__?: unknown }).__TAURI__;
  vi.useRealTimers();
});

describe('saveDownload', () => {
  it('uses the native Save As command with the complete byte payload', async () => {
    const invoke = vi.fn().mockResolvedValue(true);
    (window as typeof window & { __TAURI__?: unknown }).__TAURI__ = { core: { invoke } };

    const result = await saveDownload(new Blob(['RadioPad export']), 'report.txt');

    expect(result).toBe('saved');
    expect(invoke).toHaveBeenCalledWith('save_export_file', {
      fileName: 'report.txt',
      bytes: Array.from(new TextEncoder().encode('RadioPad export')),
    });
  });

  it('treats closing the native Save As dialog as cancellation', async () => {
    const invoke = vi.fn().mockResolvedValue(false);
    (window as typeof window & { __TAURI__?: unknown }).__TAURI__ = { core: { invoke } };

    await expect(saveDownload(new Blob(['x']), 'report.txt')).resolves.toBe('cancelled');
  });

  it('uses an attached anchor and delayed URL cleanup in a browser', async () => {
    vi.useFakeTimers();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    Object.defineProperty(URL, 'createObjectURL', { value: vi.fn(), configurable: true });
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:report');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    await expect(saveDownload(new Blob(['x']), 'report.txt')).resolves.toBe('saved');
    expect(click).toHaveBeenCalled();
    expect(revoke).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1_000);
    expect(revoke).toHaveBeenCalledWith('blob:report');
  });
});
