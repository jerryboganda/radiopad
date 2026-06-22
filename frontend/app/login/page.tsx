'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { api, publicEnv, setActiveAuthToken } from '@/lib/api';
import { setAuthToken, isAuthTokenSecure } from '@/lib/secureAuth';

const LS_TENANT = 'radiopad.tenant';
const LS_USER = 'radiopad.user';
const BUILD_NODE_ENV = process.env.NODE_ENV;
const DEV_LOGIN_FLAG = process.env.NEXT_PUBLIC_ALLOW_DEV_LOGIN;
const SSO_FLAG = process.env.NEXT_PUBLIC_ENABLE_SSO;

type SessionResult = { token?: string; tenant?: string; user?: string };

function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  try {
    const parsed = new URL(value, 'https://radiopad.local');
    if (parsed.pathname === '/login') return '/';
    return `${parsed.pathname}${parsed.search}${parsed.hash}`;
  } catch {
    return '/';
  }
}

function allowDevLogin(): boolean {
  const nodeEnv = publicEnv('NODE_ENV') ?? BUILD_NODE_ENV;
  const flag = publicEnv('NEXT_PUBLIC_ALLOW_DEV_LOGIN') ?? DEV_LOGIN_FLAG;
  // The Tauri desktop shell is an inherently local, single-user context whose
  // intended sign-in is the passwordless dev/local session (the bundled backend
  // gates it with RADIOPAD_DEV_HEADERS). Its frontend is a production `next build`,
  // so the NODE_ENV guard alone would always disable dev login; allow it inside Tauri.
  const isDesktop = typeof window !== 'undefined' && '__TAURI__' in window;
  return isDesktop || (nodeEnv !== 'production' && flag === 'true');
}

function LoginContent() {
  const router = useRouter();
  const search = useSearchParams();
  const devLoginEnabled = allowDevLogin();
  const ssoEnabled = (publicEnv('NEXT_PUBLIC_ENABLE_SSO') ?? SSO_FLAG) === 'true';
  const [tenant, setTenant] = useState('');
  const [user, setUser] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [secure, setSecure] = useState<boolean | null>(null);
  const returnToParam = search?.get('returnTo');
  const returnTo = useMemo(() => safeReturnTo(returnToParam), [returnToParam]);
  const callbackUrl = useMemo(() => {
    if (typeof window === 'undefined') return undefined;
    return `${window.location.origin}/login?returnTo=${encodeURIComponent(returnTo)}`;
  }, [returnTo]);

  const ssoReturnUrl = useMemo(() => {
    if (typeof window === 'undefined') return undefined;
    return `${window.location.origin}${returnTo}`;
  }, [returnTo]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    setTenant(devLoginEnabled ? localStorage.getItem(LS_TENANT) || 'dev' : '');
    setUser(devLoginEnabled ? localStorage.getItem(LS_USER) || 'radiologist@radiopad.local' : '');
    isAuthTokenSecure().then(setSecure).catch(() => setSecure(false));
  }, [devLoginEnabled]);

  async function completeSession(result: SessionResult) {
    if (result.tenant) localStorage.setItem(LS_TENANT, result.tenant);
    if (result.user) localStorage.setItem(LS_USER, result.user);

    if (result.token) {
      const nativeSecure = await isAuthTokenSecure().catch(() => false);
      if (nativeSecure || devLoginEnabled) {
        await setAuthToken(result.token);
        setActiveAuthToken(result.token);
      } else {
        // Browser production relies on HttpOnly/SameSite cookies for reloads.
        // Keep the bearer only in memory so the first SPA navigation after the
        // callback can complete without writing it to web localStorage.
        setActiveAuthToken(result.token);
      }
    }
    router.replace(returnTo);
  }

  useEffect(() => {
    const token = search?.get('magic');
    if (!token) return;
    let cancelled = false;
    setBusy('magic-consume');
    setErr(null);
    api.auth.magicLinkConsume(token)
      .then((result) => {
        if (!cancelled) return completeSession(result);
        return undefined;
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        const ex = e as { body?: { error?: string } | string; status?: number; message?: string };
        const bodyErr = typeof ex.body === 'object' && ex.body?.error ? ex.body.error : null;
        setErr(bodyErr || (ex.status === 401 ? 'This sign-in link has expired or was already used. Please request a new one below.' : ex.message || 'The magic link could not be used.'));
      })
      .finally(() => {
        if (!cancelled) setBusy(null);
      });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  useEffect(() => {
    const signedOut = search?.get('signout');
    if (signedOut === 'local') {
      setInfo('Signed out locally. Server logout was not available.');
    } else if (signedOut === 'server-error') {
      setErr('Signed out locally, but the server logout endpoint returned an error.');
    }
  }, [search]);

  async function beginSso() {
    setBusy('sso');
    setErr(null);
    try {
      const url = await api.auth.oidcAuthorizeUrl(ssoReturnUrl);
      window.location.assign(url);
    } catch (e) {
      const ex = e as { message?: string };
      setErr(ex.message || 'SSO could not be started.');
      setBusy(null);
    }
  }

  async function requestMagicLink(e: React.FormEvent) {
    e.preventDefault();
    setBusy('magic');
    setErr(null);
    setInfo(null);
    try {
      const result = await api.auth.magicLinkRequest(tenant.trim(), user.trim(), callbackUrl);
      setInfo(result.devLink && devLoginEnabled
        ? `Magic link requested. Dev link: ${result.devLink}`
        : 'If the account is eligible, a sign-in link has been sent.');
    } catch (e) {
      const ex = e as { body?: { error?: string }; message?: string };
      setErr(ex.body?.error || ex.message || 'The magic link request failed.');
    } finally {
      setBusy(null);
    }
  }

  async function saveDevSession(e: React.FormEvent) {
    e.preventDefault();
    if (!devLoginEnabled) return;
    setBusy('dev');
    setErr(null);
    try {
      localStorage.setItem(LS_TENANT, tenant.trim());
      localStorage.setItem(LS_USER, user.trim());
      await completeSession(await api.auth.signIn(tenant.trim(), user.trim()));
    } catch (e) {
      const ex = e as { body?: { error?: string }; message?: string };
      setErr(ex.body?.error || ex.message || 'Dev sign-in failed.');
      setBusy(null);
    }
  }

  return (
    <Container className="rp-auth-shell">
      <PageHeader
        title="Sign in"
        description="Use your organization identity provider or request a passwordless email link. Browser sessions are cookie-based in production; native desktop and mobile shells use secure OS storage."
      />

      {err && <div className="banner warn">{err}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">Production sign-in</div>
        <div className="rp-auth-action-list">
          {ssoEnabled && (
            <button className="primary" type="button" onClick={beginSso} disabled={busy !== null}>
              {busy === 'sso' ? 'Starting SSO…' : 'Continue with SSO'}
            </button>
          )}
          <button className="ghost" type="button" onClick={() => router.push('/pair')} disabled={busy !== null}>
            Pair a device
          </button>
        </div>
      </div>

      <form className="rp-panel" onSubmit={requestMagicLink}>
        <div className="rp-panel-title">Email magic link</div>
        <div className="section-block">
          <label>Tenant slug</label>
          <input value={tenant} onChange={(e) => setTenant(e.target.value)} required autoComplete="organization" />
        </div>
        <div className="section-block">
          <label>Work email</label>
          <input type="email" value={user} onChange={(e) => setUser(e.target.value)} required autoComplete="email" />
        </div>
        <div className="rp-auth-action-list">
          <button className="primary" type="submit" disabled={busy !== null}>
            {busy === 'magic' ? 'Requesting link…' : 'Email me a sign-in link'}
          </button>
        </div>
        <p className="rp-page-sub rp-mt-sm">
          Passkey sign-in remains disabled until the backend challenge store and assertion verification hardening land.
        </p>
      </form>

      {devLoginEnabled && (
        <form className="rp-panel" onSubmit={saveDevSession}>
          <div className="rp-panel-title">Dev/test bearer sign-in</div>
          <p className="rp-page-sub rp-mb-md">
            Enabled only because <code>NEXT_PUBLIC_ALLOW_DEV_LOGIN=true</code> in a non-production frontend.
            The bearer is stored in {secure === null ? 'secure storage when available' : secure ? 'OS-level secure storage' : 'browser-local storage for preview'}.
          </p>
          <div className="section-block">
            <label>Tenant slug</label>
            <input value={tenant} onChange={(e) => setTenant(e.target.value)} required />
          </div>
          <div className="section-block">
            <label>User email</label>
            <input type="email" value={user} onChange={(e) => setUser(e.target.value)} required />
          </div>
          <div className="rp-row">
            <button className="primary" type="submit" disabled={busy !== null}>
              {busy === 'dev' ? 'Signing in…' : 'Continue with dev session'}
            </button>
          </div>
        </form>
      )}
    </Container>
  );
}

export default function LoginPage() {
  return (
    <Suspense
      fallback={(
        <Container className="rp-auth-shell">
          <PageHeader
            title="Sign in"
            description="Use your organization identity provider or request a passwordless email link."
          />
        </Container>
      )}
    >
      <LoginContent />
    </Suspense>
  );
}
