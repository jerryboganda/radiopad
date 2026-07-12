'use client';

import { useCallback, useEffect, useState } from 'react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import QRCode from 'qrcode';
import { api, type WebAuthnCredentialRow } from '@/lib/api';
import { registerPasskey, isPlatformAuthenticatorAvailable } from '@/lib/webauthn';

/**
 * AUTH-001 — per-user sign-in security. Lets a radiologist enroll this device's
 * Windows Hello (fingerprint / face) as a passkey and manage existing passkeys.
 * After enrolling, the login screen offers a one-touch / one-glance sign-in.
 */
export default function AccountSecurityPage() {
  const [creds, setCreds] = useState<WebAuthnCredentialRow[] | null>(null);
  const [helloAvailable, setHelloAvailable] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  // Authenticator-app OTP (TOTP) enrollment.
  const [totpSecret, setTotpSecret] = useState<string | null>(null);
  const [totpQr, setTotpQr] = useState<string | null>(null);
  const [totpCode, setTotpCode] = useState('');
  const [totpDone, setTotpDone] = useState(false);

  const refresh = useCallback(async () => {
    try {
      setCreds(await api.auth.webAuthnCredentials());
    } catch (e) {
      setErr((e as Error).message);
    }
    // Reflect persisted TOTP enrolment so the card doesn't offer "Set up"
    // again after a reload / fresh sign-in (status lives in the backend, not
    // in this component's session-scoped state).
    const id = currentIdentity();
    if (id) {
      try {
        const { mfaEnabled } = await api.auth.mfaStatus(id.tenant, id.user);
        if (mfaEnabled) setTotpDone(true);
      } catch {
        /* non-fatal: fall back to the enrolment button */
      }
    }
  }, []);

  useEffect(() => {
    isPlatformAuthenticatorAvailable().then(setHelloAvailable).catch(() => setHelloAvailable(false));
    void refresh();
  }, [refresh]);

  async function enroll() {
    setBusy(true); setErr(null); setInfo(null);
    try {
      const label = defaultDeviceLabel();
      await registerPasskey(label);
      setInfo('This device is enrolled. You can now sign in with fingerprint or face.');
      await refresh();
    } catch (e) {
      const ex = e as { name?: string; body?: { error?: string }; message?: string };
      setErr(ex.name === 'NotAllowedError'
        ? 'Enrollment was cancelled or timed out.'
        : ex.body?.error || ex.message || 'Could not enroll this device.');
    } finally {
      setBusy(false);
    }
  }

  async function startTotp() {
    const id = currentIdentity();
    if (!id) { setErr('Could not determine your account. Sign in again, then retry.'); return; }
    setBusy(true); setErr(null); setInfo(null); setTotpDone(false);
    try {
      const res = await api.auth.mfaEnroll(id.tenant, id.user);
      setTotpSecret(res.secret);
      setTotpQr(await QRCode.toDataURL(res.otpauth, { margin: 1, width: 200 }).catch(() => ''));
    } catch (e) {
      setErr((e as { body?: { error?: string }; message?: string }).body?.error || (e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function verifyTotp() {
    const id = currentIdentity();
    if (!id) { setErr('Could not determine your account. Sign in again, then retry.'); return; }
    setBusy(true); setErr(null); setInfo(null);
    try {
      const res = await api.auth.mfaVerify(id.tenant, id.user, totpCode.trim());
      if (res.mfaEnabled) {
        setTotpDone(true);
        setTotpSecret(null);
        setTotpQr(null);
        setTotpCode('');
        setInfo('Authenticator app enabled. You can use a 6-digit code as a second step at sign-in.');
      }
    } catch {
      setErr('That code did not match. Check your authenticator app and try the current 6-digit code.');
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: string) {
    setBusy(true); setErr(null); setInfo(null);
    try {
      await api.auth.webAuthnDeleteCredential(id);
      await refresh();
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Container>
      <PageHeader
        title="Sign-in & devices"
        description="Enroll this device's Windows Hello (fingerprint or face) so you can sign in without an email link. Each passkey is tied to one device and never leaves it."
      />

      {err && <div className="banner warn">{err}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">Fingerprint / face (Windows Hello)</div>
        {helloAvailable === false ? (
          <p className="rp-page-sub">
            This device has no built-in fingerprint reader or face camera that RadioPad can use.
            You can still sign in with an email magic link.
          </p>
        ) : (
          <>
            <p className="rp-page-sub">
              Uses your device&apos;s fingerprint reader or front camera for face recognition. The biometric
              check happens on this device — RadioPad only receives a signed confirmation, never your fingerprint or face.
            </p>
            <div className="rp-auth-action-list">
              <button className="primary" type="button" onClick={enroll} disabled={busy || helloAvailable === null}>
                {busy ? 'Follow the Windows Hello prompt…' : 'Add this device'}
              </button>
            </div>
          </>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Enrolled devices</div>
        {creds === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : creds.length === 0 ? (
          <p className="rp-page-sub">No devices enrolled yet.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Device</th>
                <th>Added</th>
                <th>Last used</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {creds.map((c) => (
                <tr key={c.id}>
                  <td>{c.label || 'Passkey'}</td>
                  <td>{new Date(c.createdAt).toLocaleString()}</td>
                  <td>{c.lastUsedAt ? new Date(c.lastUsedAt).toLocaleString() : '—'}</td>
                  <td>
                    <button className="ghost" type="button" onClick={() => remove(c.id)} disabled={busy}>
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Authenticator app (one-time codes)</div>
        <p className="rp-page-sub">
          A free alternative to email links: use Google Authenticator, Microsoft Authenticator, Authy,
          or any TOTP app to generate a 6-digit code. No SMS, no cost.
        </p>
        {totpDone ? (
          <p className="rp-page-sub"><strong>Authenticator app is enabled.</strong></p>
        ) : !totpSecret ? (
          <div className="rp-auth-action-list">
            <button className="primary" type="button" onClick={startTotp} disabled={busy}>
              {busy ? 'Working…' : 'Set up authenticator app'}
            </button>
          </div>
        ) : (
          <div className="section-block">
            <p className="rp-page-sub">
              Scan this QR code with your authenticator app:
            </p>
            {totpQr
              // eslint-disable-next-line @next/next/no-img-element -- data-URL QR, next/image adds no value
              ? <p><img src={totpQr} alt="Authenticator setup QR code" width={200} height={200} /></p>
              : null}
            <p className="rp-page-sub">
              Can&apos;t scan? Choose <strong>Add account → Enter a setup key</strong> and type this key instead:
            </p>
            <p><code>{totpSecret}</code></p>
            <p className="rp-page-sub">
              Account name: your email · Type: time-based. Then enter the current 6-digit code to confirm:
            </p>
            <input
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              placeholder="123456"
              value={totpCode}
              onChange={(e) => setTotpCode(e.target.value.replace(/\D/g, ''))}
            />
            <div className="rp-auth-action-list rp-mt-sm">
              <button className="primary" type="button" onClick={verifyTotp} disabled={busy || totpCode.trim().length !== 6}>
                {busy ? 'Verifying…' : 'Confirm code'}
              </button>
            </div>
          </div>
        )}
      </div>
    </Container>
  );
}

function currentIdentity(): { tenant: string; user: string } | null {
  if (typeof window === 'undefined') return null;
  const tenant = localStorage.getItem('radiopad.tenant');
  const user = localStorage.getItem('radiopad.user');
  return tenant && user ? { tenant, user } : null;
}

function defaultDeviceLabel(): string {
  if (typeof navigator === 'undefined') return 'This device';
  const ua = navigator.userAgent;
  if (ua.includes('Windows')) return 'Windows device';
  if (ua.includes('Mac')) return 'Mac';
  if (ua.includes('Android')) return 'Android device';
  if (ua.includes('iPhone') || ua.includes('iPad')) return 'iOS device';
  return 'This device';
}
