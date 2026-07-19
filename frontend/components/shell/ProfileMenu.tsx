'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { api, setActiveAuthToken } from '@/lib/api';
import { clearAuthToken } from '@/lib/secureAuth';
import { useSttMode } from '@/lib/dictation/sttMode';
import { useCrossCheckEnabled, useUseUbag } from '@/lib/dictation/crossCheckPrefs';
import { isDesktopSurface, isWebSurface } from '@/lib/surface';
import LocalePicker from '../LocalePicker';

type Me = {
  tenant: { displayName: string };
  user: { email: string; roleName?: string };
} | null;

/**
 * variant="topbar" (RC chrome): avatar + name/role trigger on the topbar's
 * right edge, popover drops down. variant="sidebar" keeps the legacy
 * sidebar-footer placement (popover opens upward).
 */
export default function ProfileMenu({ variant = 'sidebar' }: { variant?: 'sidebar' | 'topbar' }) {
  const router = useRouter();
  const tBar = useTranslations('topbar');
  const tNav = useTranslations('nav');
  const tProfile = useTranslations('profile');
  const tSubtle = useTranslations('buttons.subtle');
  const [me, setMe] = useState<Me>(null);
  const [open, setOpen] = useState(false);
  const [dictMode, setDictMode] = useSttMode();
  const [ccEnabled, setCcEnabled] = useCrossCheckEnabled();
  const [ccUbag, setCcUbag] = useUseUbag();
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState<string | null>(null);
  const ref = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    api.me().then(setMe).catch(() => setMe(null));
  }, []);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const esc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('mousedown', handler);
    window.addEventListener('keydown', esc);
    return () => {
      window.removeEventListener('mousedown', handler);
      window.removeEventListener('keydown', esc);
    };
  }, [open]);

  const initials = me?.user?.email?.slice(0, 1).toUpperCase() ?? '?';
  const email = me?.user?.email ?? tProfile('signedOut');
  const tenant = me?.tenant?.displayName ?? tBar('tagline');
  const role = me?.user?.roleName;
  // Topbar trigger shows identity + role (RC chrome); sidebar shows tenant.
  const secondary = variant === 'topbar' ? role ?? tenant : tenant;

  async function signOut() {
    setOpen(false);
    let serverOk = true;
    try {
      await api.auth.logout();
    } catch {
      serverOk = false;
    }
    await clearAuthToken().catch(() => undefined);
    setActiveAuthToken(null);
    setMe(null);
    router.replace(serverOk ? '/login' : '/login?signout=server-error');
  }

  return (
    <div className={`rp-profile-menu${variant === 'topbar' ? ' rp-profile-menu--topbar' : ''}`} ref={ref}>
      <button
        type="button"
        className="rp-profile-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="rp-avatar" aria-hidden>{initials}</span>
        <span className="rp-profile-text">
          <span className="rp-profile-name">{email}</span>
          <span className="rp-profile-tenant">{secondary}</span>
        </span>
      </button>

      {open && (
        <div className="rp-profile-popover" role="menu">
          <div className="rp-profile-popover-meta">{tProfile('account')}</div>
          {/* Both of these pointed unconditionally at `(web)` routes, which build-surface.mjs
              stages out of the desktop and mobile bundles — so on every screen that renders the
              topbar (i.e. nearly all of them) they landed a radiologist on "Page not found" and
              lost their place. Settings exists on both surfaces at different paths; billing is a
              master-admin concern that only the web console ships. */}
          <Link
            className="rp-profile-popover-item"
            role="menuitem"
            href={isWebSurface ? '/admin/settings' : '/settings'}
            onClick={() => setOpen(false)}
          >
            {tNav('settings')}
          </Link>
          {isWebSurface && (
            <Link className="rp-profile-popover-item" role="menuitem" href="/admin/billing" onClick={() => setOpen(false)}>
              {tNav('billing')}
            </Link>
          )}
          {/* Dictation preferences drive the desktop-only on-device engines +
              cross-check flow — dead UI on the web admin surface, so gate them
              to the desktop bundle (same build-time flag the shell uses). */}
          {isDesktopSurface && (<>
          <div className="rp-profile-popover-divider" />
          <div className="rp-profile-popover-meta">Dictation</div>
          <label
            className="rp-profile-popover-item rp-profile-popover-check"
            data-testid="profile-dual-check"
            title="Cross-check dictation with a second on-device engine (Parakeet + Windows Speech) and flag disagreements for review. Doubles CPU/RAM."
          >
            <input
              type="checkbox"
              checked={dictMode === 'ensemble'}
              onChange={(e) => setDictMode(e.target.checked ? 'ensemble' : 'single')}
            />
            <span className="rp-profile-check-label">Dual-engine cross-check</span>
          </label>
          <label
            className="rp-profile-popover-item rp-profile-popover-check"
            data-testid="profile-crosscheck"
            title="Show a 'Cross Check' button that re-runs a dictation through extra engines + a medical-AI review and highlights corrections."
          >
            <input
              type="checkbox"
              checked={ccEnabled}
              onChange={(e) => setCcEnabled(e.target.checked)}
            />
            <span className="rp-profile-check-label">Manual Cross Check</span>
          </label>
          <label
            className="rp-profile-popover-item rp-profile-popover-check"
            data-testid="profile-crosscheck-ubag"
            title="Also route the medical-accuracy review through the UBAG cloud AI. Only use on reports with NO patient-identifying information (PHI)."
          >
            <input
              type="checkbox"
              checked={ccUbag}
              disabled={!ccEnabled}
              onChange={(e) => setCcUbag(e.target.checked)}
            />
            <span className="rp-profile-check-label">Cross Check via UBAG (no PHI)</span>
          </label>
          </>)}
          <div className="rp-profile-popover-divider" />
          <div className="rp-profile-popover-meta">{tProfile('language')}</div>
          <div className="rp-profile-locale-slot">
            <LocalePicker ariaLabel={tSubtle('language')} />
          </div>
          <div className="rp-profile-popover-divider" />
          {me ? (
            <button className="rp-profile-popover-item" role="menuitem" type="button" onClick={signOut}>
              {tProfile('signOut')}
            </button>
          ) : (
            <Link className="rp-profile-popover-item" role="menuitem" href="/login" onClick={() => setOpen(false)}>
              {tNav('signIn')}
            </Link>
          )}
        </div>
      )}
      {signOutError && <div className="rp-profile-signout-error">{signOutError}</div>}
    </div>
  );
}
