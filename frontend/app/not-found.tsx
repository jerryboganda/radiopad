'use client';

/**
 * Surface-aware 404. On the web (master-admin) surface, reporting routes are
 * intentionally absent, so an unknown path most likely means someone followed a
 * reporting link here — point them at the desktop app. On desktop/mobile it is a
 * plain not-found. (Clinical users who reach the web app at all are already
 * caught earlier by WebAdminGate; this covers stray deep links and the SPA
 * fallback landing on a stripped route.)
 */

import Link from 'next/link';
import { isWebSurface } from '@/lib/surface';

const DESKTOP_DOWNLOAD_URL = 'https://github.com/jerryboganda/radiopad/releases/latest';

export default function NotFound() {
  return (
    <div className="rp-public-auth-content">
      <div className="rp-panel" style={{ maxWidth: 520, margin: '12vh auto', textAlign: 'center' }}>
        <h1 className="rp-page-title" style={{ marginBottom: 8 }}>
          {isWebSurface ? 'Not part of the admin console' : 'Page not found'}
        </h1>
        {isWebSurface ? (
          <>
            <p className="rp-page-sub" style={{ marginBottom: 20 }}>
              The web app handles platform administration only. Reporting — drafting, dictating,
              validating, and signing — lives in the RadioPad desktop app.
            </p>
            <div className="rp-auth-actions" style={{ justifyContent: 'center', gap: 8 }}>
              <a className="primary" href={DESKTOP_DOWNLOAD_URL} target="_blank" rel="noreferrer noopener">
                Download the desktop app
              </a>
              <Link className="ghost" href="/admin/users">Go to admin</Link>
            </div>
          </>
        ) : (
          <>
            <p className="rp-page-sub" style={{ marginBottom: 20 }}>
              We couldn’t find that page.
            </p>
            <div className="rp-auth-actions" style={{ justifyContent: 'center' }}>
              <Link className="primary" href="/">Back to workspace</Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
