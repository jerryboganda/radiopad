import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
}));
vi.mock('next/link', () => ({
  default: ({ children, ...rest }: { children: React.ReactNode }) => <a {...rest}>{children}</a>,
}));
vi.mock('next-intl', () => ({
  useTranslations: () => (k: string) => k,
}));
vi.mock('@/lib/api', () => ({
  api: { me: () => Promise.resolve({ tenant: { displayName: 'T' }, user: { email: 'a@b.c' } }), auth: { logout: vi.fn() } },
  setActiveAuthToken: vi.fn(),
}));
vi.mock('@/lib/secureAuth', () => ({ clearAuthToken: () => Promise.resolve() }));
vi.mock('../components/LocalePicker', () => ({ default: () => <div /> }));
vi.mock('@/components/LocalePicker', () => ({ default: () => <div /> }));

beforeEach(() => {
  window.localStorage.clear();
  // Reset the modules so the in-memory preference caches start fresh per test.
  vi.resetModules();
});

async function openMenu() {
  const { default: ProfileMenu } = await import('@/components/shell/ProfileMenu');
  const { container } = render(<ProfileMenu />);
  fireEvent.click(container.querySelector('.rp-profile-trigger')!);
}

describe('ProfileMenu dictation toggles', () => {
  it('toggles Dual-engine cross-check', async () => {
    await openMenu();
    const cb = screen.getByTestId('profile-dual-check').querySelector('input')!;
    expect(cb.checked).toBe(false);
    fireEvent.click(cb);
    await waitFor(() => expect(cb.checked).toBe(true));
    expect(window.localStorage.getItem('radiopad:stt-mode')).toBe('ensemble');
  });

  it('toggles Manual Cross Check (defaults on, can turn off)', async () => {
    await openMenu();
    const cb = screen.getByTestId('profile-crosscheck').querySelector('input')!;
    await waitFor(() => expect(cb.checked).toBe(true));
    fireEvent.click(cb);
    await waitFor(() => expect(cb.checked).toBe(false));
    expect(window.localStorage.getItem('radiopad:crosscheck-enabled')).toBe('0');
  });

  it('UBAG enables only when Manual Cross Check is on', async () => {
    await openMenu();
    const manual = screen.getByTestId('profile-crosscheck').querySelector('input')!;
    const ubag = screen.getByTestId('profile-crosscheck-ubag').querySelector('input')!;
    await waitFor(() => expect(ubag.disabled).toBe(false));
    fireEvent.click(ubag);
    await waitFor(() => expect(ubag.checked).toBe(true));
    // turn manual off -> ubag disabled
    fireEvent.click(manual);
    await waitFor(() => expect(ubag.disabled).toBe(true));
  });

  // Reproduces the desktop-webview symptom: localStorage writes throw / don't
  // reflect back. The toggles must still respond (in-memory source of truth).
  it('toggles still stick when localStorage is unavailable', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('storage blocked');
    });
    try {
      await openMenu();
      const dual = screen.getByTestId('profile-dual-check').querySelector('input')!;
      expect(dual.checked).toBe(false);
      fireEvent.click(dual);
      await waitFor(() => expect(dual.checked).toBe(true));

      const manual = screen.getByTestId('profile-crosscheck').querySelector('input')!;
      await waitFor(() => expect(manual.checked).toBe(true));
      fireEvent.click(manual);
      await waitFor(() => expect(manual.checked).toBe(false));
    } finally {
      setItem.mockRestore();
    }
  });
});
