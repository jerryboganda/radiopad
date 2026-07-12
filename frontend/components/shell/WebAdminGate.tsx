'use client';

import type { ReactNode } from 'react';
import Container from '@/components/shell/Container';
import { usePermissions, hasWebAdminAccess } from '@/lib/permissions';

/**
 * Web surface guard. The web app is the master-admin / platform-operations
 * surface only — it ships no reporting workspace. A signed-in user who holds no
 * administrative permission (a clinical radiologist) is shown the desktop-app
 * notice instead of being bounced through admin routes they can't use. Admins
 * fall through to the normal shell.
 *
 * Only mounted on the `web` build (see AppShell); the backend still enforces
 * every permission server-side regardless of what this renders.
 */

const DESKTOP_DOWNLOAD_URL = 'https://github.com/jerryboganda/radiopad/releases/latest';

function DesktopAppNotice() {
  return (
    <div className="rp-public-auth-content">
      <Container>
        <div className="rp-panel" style={{ maxWidth: 560, margin: '10vh auto', textAlign: 'center' }}>
          <div className="brand-mark" aria-hidden style={{ margin: '0 auto 16px' }}>
            <span className="brand-mark-letter">R</span>
          </div>
          <h1 className="rp-page-title" style={{ marginBottom: 8 }}>Reporting lives in the desktop app</h1>
          <p className="rp-page-sub" style={{ marginBottom: 20 }}>
            The RadioPad web app is for platform administration only. Drafting, dictating,
            validating, and signing reports all happen in the desktop application — install it
            to get started. You can also pair your phone as a dictation companion from there.
          </p>
          <div className="rp-auth-actions" style={{ justifyContent: 'center' }}>
            <a className="primary" href={DESKTOP_DOWNLOAD_URL} target="_blank" rel="noreferrer noopener">
              Download the desktop app
            </a>
          </div>
        </div>
      </Container>
    </div>
  );
}

export default function WebAdminGate({ children }: { children: ReactNode }) {
  const { loading, signedOut, permissions } = usePermissions();

  // While the permission probe is in flight — OR when it comes back signed-out
  // (401 / network error / not-yet-authenticated) — defer to AuthGate, which
  // owns the redirect to /login. Only show the "use the desktop app" notice to a
  // user we KNOW is authenticated but lacks admin access; otherwise a signed-out
  // visitor would wrongly see the download page instead of sign-in.
  if (loading || signedOut) {
    return (
      <div className="rp-public-auth-content">
        <Container>
          <p className="rp-page-sub">Loading…</p>
        </Container>
      </div>
    );
  }

  if (!hasWebAdminAccess(permissions)) {
    return <DesktopAppNotice />;
  }

  return <>{children}</>;
}
