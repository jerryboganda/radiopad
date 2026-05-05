'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, setActiveAuthToken } from '@/lib/api';
import { setAuthToken, isAuthTokenSecure } from '@/lib/secureAuth';

const LS_TENANT = 'radiopad.tenant';
const LS_USER = 'radiopad.user';

export default function LoginPage() {
  const router = useRouter();
  const [tenant, setTenant] = useState('dev');
  const [user, setUser] = useState('radiologist@radiopad.local');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [secure, setSecure] = useState<boolean | null>(null);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    setTenant(localStorage.getItem(LS_TENANT) || 'dev');
    setUser(localStorage.getItem(LS_USER) || 'radiologist@radiopad.local');
    isAuthTokenSecure().then(setSecure).catch(() => setSecure(false));
  }, []);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setErr(null);
    try {
      localStorage.setItem(LS_TENANT, tenant.trim());
      localStorage.setItem(LS_USER, user.trim());
      // Mint a session token. The header-based dev identity still works
      // for tools and tests; the token unlocks future SSO/OIDC pipelines.
      const result = await api.auth.signIn(tenant.trim(), user.trim());
      await setAuthToken(result.token);
      setActiveAuthToken(result.token);
      router.push('/');
    } catch (e) {
      const ex = e as { body?: { error?: string }; message: string };
      setErr(ex.body?.error || ex.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-container" style={{ maxWidth: 520 }}>
      <h1 className="rp-page-title">Sign in</h1>
      <p className="rp-page-sub">
        Dev identity is conveyed via the <code>X-RadioPad-Tenant</code> and{' '}
        <code>X-RadioPad-User</code> headers. In production, an authenticating reverse proxy maps the
        real principal onto these headers. After sign-in we mint an opaque bearer token and store it
        in {secure === null ? '…' : secure ? 'OS-level secure storage (Keychain / Keystore)' : 'browser-local storage (preview only)'}.
      </p>

      {err && <div className="banner warn">{err}</div>}

      <form className="rp-panel" onSubmit={save}>
        <div className="section-block">
          <label>Tenant slug</label>
          <input value={tenant} onChange={(e) => setTenant(e.target.value)} required />
        </div>
        <div className="section-block">
          <label>User email</label>
          <input type="email" value={user} onChange={(e) => setUser(e.target.value)} required />
        </div>
        <div className="rp-row">
          <button className="primary" type="submit" disabled={busy}>
            {busy ? 'Signing in…' : 'Continue →'}
          </button>
        </div>
      </form>
    </div>
  );
}
