import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import * as React from 'react';
import LoginPage from '@/app/login/page';

// A working in-memory `localStorage` is installed centrally in
// `__tests__/setup.ts` (jsdom here is launched with a broken
// `--localstorage-file` flag, so the built-in Storage methods are missing).

const { replace, push } = vi.hoisted(() => ({
  replace: vi.fn(),
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace,
    push,
    prefetch: vi.fn(),
    back: vi.fn(),
    forward: vi.fn(),
    refresh: vi.fn(),
  }),
  useSearchParams: () => new URLSearchParams(''),
}));

vi.mock('@/lib/secureAuth', () => ({
  isAuthTokenSecure: vi.fn(() => new Promise<boolean>(() => {})),
  setAuthToken: vi.fn(() => Promise.resolve()),
}));

vi.mock('@/lib/api', () => ({
  publicEnv: (name: string) => process.env[name],
  setActiveAuthToken: vi.fn(),
  api: {
    auth: {
      oidcAuthorizeUrl: vi.fn(() => Promise.resolve('/api/auth/oidc/authorize')),
      magicLinkRequest: vi.fn(() => Promise.resolve({ ok: true })),
      magicLinkConsume: vi.fn(() => Promise.resolve({ token: 'rp_token', tenant: 'dev', user: 'u@example.com' })),
      signIn: vi.fn(() => Promise.resolve({ token: 'rp_dev', tenant: 'dev', user: 'radiologist@radiopad.local' })),
      webAuthnSignInOptions: vi.fn(),
      webAuthnSignIn: vi.fn(),
    },
  },
}));

beforeEach(() => {
  vi.unstubAllEnvs();
  replace.mockClear();
  push.mockClear();
  localStorage.clear();
});

describe('LoginPage auth choices', () => {
  it('hides the dev tenant/user bearer form by default', () => {
    // The "Continue with SSO" button only renders when SSO is enabled
    // (NEXT_PUBLIC_ENABLE_SSO=true); enable it here so the production sign-in
    // option is asserted. The dev bearer form stays hidden regardless.
    vi.stubEnv('NEXT_PUBLIC_ENABLE_SSO', 'true');

    render(<LoginPage />);

    expect(screen.getByRole('heading', { name: 'Sign in to RadioPad' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue with SSO' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pair a device' })).toBeInTheDocument();
    // Magic-link sign-in was removed (auth is now password + mandatory TOTP + biometric).
    expect(screen.queryByRole('button', { name: 'Email me a sign-in link' })).not.toBeInTheDocument();
    expect(screen.queryByText(/bearer sign-in/i)).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('radiologist@radiopad.local')).not.toBeInTheDocument();
  });

  it('shows the dev tenant/user bearer form only behind the explicit non-production flag', () => {
    vi.stubEnv('NEXT_PUBLIC_ALLOW_DEV_LOGIN', 'true');

    render(<LoginPage />);

    expect(screen.getByText(/bearer sign-in/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue with dev session' })).toBeInTheDocument();
  });
});
