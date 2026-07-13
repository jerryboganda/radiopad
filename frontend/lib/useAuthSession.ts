/**
 * Shared client-side hook that resolves the active sign-in session via
 * `GET /api/tenant/me`. Used by admin surfaces (Settings / Billing /
 * Usage) so they can render a "Sign in required" empty state instead of
 * shouting raw 401/403 banners at signed-out visitors (see screenshots
 * from radiopadstudio.com tracked in PROGRESS.md).
 *
 * The hook never throws. Callers branch on `signedOut` (true when /me
 * returns 401, 403, or any auth-derived failure) and on `me` (resolved
 * tenant + user record) to decide what to render. Always uses the typed
 * `api` client — never calls `fetch` directly.
 */

'use client';

import { useEffect, useState } from 'react';
import { api } from './api';

export type AuthSessionMe = Awaited<ReturnType<typeof api.me>>;

export interface AuthSession {
  /** True until the /me probe resolves. */
  loading: boolean;
  /** The tenant + user record when signed in; null otherwise. */
  me: AuthSessionMe | null;
  /**
   * True when the backend rejected the session (HTTP 401 or 403) OR when
   * /me threw before yielding a record. False once `me` is non-null.
   */
  signedOut: boolean;
  /** Last error message from /me (for diagnostics / dev banners). */
  error: string | null;
}

export function useAuthSession(): AuthSession {
  const [state, setState] = useState<AuthSession>({
    loading: true,
    me: null,
    signedOut: false,
    error: null,
  });

  useEffect(() => {
    let cancelled = false;
    api
      .me()
      .then((me) => {
        if (cancelled) return;
        setState({ loading: false, me, signedOut: false, error: null });
      })
      .catch((e: Error & { status?: number }) => {
        if (cancelled) return;
        const signedOut = e?.status === 401 || e?.status === 403 || e?.status === undefined;
        setState({ loading: false, me: null, signedOut, error: e?.message ?? 'Unknown error' });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}

/**
 * Returns true when the given error looks like a sign-in or RBAC denial —
 * use it to decide whether to render a "Sign in required" / "Insufficient
 * permissions" empty state on individual API calls (Billing has several
 * independent panels that each may fail independently).
 */
export function isAuthError(e: unknown): boolean {
  if (!e || typeof e !== 'object') return false;
  const status = (e as { status?: number }).status;
  return status === 401 || status === 403;
}
