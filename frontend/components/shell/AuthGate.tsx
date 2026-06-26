'use client';

import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Container from '@/components/shell/Container';
import { useAuthSession } from '@/lib/useAuthSession';

/**
 * Gates protected routes behind an authenticated session. Wrapped around the
 * app shell for every non-public route (AppShell renders `/login` and `/pair`
 * without this guard). The desktop sidecar runs the backend with
 * `RADIOPAD_REQUIRE_AUTH=1`, so `GET /api/tenant/me` returns 401 until the user
 * signs in — that 401 surfaces here as `signedOut` and we redirect to the login
 * screen. Once a token is minted and stored, `/me` succeeds and the shell renders.
 */
export default function AuthGate({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { loading, signedOut } = useAuthSession();

  useEffect(() => {
    if (loading || !signedOut) return;
    const target = pathname && pathname !== '/'
      ? `/login?returnTo=${encodeURIComponent(pathname)}`
      : '/login';
    router.replace(target);
  }, [loading, signedOut, pathname, router]);

  if (loading || signedOut) {
    return (
      <div className="rp-public-auth-content">
        <Container>
          <p className="rp-page-sub">Loading…</p>
        </Container>
      </div>
    );
  }

  return <>{children}</>;
}
