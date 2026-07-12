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
import AuthScaffold from '@/components/auth/AuthScaffold';
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

function StepRail({ step, complete }: { step: 1 | 2 | 3; complete: boolean }) {
  const steps: Array<{ n: 1 | 2 | 3; label: string }> = [
    { n: 1, label: 'Request code' },
    { n: 2, label: 'Approve in browser' },
    { n: 3, label: 'Done' },
  ];
  return (
    <div className="rp-pair-steps">
      {steps.map((s) => {
        const done = complete || s.n < step;
        const state = done ? 'done' : s.n === step ? 'active' : '';
        return (
          <div key={s.n} className={`rp-pair-step ${state}`}>
            <span className="rp-pair-step-dot">
              {done ? (
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                  <path d="m5 12 5 5L20 7" />
                </svg>
              ) : s.n}
            </span>
            <span className="rp-pair-step-label">{s.label}</span>
          </div>
        );
      })}
    </div>
  );
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

  const step: 1 | 2 | 3 = phase === 'paired'
    ? 3
    : phase === 'awaiting' || (phase === 'error' && userCode)
      ? 2
      : 1;

  return (
    <AuthScaffold variant="pair">
      <div className="rp-auth-head">
        <div className="rp-auth-eyebrow">Device pairing</div>
        <h1 className="rp-auth-title">Pair this device</h1>
        <p className="rp-auth-sub">
          RadioPad binds this desktop install to a tenant and user with a one-time code
          (RFC 8628). The pairing token is stored in the OS keyring — this device never sees a password.
        </p>
      </div>

      <StepRail step={step} complete={phase === 'paired'} />

      {err && <div className="banner danger" role="alert">{err}</div>}

      {(phase === 'idle' || phase === 'requesting') && (
        <>
          <p className="rp-auth-sub" style={{ marginBottom: 16 }}>
            Click <strong>Request pairing code</strong>, then approve the code from a browser
            already signed in to RadioPad.
          </p>
          <div className="rp-auth-actions">
            <button className="primary" type="button" onClick={startPairing} disabled={phase === 'requesting'}>
              {phase === 'requesting' ? 'Requesting code…' : 'Request pairing code'}
            </button>
          </div>
        </>
      )}

      {phase === 'awaiting' && userCode && (
        <>
          <p className="rp-auth-sub">
            On a signed-in browser, open <code>{verificationUri}</code> and enter this code:
          </p>
          <div className="section-block rp-pair-code-tile">
            <code className="rp-pair-code">{userCode}</code>
          </div>
          <p className="rp-auth-hint" aria-live="polite" style={{ marginTop: 0 }}>
            Code expires in <code>{secondsLeft}s</code>. Waiting for approval…
          </p>
          <div className="rp-auth-actions">
            <button
              className="ghost"
              type="button"
              onClick={() => { clearPoll(); setPhase('idle'); setUserCode(null); }}
            >
              Cancel
            </button>
          </div>
        </>
      )}

      {phase === 'paired' && paired && (
        <>
          <div className="banner ok" role="status">
            Paired as <code>{paired.user}</code> in tenant <code>{paired.tenant}</code>.
          </div>
          <div className="rp-auth-actions">
            <button className="primary" type="button" onClick={done}>
              Continue
            </button>
          </div>
        </>
      )}

      {phase === 'error' && (
        <div className="rp-auth-actions">
          <button className="primary" type="button" onClick={startPairing}>
            Try again
          </button>
        </div>
      )}

      <div className="rp-auth-foot">
        Prefer email? <a className="rp-auth-link" href="/login">Back to sign in</a>
      </div>
    </AuthScaffold>
  );
}
