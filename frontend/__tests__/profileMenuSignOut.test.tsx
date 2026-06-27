import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import * as React from 'react';
import ProfileMenu from '@/components/shell/ProfileMenu';

const { push, replace, prefetch, back, forward, refresh, logout, setActiveAuthToken, clearAuthToken } =
  vi.hoisted(() => ({
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
    back: vi.fn(),
    forward: vi.fn(),
    refresh: vi.fn(),
    logout: vi.fn(),
    setActiveAuthToken: vi.fn(),
    clearAuthToken: vi.fn(),
  }));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push, replace, prefetch, back, forward, refresh }),
}));

vi.mock('next/link', () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('next-intl', () => ({
  useTranslations: () => (key: string) => ({
    tagline: 'AI radiology reporting',
    account: 'Account',
    language: 'Language',
    signOut: 'Sign out',
    signedOut: 'Signed out',
    settings: 'Settings',
    billing: 'Billing',
    signIn: 'Sign in',
  }[key] ?? key),
}));

vi.mock('@/components/LocalePicker', () => ({
  default: () => <select aria-label="Language" />,
}));

vi.mock('@/lib/api', () => ({
  setActiveAuthToken,
  api: {
    me: vi.fn(() => Promise.resolve({
      tenant: { displayName: 'Radiology' },
      user: { email: 'reader@example.com' },
    })),
    auth: { logout },
  },
}));

vi.mock('@/lib/secureAuth', () => ({
  clearAuthToken,
}));

beforeEach(() => {
  push.mockClear();
  replace.mockClear();
  prefetch.mockClear();
  back.mockClear();
  forward.mockClear();
  refresh.mockClear();
  logout.mockReset();
  setActiveAuthToken.mockClear();
  clearAuthToken.mockReset();
  logout.mockResolvedValue(undefined);
  clearAuthToken.mockResolvedValue(undefined);
});

describe('ProfileMenu sign-out', () => {
  it('calls server logout, clears stored bearer state, and routes to login', async () => {
    render(<ProfileMenu />);

    fireEvent.click(await screen.findByRole('button', { name: /reader@example.com/i }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    await waitFor(() => expect(logout).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(clearAuthToken).toHaveBeenCalledTimes(1));
    expect(setActiveAuthToken).toHaveBeenCalledWith(null);
    await waitFor(() => expect(replace).toHaveBeenCalledWith('/login'));
  });

  it('still clears local auth when logout endpoint is unavailable', async () => {
    logout.mockRejectedValueOnce(Object.assign(new Error('missing'), { status: 404 }));

    render(<ProfileMenu />);

    fireEvent.click(await screen.findByRole('button', { name: /reader@example.com/i }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    await waitFor(() => expect(clearAuthToken).toHaveBeenCalledTimes(1));
    expect(setActiveAuthToken).toHaveBeenCalledWith(null);
    await waitFor(() => expect(replace).toHaveBeenCalledWith('/login?signout=server-error'));
  });

  it('lets the end user toggle the dual-engine cross-check from the options menu', async () => {
    window.localStorage.removeItem('radiopad:stt-mode');
    render(<ProfileMenu />);
    fireEvent.click(await screen.findByRole('button', { name: /reader@example.com/i }));

    const checkbox = within(screen.getByTestId('profile-dual-check'))
      .getByRole('checkbox') as HTMLInputElement;
    expect(checkbox.checked).toBe(false); // default: off (single / auto)

    fireEvent.click(checkbox);
    expect(checkbox.checked).toBe(true);
    expect(window.localStorage.getItem('radiopad:stt-mode')).toBe('ensemble');

    fireEvent.click(checkbox);
    expect(checkbox.checked).toBe(false);
    expect(window.localStorage.getItem('radiopad:stt-mode')).toBe('single');
  });
});
