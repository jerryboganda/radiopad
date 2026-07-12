/**
 * Client-side RBAC mirror. The backend remains the single source of truth and
 * ENFORCES every permission server-side; these helpers only decide which UI
 * affordances to render so users aren't shown controls they cannot use (and
 * don't run into surprise 403s).
 *
 * The permission KEYS are delivered by `GET /api/tenant/me` (`user.permissions`),
 * computed server-side from the same RolePermissionMap the API enforces with —
 * so this mirror can never drift from the backend, and every role (including
 * trainee / research / auditor roles) is covered automatically.
 */

'use client';

import { useEffect, useState } from 'react';
import { api } from './api';

/** Every permission key defined in `PermissionCatalog` (backend). */
export type PermissionKey =
  | 'reports.read'
  | 'reports.draft'
  | 'reports.edit'
  | 'reports.validate'
  | 'reports.sign'
  | 'reports.export'
  | 'rulebooks.read'
  | 'rulebooks.manage'
  | 'rulebooks.approve'
  | 'templates.read'
  | 'templates.manage'
  | 'templates.approve'
  | 'providers.read'
  | 'providers.manage'
  | 'audit.read'
  | 'audit.verify'
  | 'audit.export'
  | 'users.read'
  | 'users.manage'
  | 'users.revoke_sessions'
  | 'billing.read'
  | 'billing.manage'
  | 'security.manage'
  | 'tenant_settings.manage'
  | 'validation_packs.read'
  | 'validation_packs.manage'
  | 'validation_packs.run'
  | 'mcp_tools.invoke'
  | 'mcp_tools.manage'
  | 'prompt_overrides.manage'
  | 'prompt_overrides.approve'
  | 'modalities.read'
  | 'modalities.manage'
  | 'body_parts.read'
  | 'body_parts.manage';

/** Pure check: does this permission set grant the given key? */
export function can(
  permissions: readonly string[] | null | undefined,
  key: PermissionKey,
): boolean {
  return !!permissions && permissions.includes(key);
}

/**
 * Permissions that grant access to the master-admin (web) surface. A signed-in
 * user holding NONE of these is clinical-only — the web app has no reporting
 * workspace for them, so they are shown the "use the desktop app" notice. These
 * are administrative capabilities (manage / approve / verify), never the plain
 * read/draft/edit permissions a reporting radiologist also holds.
 */
// IMPORTANT: keep this list to unambiguous ADMIN capabilities only. A plain
// Radiologist holds `users.read`, `billing.read`, `mcp_tools.invoke`, and
// `audit.read` (see backend RolePermissionMap), so including any of those here
// would fail OPEN and let a clinical user into the admin console. Every entry
// below is a manage/approve/verify/export capability that no purely-clinical
// role (Radiologist, Resident, Fellow, Subspecialist, Researcher) is granted;
// every admin role (ReportingAdmin, ItAdmin, BillingAdmin, ComplianceReviewer,
// Auditor, MedicalDirector, platform super-admin) holds at least one.
export const WEB_ADMIN_PERMISSIONS: readonly PermissionKey[] = [
  'users.manage',
  'users.revoke_sessions',
  'tenant_settings.manage',
  'security.manage',
  'billing.manage',
  'providers.manage',
  'mcp_tools.manage',
  'validation_packs.manage',
  'audit.verify',
  'audit.export',
  'rulebooks.approve',
  'templates.approve',
  'prompt_overrides.approve',
];

/** Whether this permission set may use the master-admin (web) surface. */
export function hasWebAdminAccess(
  permissions: readonly string[] | null | undefined,
): boolean {
  return !!permissions && WEB_ADMIN_PERMISSIONS.some((k) => permissions.includes(k));
}

export interface PermissionState {
  /** True until the /me probe resolves. */
  loading: boolean;
  /** True when the backend rejected the session (401/403) or /me threw. */
  signedOut: boolean;
  /** Effective permission keys for the signed-in user. */
  permissions: string[];
  /** Numeric UserRole ordinal (backwards-compat with lib/roles.ts). */
  role: number | null;
  /** Server-rendered role name (e.g. "MedicalDirector"). */
  roleName: string | null;
  /** Convenience predicate bound to the resolved permission set. */
  can: (key: PermissionKey) => boolean;
}

// Module-level cache so the many components that need the permission set
// (sidebar, page guards, action buttons) share ONE /api/tenant/me request and
// stay consistent across client-side navigations. Cleared on auth failure so a
// later sign-in re-probes.
let mePromise: ReturnType<typeof api.me> | null = null;
function loadMe(): ReturnType<typeof api.me> {
  if (!mePromise) mePromise = api.me();
  return mePromise;
}

/** Force the next `usePermissions()` mount to re-fetch (after sign-in/out). */
export function resetPermissionsCache(): void {
  mePromise = null;
}

const SIGNED_OUT: PermissionState = {
  loading: false,
  signedOut: true,
  permissions: [],
  role: null,
  roleName: null,
  can: () => false,
};

export function usePermissions(): PermissionState {
  const [state, setState] = useState<PermissionState>({
    loading: true,
    signedOut: false,
    permissions: [],
    role: null,
    roleName: null,
    can: () => false,
  });

  useEffect(() => {
    let cancelled = false;
    loadMe()
      .then((me) => {
        if (cancelled) return;
        const permissions = me.user.permissions ?? [];
        setState({
          loading: false,
          signedOut: false,
          permissions,
          role: me.user.role,
          roleName: me.user.roleName ?? null,
          can: (key) => permissions.includes(key),
        });
      })
      .catch((e: Error & { status?: number }) => {
        // Allow a later mount (post sign-in) to retry rather than caching a failure.
        mePromise = null;
        if (cancelled) return;
        const signedOut =
          e?.status === 401 || e?.status === 403 || e?.status === undefined;
        setState(signedOut ? SIGNED_OUT : { ...SIGNED_OUT, signedOut: false });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
