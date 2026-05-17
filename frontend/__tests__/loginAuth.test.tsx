import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import * as React from 'react';
import LoginPage from '@/app/login/page';

const { replace, push } = vi.hoisted(() => ({
  replace: vi.fn(),
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace, push }),
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
    render(<LoginPage />);

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue with SSO' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Email me a sign-in link' })).toBeInTheDocument();
    expect(screen.queryByText('Dev/test bearer sign-in')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('radiologist@radiopad.local')).not.toBeInTheDocument();
  });

  it('shows the dev tenant/user bearer form only behind the explicit non-production flag', () => {
    vi.stubEnv('NEXT_PUBLIC_ALLOW_DEV_LOGIN', 'true');

    render(<LoginPage />);

    expect(screen.getByText('Dev/test bearer sign-in')).toBeInTheDocument();
    expect(screen.getByText(/NEXT_PUBLIC_ALLOW_DEV_LOGIN=true/)).toBeInTheDocument();
  });
});
