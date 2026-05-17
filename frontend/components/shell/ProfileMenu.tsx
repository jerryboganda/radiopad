'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { api, setActiveAuthToken } from '@/lib/api';
import { clearAuthToken } from '@/lib/secureAuth';
import LocalePicker from '../LocalePicker';

type Me = { tenant: { displayName: string }; user: { email: string } } | null;

export default function ProfileMenu() {
  const router = useRouter();
  const tBar = useTranslations('topbar');
  const tNav = useTranslations('nav');
  const tProfile = useTranslations('profile');
  const tSubtle = useTranslations('buttons.subtle');
  const [me, setMe] = useState<Me>(null);
  const [open, setOpen] = useState(false);
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

  async function signOut() {
    setSigningOut(true);
    setSignOutError(null);
    setOpen(false);
    let redirectStatus = '';
    try {
      await api.auth.logout();
    } catch (e) {
      const status = (e as { status?: number }).status;
      redirectStatus = status === 404 || status === 405 ? '?signout=local' : '?signout=server-error';
      setSignOutError(status === 404 || status === 405
        ? 'Server logout endpoint is not available; local credentials were cleared.'
        : 'Server logout failed; local credentials were cleared.');
    } finally {
      try {
        await clearAuthToken();
      } finally {
        setActiveAuthToken(null);
        setMe(null);
        setSigningOut(false);
        router.push(`/login${redirectStatus}`);
      }
    }
  }

  return (
    <div className="rp-profile-menu" ref={ref}>
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
          <span className="rp-profile-tenant">{tenant}</span>
        </span>
      </button>

      {open && (
        <div className="rp-profile-popover" role="menu">
          <div className="rp-profile-popover-meta">{tProfile('account')}</div>
          <Link className="rp-profile-popover-item" role="menuitem" href="/admin/settings" onClick={() => setOpen(false)}>
            {tNav('settings')}
          </Link>
          <Link className="rp-profile-popover-item" role="menuitem" href="/admin/billing" onClick={() => setOpen(false)}>
            {tNav('billing')}
          </Link>
          <div className="rp-profile-popover-divider" />
          <div className="rp-profile-popover-meta">{tProfile('language')}</div>
          <div className="rp-profile-locale-slot">
            <LocalePicker ariaLabel={tSubtle('language')} />
          </div>
          <div className="rp-profile-popover-divider" />
          {me ? (
            <button
              className="rp-profile-popover-item"
              role="menuitem"
              type="button"
              onClick={signOut}
              disabled={signingOut}
            >
              {signingOut ? tProfile('signOut') : tProfile('signOut')}
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
