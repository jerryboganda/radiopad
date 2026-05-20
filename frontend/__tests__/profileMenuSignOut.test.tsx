import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';
import ProfileMenu from '@/components/shell/ProfileMenu';

const { push, logout, setActiveAuthToken, clearAuthToken } = vi.hoisted(() => ({
  push: vi.fn(),
  logout: vi.fn(),
  setActiveAuthToken: vi.fn(),
  clearAuthToken: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push }),
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
    expect(push).toHaveBeenCalledWith('/login');
  });

  it('still clears local auth when logout endpoint is unavailable', async () => {
    logout.mockRejectedValueOnce(Object.assign(new Error('missing'), { status: 404 }));

    render(<ProfileMenu />);

    fireEvent.click(await screen.findByRole('button', { name: /reader@example.com/i }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    await waitFor(() => expect(clearAuthToken).toHaveBeenCalledTimes(1));
    expect(setActiveAuthToken).toHaveBeenCalledWith(null);
    expect(push).toHaveBeenCalledWith('/login?signout=local');
  });
});
