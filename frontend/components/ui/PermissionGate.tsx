/**
 * Page-level RBAC guard for management/admin surfaces. Renders a clean
 * "Sign in required" / "You don't have access" empty state instead of letting
 * the page fire a wall of 403s when a user without the permission lands here
 * (e.g. via a deep link — the sidebar already hides links they can't use).
 *
 * The backend still enforces every permission; this only governs what the UI
 * shows. Mirrors the permission set delivered by `GET /api/tenant/me`.
 */

'use client';

import type { ReactNode } from 'react';
import { usePermissions, type PermissionKey } from '@/lib/permissions';
import SignInRequired from './SignInRequired';

export interface PermissionGateProps {
  /** Permission key required to view the wrapped content. */
  permission: PermissionKey;
  /** Page title rendered above the empty/denied state (matches admin chrome). */
  title?: ReactNode;
  /** Who to ask for access, shown on a permission denial. */
  deniedDetail?: ReactNode;
  children: ReactNode;
}

export default function PermissionGate({
  permission,
  title,
  deniedDetail,
  children,
}: PermissionGateProps) {
  const { loading, signedOut, can } = usePermissions();

  // While the permission probe is in flight, render nothing — pages mount their
  // own skeletons under this guard once access is confirmed.
  if (loading) return null;

  if (signedOut) {
    return (
      <div className="rp-container">
        {title && <h1 className="rp-page-title">{title}</h1>}
        <SignInRequired />
      </div>
    );
  }

  if (!can(permission)) {
    return (
      <div className="rp-container">
        {title && <h1 className="rp-page-title">{title}</h1>}
        <SignInRequired
          surface="You don't have permission to view this page in this workspace."
          detail={deniedDetail ?? 'Ask a workspace administrator to grant you access.'}
        />
      </div>
    );
  }

  return <>{children}</>;
}
