/**
 * Shared empty state for admin surfaces visited by a signed-out user.
 * Replaces the raw "API 403" banners users were seeing on
 * /admin/settings, /admin/billing, and /admin/usage (see PROGRESS.md
 * iter-32 follow-up). Uses only locked design tokens and component
 * classes — no inline colours / borders / radii.
 */

'use client';

import Link from 'next/link';
import type { ReactNode } from 'react';

export interface SignInRequiredProps {
  /** Page-specific reason (e.g. "Tenant settings"). Defaults to a generic line. */
  surface?: ReactNode;
  /** Optional diagnostic detail; rendered as small print under the CTA. */
  detail?: ReactNode;
}

export default function SignInRequired({ surface, detail }: SignInRequiredProps) {
  const returnTo =
    typeof window !== 'undefined' ? window.location.pathname + window.location.search : '/';
  const href = `/login?returnTo=${encodeURIComponent(returnTo)}`;
  return (
    <div className="rp-empty" role="status">
      <p className="rp-empty-title">Sign in required</p>
      <p className="rp-empty-desc">
        {surface ?? 'This page is tenant-scoped and needs an authenticated session.'}
      </p>
      <div className="rp-empty-actions">
        <Link className="primary" href={href}>
          Sign in
        </Link>
      </div>
      {detail && <p className="rp-page-sub rp-mt-sm">{detail}</p>}
    </div>
  );
}
