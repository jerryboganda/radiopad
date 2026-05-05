'use client';

import { useEffect, useState } from 'react';
import { api, type Provider } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';

/**
 * Iter-35 PROV-007 — admin surface for the per-provider OAuth refresh-token
 * vault. The page never receives ciphertext; it only renders the boolean
 * `hasToken`, the timestamps, and the rotation policy. Saving a token uses
 * a write-only field that is cleared after submit so the plaintext is not
 * retained in component state.
 *
 * Locked design tokens only: `.rp-container`, `.rp-page-title`, `.rp-page-sub`,
 * `.rp-panel`, `.rp-panel-title`, `.rp-input`, `.rp-row`, `.banner.warn|ok`,
 * `.badge.ok|warn`, button variants `.primary` / `.ghost` / `.subtle`.
 */
type Status = {
  hasToken: boolean;
  updatedAt: string | null;
  expiresAt: string | null;
  rotationPolicy: 'never' | 'before_expiry' | 'every_24h';
};

export default function ProviderOAuthAdminPage() {
  const [providerId, setProviderId] = useState<string | null>(null);
  const [provider, setProvider] = useState<Provider | null>(null);
  const [status, setStatus] = useState<Status | null>(null);
  const [token, setToken] = useState('');
  const [expiresAt, setExpiresAt] = useState('');
  const [rotationPolicy, setRotationPolicy] = useState<Status['rotationPolicy']>('before_expiry');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  useEffect(() => {
    setProviderId(readQueryParam('id'));
  }, []);

  useEffect(() => {
    if (!providerId) return;
    void refresh();
    void api.providers.list().then((rows) => {
      const p = rows.find((r) => r.id === providerId) ?? null;
      setProvider(p);
    });
  }, [providerId]);

  async function refresh() {
    if (!providerId) return;
    try {
      const s = await api.providers.oauth.status(providerId);
      setStatus(s);
      setRotationPolicy(s.rotationPolicy);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function save() {
    if (!providerId) { setError('Missing provider id.'); return; }
    if (!token) { setError('Refresh token is required.'); return; }
    setBusy(true); setError(null); setInfo(null);
    try {
      await api.providers.oauth.save(providerId, {
        refreshToken: token,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
        rotationPolicy,
      });
      setToken('');
      setInfo('Refresh token saved. Plaintext discarded from this browser.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!providerId) { setError('Missing provider id.'); return; }
    if (!confirm('Delete the stored OAuth refresh token for this provider?')) return;
    setBusy(true); setError(null); setInfo(null);
    try {
      await api.providers.oauth.delete(providerId);
      setInfo('Refresh token deleted.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">
        Provider OAuth — {provider?.name ?? providerId ?? ''}
      </h1>
      <p className="rp-page-sub">
        Per-provider OAuth refresh-token vault. Tokens are encrypted at rest
        with AES-256-GCM under a tenant-scoped KMS key; only{' '}
        <code>hasToken</code>, timestamps, and the rotation policy are
        returned to the UI. The rotation worker scans every 15 min and
        refreshes any token whose policy + expiry indicate it is due.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {providerId === '' && <div className="banner warn">Missing provider id.</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">Status</div>
        {status === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : (
          <ul className="rp-list">
            <li>
              <strong>Stored:</strong>{' '}
              <span className={status.hasToken ? 'badge ok' : 'badge warn'}>
                {status.hasToken ? 'yes' : 'no'}
              </span>
            </li>
            <li><strong>Updated:</strong> {status.updatedAt ?? '—'}</li>
            <li><strong>Expires:</strong> {status.expiresAt ?? '—'}</li>
            <li><strong>Rotation policy:</strong> <code>{status.rotationPolicy}</code></li>
          </ul>
        )}
        <div className="rp-row" style={{ marginTop: 8 }}>
          <button className="ghost" onClick={refresh} disabled={busy}>Refresh</button>
          {status?.hasToken && (
            <button
              className="subtle"
              style={{ marginLeft: 8 }}
              onClick={remove}
              disabled={busy}
            >
              Delete token
            </button>
          )}
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Save / replace token</div>
        <p className="rp-page-sub">
          Paste the OAuth refresh token issued by the upstream IdP. The
          plaintext is sent over TLS to the API, encrypted, and immediately
          cleared from this browser. RadioPad never logs the token.
        </p>
        <label className="rp-page-sub" htmlFor="rt">Refresh token</label>
        <input
          id="rt"
          className="rp-input"
          type="password"
          autoComplete="off"
          spellCheck={false}
          value={token}
          onChange={(e) => setToken(e.target.value)}
          placeholder="rt_..."
        />
        <label className="rp-page-sub" htmlFor="exp" style={{ marginTop: 8 }}>
          Expires at (optional)
        </label>
        <input
          id="exp"
          className="rp-input"
          type="datetime-local"
          value={expiresAt}
          onChange={(e) => setExpiresAt(e.target.value)}
        />
        <label className="rp-page-sub" htmlFor="pol" style={{ marginTop: 8 }}>
          Rotation policy
        </label>
        <select
          id="pol"
          className="rp-input"
          value={rotationPolicy}
          onChange={(e) => setRotationPolicy(e.target.value as Status['rotationPolicy'])}
        >
          <option value="before_expiry">before_expiry — refresh within 1 h of expiry</option>
          <option value="every_24h">every_24h — refresh every 24 h</option>
          <option value="never">never — manual rotation only</option>
        </select>
        <div className="rp-row" style={{ marginTop: 12 }}>
          <button className="primary" onClick={save} disabled={busy || !token}>
            {status?.hasToken ? 'Replace token' : 'Save token'}
          </button>
        </div>
      </div>
    </div>
  );
}
