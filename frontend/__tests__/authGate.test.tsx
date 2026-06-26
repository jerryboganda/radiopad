import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import * as React from 'react';
import AuthGate from '@/components/shell/AuthGate';

type FakeSession = { loading: boolean; signedOut: boolean; me: unknown; error: string | null };

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));
const { session } = vi.hoisted(() => ({
  session: { value: { loading: true, signedOut: false, me: null, error: null } as FakeSession },
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace, push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => '/reports',
}));

vi.mock('@/lib/useAuthSession', () => ({
  useAuthSession: () => session.value,
}));

// Container only renders children — stub to avoid pulling the shell tree.
vi.mock('@/components/shell/Container', () => ({
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

beforeEach(() => {
  replace.mockClear();
  session.value = { loading: true, signedOut: false, me: null, error: null };
});

describe('AuthGate', () => {
  it('shows a splash and does not redirect while the session is loading', () => {
    session.value = { loading: true, signedOut: false, me: null, error: null };
    render(<AuthGate><div>protected content</div></AuthGate>);
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByText('protected content')).not.toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it('redirects to /login with returnTo when signed out', () => {
    session.value = { loading: false, signedOut: true, me: null, error: '401' };
    render(<AuthGate><div>protected content</div></AuthGate>);
    expect(replace).toHaveBeenCalledWith('/login?returnTo=%2Freports');
    expect(screen.queryByText('protected content')).not.toBeInTheDocument();
  });

  it('renders children when signed in', () => {
    session.value = { loading: false, signedOut: false, me: { tenant: {}, user: {} }, error: null };
    render(<AuthGate><div>protected content</div></AuthGate>);
    expect(screen.getByText('protected content')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});
