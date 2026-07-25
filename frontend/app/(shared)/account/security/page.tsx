'use client';

import { useCallback, useEffect, useState } from 'react';
import Container from '@/components/shell/Container';
import QRCode from 'qrcode';
import {
  Check,
  ChevronRight,
  Fingerprint,
  KeyRound,
  Lock,
  Mail,
  MonitorSmartphone,
  MoreVertical,
  Ticket,
  Usb,
} from 'lucide-react';
import { api, type AuthSessionRow, type WebAuthnCredentialRow } from '@/lib/api';
import { registerPasskey, isPlatformAuthenticatorAvailable } from '@/lib/webauthn';

/**
 * AUTH-001/003/009 — per-user sign-in security. Lets a radiologist enroll this
 * device's Windows Hello (fingerprint / face) as a passkey, set up a TOTP
 * authenticator app, and review/revoke their own active sessions.
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
  // True when we couldn't confirm enrolment status either way — distinct from
  // "confirmed not enrolled" so the UI doesn't offer "Set up" as if that were known.
  const [statusCheckFailed, setStatusCheckFailed] = useState(false);
  // AUTH-009 — the caller's own active sessions.
  const [sessions, setSessions] = useState<AuthSessionRow[] | null>(null);
  const [showAllSessions, setShowAllSessions] = useState(false);
  const [revokingId, setRevokingId] = useState<string | null>(null);

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
        setTotpDone(mfaEnabled);
        setStatusCheckFailed(false);
      } catch {
        // Could not confirm enrolment status (session drift, network hiccup).
        // Don't claim "not enrolled" — that's a lie by omission that already
        // cost a support cycle once. Surface it instead of guessing.
        setStatusCheckFailed(true);
      }
    } else {
      setStatusCheckFailed(true);
    }
    try {
      setSessions((await api.auth.sessions()).sessions);
    } catch {
      setSessions(null);
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

  async function revokeSession(id: string) {
    if (!confirm('Sign that device out? It will need to sign in again.')) return;
    setRevokingId(id);
    setErr(null); setInfo(null);
    try {
      await api.auth.revokeSession(id);
      await refresh();
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setRevokingId(null);
    }
  }

  function backupMethodStub(name: string) {
    setErr(null);
    setInfo(`${name} isn't available yet.`);
  }

  const visibleSessions = sessions && !showAllSessions ? sessions.slice(0, 4) : sessions;

  return (
    <Container>
      <div className="rp-security-hero">
        <span className="rp-security-hero-icon" aria-hidden>
          <Lock size={22} strokeWidth={2} />
        </span>
        <div>
          <h1 className="rp-page-title">Sign-in &amp; devices</h1>
          <p className="rp-page-sub">
            Secure your account by enrolling Windows Hello or other authenticators.
            Each passkey or authenticator is tied to one device and never leaves it.
          </p>
        </div>
      </div>

      {err && <div className="banner warn">{err}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-security-grid">
        {/* Fingerprint / Face (Windows Hello) */}
        <div className="rp-panel rp-security-card">
          <div className="rp-security-card-head">
            <div className="rp-panel-title">Fingerprint / Face (Windows Hello)</div>
            {helloAvailable && <span className="badge ok">Most secure</span>}
          </div>
          {helloAvailable === false ? (
            <p className="rp-page-sub">
              This device has no built-in fingerprint reader or face camera that RadioPad can use.
              You can still sign in with an email magic link.
            </p>
          ) : (
            <div className="rp-security-split">
              <span className="rp-security-illustration" aria-hidden>
                <Fingerprint size={44} strokeWidth={1.6} />
              </span>
              <div className="rp-security-split-body">
                <p className="rp-page-sub">
                  Use your device&apos;s fingerprint reader or front camera for fast, passwordless
                  sign-in. The biometric check happens on this device — RadioPad never sees or
                  stores your biometrics.
                </p>
                <ul className="rp-security-checklist">
                  <li><Check size={15} strokeWidth={2.2} aria-hidden /> Device-bound and secure</li>
                  <li><Check size={15} strokeWidth={2.2} aria-hidden /> Fast and seamless access</li>
                  <li><Check size={15} strokeWidth={2.2} aria-hidden /> Never leaves your device</li>
                </ul>
                <button className="primary" type="button" onClick={enroll} disabled={busy || helloAvailable === null}>
                  {busy ? 'Follow the Windows Hello prompt…' : 'Add this device'}
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Enrolled devices */}
        <div className="rp-panel rp-security-card">
          <div className="rp-panel-title">Enrolled devices</div>
          {creds === null ? (
            <p className="rp-page-sub">Loading…</p>
          ) : creds.length === 0 ? (
            <div className="rp-security-empty">
              <span className="rp-security-illustration" aria-hidden>
                <MonitorSmartphone size={44} strokeWidth={1.6} />
              </span>
              <p className="rp-security-empty-title">No devices enrolled yet</p>
              <p className="rp-page-sub">
                Add Windows Hello or another authenticator to enable passwordless sign-in.
              </p>
              <button
                className="primary-ghost"
                type="button"
                onClick={enroll}
                disabled={busy || helloAvailable !== true}
              >
                Add a device
              </button>
            </div>
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

        {/* Authenticator app (TOTP) */}
        <div className="rp-panel rp-security-card">
          <div className="rp-security-card-head">
            <div className="rp-panel-title">Authenticator app (one-time codes)</div>
            <span className={`badge ${totpDone ? 'ok' : ''}`}>{totpDone ? 'Enabled' : 'Not set up'}</span>
          </div>
          {totpDone ? (
            <div className="rp-security-split">
              <span className="rp-security-illustration" aria-hidden>
                <KeyRound size={44} strokeWidth={1.6} />
              </span>
              <div className="rp-security-split-body">
                <p className="rp-page-sub"><strong>Authenticator app is enabled.</strong> You can use a 6-digit code as a second step at sign-in.</p>
              </div>
            </div>
          ) : !totpSecret ? (
            <div className="rp-security-split">
              <span className="rp-security-illustration" aria-hidden>
                <KeyRound size={44} strokeWidth={1.6} />
              </span>
              <div className="rp-security-split-body">
                <p className="rp-page-sub">
                  Use Google Authenticator, Microsoft Authenticator, Authy, or any TOTP app to
                  generate 6-digit codes. No SMS, no cost. Add an extra layer of security to your account.
                </p>
                {statusCheckFailed && (
                  <p className="rp-page-sub">
                    Couldn&apos;t confirm whether an authenticator app is already set up for this
                    account — showing this button just in case, but if you&apos;ve already enrolled,
                    try{' '}
                    <button type="button" className="subtle" onClick={() => void refresh()} disabled={busy}>
                      checking again
                    </button>{' '}
                    before setting up a second one.
                  </p>
                )}
                <button className="primary" type="button" onClick={startTotp} disabled={busy}>
                  {busy ? 'Working…' : 'Set up authenticator'}
                </button>
              </div>
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

        {/* Backup methods — visual only; none of these have a backend yet. */}
        <div className="rp-panel rp-security-card">
          <div className="rp-panel-title">Backup methods</div>
          <ul className="rp-security-method-list">
            <li className="rp-security-method-row">
              <span className="rp-security-illustration sm" aria-hidden><Mail size={18} strokeWidth={1.8} /></span>
              <div className="rp-security-method-text">
                <strong>Recovery email</strong>
                <p className="rp-page-sub">Add a recovery email to regain access.</p>
              </div>
              <span className="badge">Not set</span>
              <button
                type="button"
                className="icon-btn"
                aria-label="Recovery email"
                onClick={() => backupMethodStub('Recovery email')}
              >
                <ChevronRight size={16} strokeWidth={2} aria-hidden />
              </button>
            </li>
            <li className="rp-security-method-row">
              <span className="rp-security-illustration sm" aria-hidden><Ticket size={18} strokeWidth={1.8} /></span>
              <div className="rp-security-method-text">
                <strong>Backup codes</strong>
                <p className="rp-page-sub">Generate and store backup codes.</p>
              </div>
              <span className="badge">Not set</span>
              <button
                type="button"
                className="icon-btn"
                aria-label="Backup codes"
                onClick={() => backupMethodStub('Backup codes')}
              >
                <ChevronRight size={16} strokeWidth={2} aria-hidden />
              </button>
            </li>
            <li className="rp-security-method-row">
              <span className="rp-security-illustration sm" aria-hidden><Usb size={18} strokeWidth={1.8} /></span>
              <div className="rp-security-method-text">
                <strong>Security key (FIDO2)</strong>
                <p className="rp-page-sub">Use a physical security key for sign-in.</p>
              </div>
              <span className="badge">Not set</span>
              <button
                type="button"
                className="icon-btn"
                aria-label="Security key (FIDO2)"
                onClick={() => backupMethodStub('Security key (FIDO2)')}
              >
                <ChevronRight size={16} strokeWidth={2} aria-hidden />
              </button>
            </li>
          </ul>
        </div>
      </div>

      {/* Active sessions (AUTH-009) */}
      <div className="rp-panel">
        <div className="rp-panel-title">Active sessions</div>
        {sessions === null ? (
          <p className="rp-page-sub">Couldn&apos;t load your active sessions.</p>
        ) : sessions.length === 0 ? (
          <p className="rp-page-sub">No active sessions.</p>
        ) : (
          <>
            <table className="rp-table">
              <thead>
                <tr>
                  <th>Device</th>
                  <th>Location / IP</th>
                  <th>Last active</th>
                  <th>Status</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {visibleSessions?.map((s) => (
                  <tr key={s.id}>
                    <td>
                      <strong>{s.deviceCategory ?? 'Unknown device'}</strong>
                      {s.deviceDetail && <div className="rp-page-sub">{s.deviceDetail}</div>}
                    </td>
                    <td>{s.ipAddress ?? '—'}</td>
                    <td>{s.isCurrent ? 'Now' : new Date(s.issuedAt).toLocaleString()}</td>
                    <td>
                      {s.isCurrent ? (
                        <span className="badge ok">
                          <Check size={12} strokeWidth={2.4} aria-hidden /> This session
                        </span>
                      ) : (
                        <span className="badge">Active</span>
                      )}
                    </td>
                    <td>
                      {!s.isCurrent && (
                        <button
                          type="button"
                          className="icon-btn"
                          aria-label="Sign out this device"
                          onClick={() => revokeSession(s.id)}
                          disabled={revokingId === s.id}
                        >
                          <MoreVertical size={16} strokeWidth={2} aria-hidden />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {sessions.length > 4 && !showAllSessions && (
              <div className="rp-security-sessions-more">
                <button type="button" className="subtle" onClick={() => setShowAllSessions(true)}>
                  View all sessions
                </button>
              </div>
            )}
          </>
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
