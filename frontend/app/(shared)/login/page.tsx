'use client';

import { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Eye, EyeOff } from 'lucide-react';
import AuthScaffold from '@/components/auth/AuthScaffold';
import { api, publicEnv, setActiveAuthToken } from '@/lib/api';
import { setAuthToken, isAuthTokenSecure } from '@/lib/secureAuth';
import { signInWithPasskey, registerPasskey, isPlatformAuthenticatorAvailable } from '@/lib/webauthn';
import { isDesktopSurface } from '@/lib/surface';

const LS_TENANT = 'radiopad.tenant';
const LS_USER = 'radiopad.user';
const BUILD_NODE_ENV = process.env.NODE_ENV;
const DEV_LOGIN_FLAG = process.env.NEXT_PUBLIC_ALLOW_DEV_LOGIN;
const SSO_FLAG = process.env.NEXT_PUBLIC_ENABLE_SSO;

type SessionResult = { token?: string; tenant?: string; user?: string };
type Stage = 'credentials' | 'totp' | 'enroll' | 'biometric-offer' | 'reset';

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
  // Dev / passwordless sign-in is a development convenience, gated PURELY on an
  // explicit build-time opt-in (NEXT_PUBLIC_ALLOW_DEV_LOGIN) in a non-production
  // build. The desktop shell must NOT enable it just for being Tauri: the desktop
  // now talks to the hosted PRODUCTION API, where dev sign-in is disabled (401),
  // so the dev affordance + the prefilled `dev` / `radiologist@radiopad.local`
  // defaults would be broken and misleading there. Real desktop auth uses the
  // production password + TOTP + biometric flow; native secure token storage is
  // gated separately on `isAuthTokenSecure()`, not on this flag.
  return nodeEnv !== 'production' && flag === 'true';
}

function digitsOnly(v: string): string {
  return v.replace(/\D/g, '').slice(0, 6);
}

function LoginContent() {
  const router = useRouter();
  const search = useSearchParams();
  const devLoginEnabled = allowDevLogin();
  const ssoEnabled = (publicEnv('NEXT_PUBLIC_ENABLE_SSO') ?? SSO_FLAG) === 'true';

  const [stage, setStage] = useState<Stage>('credentials');
  const [tenant, setTenant] = useState('');
  const [user, setUser] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [helloAvailable, setHelloAvailable] = useState(false);

  // TOTP step-up / enrolment.
  const [code, setCode] = useState('');
  const [setupToken, setSetupToken] = useState<string | null>(null);
  const [enrollSecret, setEnrollSecret] = useState<string | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);

  // Email-free reset (prove TOTP).
  const [resetCode, setResetCode] = useState('');
  const [resetNewPw, setResetNewPw] = useState('');

  const pendingResult = useRef<SessionResult | null>(null);
  // Canonical identity (tenant slug + email) returned by sign-in. The mfa-setup
  // ticket is bound to these exact values, so enroll AND verify must send them —
  // not the typed form fields, which may differ in case/format from the slug.
  const enrollIdentity = useRef<{ tenant: string; user: string } | null>(null);

  const returnToParam = search?.get('returnTo');
  const returnTo = useMemo(() => safeReturnTo(returnToParam), [returnToParam]);
  const ssoReturnUrl = useMemo(() => {
    if (typeof window === 'undefined') return undefined;
    return `${window.location.origin}${returnTo}`;
  }, [returnTo]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    setTenant(localStorage.getItem(LS_TENANT) || (devLoginEnabled ? 'dev' : ''));
    setUser(localStorage.getItem(LS_USER) || (devLoginEnabled ? 'radiologist@radiopad.local' : ''));
    isPlatformAuthenticatorAvailable().then(setHelloAvailable).catch(() => setHelloAvailable(false));
  }, [devLoginEnabled]);

  useEffect(() => {
    const signedOut = search?.get('signout');
    if (signedOut === 'local') setInfo('Signed out locally. Server logout was not available.');
    else if (signedOut === 'server-error') setErr('Signed out locally, but the server logout endpoint returned an error.');
  }, [search]);

  async function persistToken(result: SessionResult) {
    if (result.tenant) localStorage.setItem(LS_TENANT, result.tenant);
    if (result.user) localStorage.setItem(LS_USER, result.user);
    if (result.token) {
      const nativeSecure = await isAuthTokenSecure().catch(() => false);
      if (nativeSecure || devLoginEnabled) await setAuthToken(result.token);
      setActiveAuthToken(result.token);
    }
  }

  async function finishLogin(result: SessionResult) {
    await persistToken(result);
    router.replace(returnTo);
  }

  function resetTransient() {
    setErr(null);
    setInfo(null);
    setCode('');
  }

  // ── Primary password sign-in ────────────────────────────────────────────
  async function submitPassword(e: React.FormEvent) {
    e.preventDefault();
    resetTransient();
    setBusy('password');
    try {
      const t = tenant.trim();
      const u = user.trim();
      const result = await api.auth.passwordSignIn(t, u, password);
      localStorage.setItem(LS_TENANT, result.tenant ?? t);
      localStorage.setItem(LS_USER, result.user ?? u);
      if (result.mfaRequired) {
        setCode('');
        setStage('totp');
      } else if (result.mfaSetupRequired && result.setupToken) {
        setSetupToken(result.setupToken);
        enrollIdentity.current = { tenant: result.tenant ?? t, user: result.user ?? u };
        await beginEnrollment(result.tenant ?? t, result.user ?? u, result.setupToken);
      } else {
        setErr('Unexpected sign-in response. Please try again.');
      }
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string };
      setErr(e2.body?.error || e2.message || 'Sign-in failed. Check your organization, email, and password.');
    } finally {
      setBusy(null);
      setPassword('');
    }
  }

  // ── Forced first-login TOTP enrolment ───────────────────────────────────
  async function beginEnrollment(t: string, u: string, token: string) {
    setBusy('enroll-begin');
    try {
      const { secret, otpauth } = await api.auth.mfaEnroll(t, u, token);
      setEnrollSecret(secret);
      try {
        const QRCode = (await import('qrcode')).default;
        const url = await QRCode.toDataURL(otpauth, { margin: 1, width: 220, errorCorrectionLevel: 'M' });
        setQrDataUrl(url);
      } catch {
        setQrDataUrl(null); // fall back to manual secret entry
      }
      setCode('');
      setStage('enroll');
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string };
      setErr(e2.body?.error || e2.message || 'Could not start authenticator setup. Please sign in again.');
      setStage('credentials');
    } finally {
      setBusy(null);
    }
  }

  async function verifyEnrollment(e: React.FormEvent) {
    e.preventDefault();
    if (!setupToken) return;
    setBusy('enroll-verify');
    setErr(null);
    try {
      // Use the canonical identity the setup ticket was minted for, falling back
      // to the typed fields only if it is somehow unavailable.
      const id = enrollIdentity.current ?? { tenant: tenant.trim(), user: user.trim() };
      const result = await api.auth.mfaVerify(id.tenant, id.user, code.trim(), setupToken);
      if (!result.token) {
        setErr('Enrolment did not complete. Please try the code again.');
        return;
      }
      pendingResult.current = { token: result.token, tenant: result.tenant, user: result.user };
      await persistToken(pendingResult.current);
      // Offer biometric for next time, then enter the app.
      if (helloAvailable) setStage('biometric-offer');
      else router.replace(returnTo);
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string };
      setErr(e2.body?.error || 'That code did not match. Open your authenticator app and enter the current 6-digit code.');
    } finally {
      setBusy(null);
    }
  }

  // ── TOTP step-up (already enrolled) ─────────────────────────────────────
  async function verifyStepUp(e: React.FormEvent) {
    e.preventDefault();
    setBusy('totp');
    setErr(null);
    try {
      const result = await api.auth.mfaLogin(tenant.trim(), user.trim(), code.trim());
      await finishLogin(result);
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string };
      setErr(e2.body?.error || 'That code did not match. Open your authenticator app and enter the current 6-digit code.');
    } finally {
      setBusy(null);
    }
  }

  // ── Biometric ───────────────────────────────────────────────────────────
  async function signInWithHello() {
    if (!tenant.trim() || !user.trim()) {
      setErr('Enter your organization and email above, then use Windows Hello.');
      return;
    }
    setBusy('hello');
    resetTransient();
    try {
      const result = await signInWithPasskey({ tenant: tenant.trim(), user: user.trim() });
      localStorage.setItem(LS_TENANT, tenant.trim());
      localStorage.setItem(LS_USER, user.trim());
      await finishLogin(result);
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string; name?: string };
      setErr(e2.name === 'NotAllowedError'
        ? 'Windows Hello was cancelled or timed out.'
        : e2.body?.error || e2.message || 'Biometric sign-in failed. Set up fingerprint/face on this device first.');
    } finally {
      setBusy(null);
    }
  }

  async function enableBiometric() {
    setBusy('enroll-bio');
    setErr(null);
    try {
      await registerPasskey('This device');
      router.replace(returnTo);
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string; name?: string };
      setErr(e2.name === 'NotAllowedError'
        ? 'Biometric setup was cancelled. You can enable it later in Settings.'
        : e2.body?.error || e2.message || 'Could not enable biometric sign-in. You can set it up later in Settings.');
    } finally {
      setBusy(null);
    }
  }

  // ── SSO + dev ───────────────────────────────────────────────────────────
  async function beginSso() {
    setBusy('sso');
    setErr(null);
    try {
      const url = await api.auth.oidcAuthorizeUrl(ssoReturnUrl);
      window.location.assign(url);
    } catch (e) {
      setErr((e as { message?: string }).message || 'SSO could not be started.');
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
      const result = await api.auth.signIn(tenant.trim(), user.trim());
      if (result.mfaRequired) { setStage('totp'); setCode(''); }
      else await finishLogin(result);
    } catch (e) {
      setErr((e as { body?: { error?: string } }).body?.error || 'Dev sign-in failed.');
    } finally {
      setBusy(null);
    }
  }

  // ── Email-free reset ────────────────────────────────────────────────────
  async function submitReset(e: React.FormEvent) {
    e.preventDefault();
    setBusy('reset');
    setErr(null);
    try {
      await api.auth.passwordResetWithTotp(tenant.trim(), user.trim(), resetCode.trim(), resetNewPw);
      setStage('credentials');
      setResetCode('');
      setResetNewPw('');
      setInfo('Password updated. Sign in with your new password.');
    } catch (ex) {
      const e2 = ex as { body?: { error?: string }; message?: string };
      setErr(e2.body?.error || e2.message || 'Reset failed. Check your authenticator code and try again.');
    } finally {
      setBusy(null);
    }
  }

  const stepIndex = stage === 'credentials' || stage === 'reset' ? 0 : stage === 'biometric-offer' ? 2 : 1;

  return (
    <AuthScaffold variant="signin">
      {stage !== 'reset' && (
        <div className="rp-pair-steps" aria-hidden>
          {['Credentials', 'Authenticator', 'Done'].map((label, i) => (
            <div key={label} className={`rp-pair-step ${i === stepIndex ? 'active' : ''} ${i < stepIndex ? 'done' : ''}`}>
              <span className="rp-pair-step-dot">{i < stepIndex ? '✓' : i + 1}</span>
              <span className="rp-pair-step-label">{label}</span>
            </div>
          ))}
        </div>
      )}

      {err && <div className="banner danger" role="alert">{err}</div>}
      {info && <div className="banner info" role="status">{info}</div>}

      {/* ── Credentials ───────────────────────────────────────────── */}
      {stage === 'credentials' && (
        <>
          <div className="rp-auth-head">
            <div className="rp-auth-eyebrow">Welcome back</div>
            <h1 className="rp-auth-title">Sign in to RadioPad</h1>
            <p className="rp-auth-sub">Enter your organization credentials. You&rsquo;ll confirm with your authenticator app.</p>
          </div>

          <form className="rp-auth-form" onSubmit={submitPassword}>
            <div className="section-block">
              <label htmlFor="li-tenant">Organization</label>
              <input id="li-tenant" value={tenant} onChange={(e) => setTenant(e.target.value)} required autoComplete="organization" placeholder="your-org" />
            </div>
            <div className="section-block">
              <label htmlFor="li-email">Work email</label>
              <input id="li-email" type="email" value={user} onChange={(e) => setUser(e.target.value)} required autoComplete="username" placeholder="you@org.example" />
            </div>
            <div className="section-block">
              <label htmlFor="li-pw">Password</label>
              <div className="rp-pw-field">
                <input
                  id="li-pw"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  autoComplete="current-password"
                  placeholder="Your password"
                />
                <button type="button" className="rp-pw-toggle" onClick={() => setShowPassword((s) => !s)} aria-label={showPassword ? 'Hide password' : 'Show password'}>
                  {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                </button>
              </div>
              <p className="rp-field-hint">
                <button type="button" className="rp-auth-link" onClick={() => { resetTransient(); setStage('reset'); }}>
                  Forgot password? Use your authenticator
                </button>
              </p>
            </div>

            <div className="rp-auth-actions">
              <button className="primary" type="submit" disabled={busy !== null}>
                {busy === 'password' || busy === 'enroll-begin' ? 'Signing in…' : 'Sign in'}
              </button>
              {helloAvailable && (
                <button className="primary-ghost" type="button" onClick={signInWithHello} disabled={busy !== null}>
                  {busy === 'hello' ? 'Waiting for Windows Hello…' : 'Sign in with fingerprint or face'}
                </button>
              )}
            </div>
          </form>

          <div className="rp-auth-divider">Other options</div>
          <div className="rp-auth-actions">
            {ssoEnabled && (
              <button className="ghost" type="button" onClick={beginSso} disabled={busy !== null}>
                {busy === 'sso' ? 'Starting SSO…' : 'Continue with SSO'}
              </button>
            )}
            {/* /pair is a (desktop) route. Login is (shared) and ships everywhere, so on the web
                console and the mobile companion this button led straight to "Page not found". */}
            {isDesktopSurface && (
              <button className="ghost" type="button" onClick={() => router.push('/pair')} disabled={busy !== null}>
                Pair a device
              </button>
            )}
          </div>

          {devLoginEnabled && (
            <details className="rp-advanced">
              <summary>Dev / test bearer sign-in</summary>
              <form className="rp-auth-form rp-mt-sm" onSubmit={saveDevSession}>
                <div className="rp-auth-actions">
                  <button className="subtle" type="submit" disabled={busy !== null}>
                    {busy === 'dev' ? 'Signing in…' : 'Continue with dev session'}
                  </button>
                </div>
              </form>
            </details>
          )}

          <div className="rp-auth-foot">
            New accounts are created by your administrator. Need access? Contact your RadioPad admin.
          </div>
        </>
      )}

      {/* ── TOTP step-up ──────────────────────────────────────────── */}
      {stage === 'totp' && (
        <>
          <div className="rp-auth-head">
            <div className="rp-auth-eyebrow">Two-factor</div>
            <h1 className="rp-auth-title">Enter your authenticator code</h1>
            <p className="rp-auth-sub">Open your authenticator app and enter the current 6-digit code for <strong>{user}</strong>.</p>
          </div>
          <form className="rp-auth-form" onSubmit={verifyStepUp}>
            <div className="section-block">
              <label htmlFor="li-totp">6-digit code</label>
              <input id="li-totp" className="rp-otp-input" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(digitsOnly(e.target.value))} autoFocus placeholder="000000" />
            </div>
            <div className="rp-auth-actions">
              <button className="primary" type="submit" disabled={busy !== null || code.length !== 6}>
                {busy === 'totp' ? 'Verifying…' : 'Verify & sign in'}
              </button>
              <button className="ghost" type="button" onClick={() => { setStage('credentials'); setCode(''); }} disabled={busy !== null}>Back</button>
            </div>
          </form>
        </>
      )}

      {/* ── Forced first-login enrolment ──────────────────────────── */}
      {stage === 'enroll' && (
        <>
          <div className="rp-auth-head">
            <div className="rp-auth-eyebrow">Secure your account</div>
            <h1 className="rp-auth-title">Set up your authenticator</h1>
            <p className="rp-auth-sub">Scan this with Google Authenticator, Authy, or 1Password, then enter the 6-digit code to finish.</p>
          </div>
          <div className="rp-totp-enroll">
            <div className="rp-qr">
              {qrDataUrl
                // eslint-disable-next-line @next/next/no-img-element
                ? <img src={qrDataUrl} width={200} height={200} alt="Authenticator QR code" />
                : <div className="rp-qr-fallback">QR unavailable — add the key below manually.</div>}
            </div>
            {enrollSecret && (
              <div className="rp-secret-tile">
                <div className="rp-secret-label">Setup key</div>
                <code className="rp-secret-code">{enrollSecret.match(/.{1,4}/g)?.join(' ') ?? enrollSecret}</code>
                <button type="button" className="rp-auth-link" onClick={() => navigator.clipboard?.writeText(enrollSecret).then(() => setInfo('Setup key copied.')).catch(() => {})}>Copy key</button>
              </div>
            )}
          </div>
          <form className="rp-auth-form" onSubmit={verifyEnrollment}>
            <div className="section-block">
              <label htmlFor="li-enroll">6-digit code</label>
              <input id="li-enroll" className="rp-otp-input" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(digitsOnly(e.target.value))} placeholder="000000" />
            </div>
            <div className="rp-auth-actions">
              <button className="primary" type="submit" disabled={busy !== null || code.length !== 6}>
                {busy === 'enroll-verify' ? 'Verifying…' : 'Verify & continue'}
              </button>
              <button className="ghost" type="button" onClick={() => { setStage('credentials'); setCode(''); setSetupToken(null); }} disabled={busy !== null}>Cancel</button>
            </div>
          </form>
        </>
      )}

      {/* ── Biometric offer (after first-login enrolment) ─────────── */}
      {stage === 'biometric-offer' && (
        <>
          <div className="rp-auth-head">
            <div className="rp-auth-eyebrow">Almost done</div>
            <h1 className="rp-auth-title">Enable fingerprint / face?</h1>
            <p className="rp-auth-sub">Use Windows Hello on this device to sign in with a touch or glance next time — your password and authenticator stay as backup.</p>
          </div>
          <div className="rp-auth-actions">
            <button className="primary" type="button" onClick={enableBiometric} disabled={busy !== null}>
              {busy === 'enroll-bio' ? 'Setting up…' : 'Enable biometric sign-in'}
            </button>
            <button className="ghost" type="button" onClick={() => router.replace(returnTo)} disabled={busy !== null}>Skip for now</button>
          </div>
        </>
      )}

      {/* ── Email-free reset ──────────────────────────────────────── */}
      {stage === 'reset' && (
        <>
          <div className="rp-auth-head">
            <div className="rp-auth-eyebrow">Reset password</div>
            <h1 className="rp-auth-title">Reset with your authenticator</h1>
            <p className="rp-auth-sub">No email needed — prove it&rsquo;s you with your authenticator code, then choose a new password.</p>
          </div>
          <form className="rp-auth-form" onSubmit={submitReset}>
            <div className="section-block">
              <label htmlFor="rs-tenant">Organization</label>
              <input id="rs-tenant" value={tenant} onChange={(e) => setTenant(e.target.value)} required placeholder="your-org" />
            </div>
            <div className="section-block">
              <label htmlFor="rs-email">Work email</label>
              <input id="rs-email" type="email" value={user} onChange={(e) => setUser(e.target.value)} required placeholder="you@org.example" />
            </div>
            <div className="section-block">
              <label htmlFor="rs-code">Authenticator code</label>
              <input id="rs-code" className="rp-otp-input" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={resetCode} onChange={(e) => setResetCode(digitsOnly(e.target.value))} placeholder="000000" />
            </div>
            <div className="section-block">
              <label htmlFor="rs-pw">New password</label>
              <input id="rs-pw" type="password" value={resetNewPw} onChange={(e) => setResetNewPw(e.target.value)} required autoComplete="new-password" minLength={12} placeholder="At least 12 characters" />
              <p className="rp-field-hint">Use at least 12 characters.</p>
            </div>
            <div className="rp-auth-actions">
              <button className="primary" type="submit" disabled={busy !== null || resetCode.length !== 6 || resetNewPw.length < 12}>
                {busy === 'reset' ? 'Updating…' : 'Update password'}
              </button>
              <button className="ghost" type="button" onClick={() => { setStage('credentials'); resetTransient(); }} disabled={busy !== null}>Back to sign in</button>
            </div>
          </form>
        </>
      )}
    </AuthScaffold>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<AuthScaffold variant="signin"><div className="rp-auth-head"><h1 className="rp-auth-title">Sign in to RadioPad</h1></div></AuthScaffold>}>
      <LoginContent />
    </Suspense>
  );
}
