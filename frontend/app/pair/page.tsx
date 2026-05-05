'use client';

/**
 * PRD DESK-008 — desktop device-pairing screen.
 *
 * Drives the RFC 8628 device authorization grant exposed by
 * `POST /api/auth/device/{authorize,token}`:
 *   1. Ask the desktop shell (when present) for a stable device fingerprint.
 *   2. Request a `(deviceCode, userCode)` pair from the backend.
 *   3. Display the `userCode` to the operator and tell them to approve it
 *      from a signed-in browser at `/devices`.
 *   4. Poll `device/token` at the documented interval until the operator
 *      approves. On success, persist the bearer to the OS keyring (web:
 *      browser-local fallback) and stash the desktop pairing token via the
 *      `device_pairing_token_set` Tauri command so the shell can re-use it.
 */

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, setActiveAuthToken } from '@/lib/api';
import { setAuthToken } from '@/lib/secureAuth';

const LS_TENANT = 'radiopad.tenant';
const LS_USER = 'radiopad.user';

type Phase = 'idle' | 'requesting' | 'awaiting' | 'paired' | 'error';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function tauriInvoke(): undefined | ((cmd: string, args?: any) => Promise<any>) {
  if (typeof window === 'undefined') return undefined;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const t: any = (window as any).__TAURI__;
  return t?.core?.invoke || t?.invoke;
}

export default function PairPage() {
  const router = useRouter();
  const [phase, setPhase] = useState<Phase>('idle');
  const [userCode, setUserCode] = useState<string | null>(null);
  const [verificationUri, setVerificationUri] = useState<string>('/devices');
  const [expiresAt, setExpiresAt] = useState<number | null>(null);
  const [secondsLeft, setSecondsLeft] = useState<number>(0);
  const [err, setErr] = useState<string | null>(null);
  const [paired, setPaired] = useState<{ tenant: string; user: string } | null>(null);
  const pollTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  function clearPoll() {
    if (pollTimer.current) {
      clearTimeout(pollTimer.current);
      pollTimer.current = null;
    }
  }

  useEffect(() => () => clearPoll(), []);

  useEffect(() => {
    if (!expiresAt) return;
    const tick = () => {
      const remaining = Math.max(0, Math.floor((expiresAt - Date.now()) / 1000));
      setSecondsLeft(remaining);
    };
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [expiresAt]);

  async function startPairing() {
    setPhase('requesting');
    setErr(null);
    setPaired(null);
    setUserCode(null);
    clearPoll();
    try {
      const invoke = tauriInvoke();
      let fingerprint: string | undefined;
      if (invoke) {
        try {
          fingerprint = (await invoke('device_fingerprint')) as string;
        } catch {
          /* fall through — backend treats as anonymous */
        }
      }
      const res = await api.auth.deviceAuthorize('radiopad-desktop', fingerprint);
      setUserCode(res.userCode);
      setVerificationUri(res.verificationUri || '/devices');
      setExpiresAt(Date.now() + res.expiresIn * 1000);
      setPhase('awaiting');
      schedulePoll(res.deviceCode, res.interval);
    } catch (e) {
      const ex = e as { body?: { error?: string }; message?: string };
      setErr(ex.body?.error || ex.message || 'request failed');
      setPhase('error');
    }
  }

  function schedulePoll(deviceCode: string, intervalSeconds: number) {
    let interval = Math.max(1, intervalSeconds);
    const poll = async () => {
      try {
        const tok = await api.auth.deviceToken(deviceCode);
        await setAuthToken(tok.accessToken);
        setActiveAuthToken(tok.accessToken);
        try {
          if (typeof localStorage !== 'undefined') {
            localStorage.setItem(LS_TENANT, tok.tenant);
            localStorage.setItem(LS_USER, tok.user);
          }
        } catch {
          /* ignore */
        }
        const invoke = tauriInvoke();
        if (invoke) {
          try {
            await invoke('device_pairing_token_set', { token: tok.accessToken });
          } catch {
            /* keyring unavailable in dev preview — token still in secureAuth */
          }
        }
        setPaired({ tenant: tok.tenant, user: tok.user });
        setPhase('paired');
      } catch (e) {
        const ex = e as { body?: { error?: string }; status?: number };
        const code = ex.body?.error;
        if (code === 'authorization_pending') {
          pollTimer.current = setTimeout(poll, interval * 1000);
          return;
        }
        if (code === 'slow_down') {
          interval += 5;
          pollTimer.current = setTimeout(poll, interval * 1000);
          return;
        }
        setErr(code || `error ${ex.status ?? '?'}`);
        setPhase('error');
      }
    };
    pollTimer.current = setTimeout(poll, interval * 1000);
  }

  function done() {
    router.push('/');
  }

  return (
    <div className="rp-container rp-pair-shell">
      <h1 className="rp-page-title">Pair this device</h1>
      <p className="rp-page-sub">
        RadioPad uses the OAuth 2.0 device authorization grant (RFC 8628) to bind this
        desktop install to a tenant and user. The pairing token is stored in the OS
        keyring; this device never sees a password.
      </p>

      {err && <div className="banner danger">{err}</div>}

      {phase === 'idle' && (
        <div className="rp-panel">
          <p>
            Click <strong>Request pairing code</strong> below, then approve the code on a
            web browser already signed in to RadioPad.
          </p>
          <div className="rp-row">
            <button className="primary" type="button" onClick={startPairing}>
              Request pairing code →
            </button>
          </div>
        </div>
      )}

      {phase === 'requesting' && (
        <div className="rp-panel">
          <p>Requesting code…</p>
        </div>
      )}

      {phase === 'awaiting' && userCode && (
        <div className="rp-panel">
          <p className="rp-page-sub">
            On a signed-in browser, open{' '}
            <code>{verificationUri}</code> and enter:
          </p>
          <div className="section-block rp-pair-code-tile">
            <code className="rp-pair-code">{userCode}</code>
          </div>
          <p className="rp-page-sub">
            Code expires in <code>{secondsLeft}s</code>. Polling for approval…
          </p>
          <div className="rp-row">
            <button
              className="ghost"
              type="button"
              onClick={() => {
                clearPoll();
                setPhase('idle');
                setUserCode(null);
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {phase === 'paired' && paired && (
        <div className="rp-panel">
          <div className="banner info">
            Paired as <code>{paired.user}</code> in tenant <code>{paired.tenant}</code>.
          </div>
          <div className="rp-row">
            <button className="primary" type="button" onClick={done}>
              Continue →
            </button>
          </div>
        </div>
      )}

      {phase === 'error' && (
        <div className="rp-panel">
          <div className="rp-row">
            <button className="primary" type="button" onClick={startPairing}>
              Try again →
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
