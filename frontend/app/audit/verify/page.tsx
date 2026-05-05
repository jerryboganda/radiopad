'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type AuditEvent = {
  id: string;
  tenantId: string;
  action: number | string;
  detailsJson: string;
  integrityChain: string;
  createdAt: string;
};

/**
 * Recompute the SHA-256 hash chain client-side and flag any rows that
 * disagree with the server-stored `integrityChain`. The server uses the
 * canonical payload `{id}|{tenantId}|{(int)action}|{detailsJson}|{prev}`.
 */
async function sha256Hex(text: string): Promise<string> {
  const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(buf)).map((b) => b.toString(16).padStart(2, '0')).join('');
}

export default function AuditVerifyPage() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [results, setResults] = useState<Array<{ id: string; ok: boolean; expected: string; actual: string }>>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.audit.query({ take: 500 })
      .then((e) => setEvents((e as AuditEvent[]).slice().reverse())) // oldest first
      .catch((e: Error) => setError(e.message));
  }, []);

  async function verify() {
    setBusy(true);
    try {
      let prev = '';
      const out: typeof results = [];
      for (const e of events) {
        const action = typeof e.action === 'number' ? e.action : Number(e.action);
        const payload = `${e.id}|${e.tenantId}|${action}|${e.detailsJson}|${prev}`;
        const computed = await sha256Hex(payload);
        out.push({ id: e.id, ok: computed === e.integrityChain, expected: e.integrityChain, actual: computed });
        prev = e.integrityChain;
      }
      setResults(out);
    } finally {
      setBusy(false);
    }
  }

  const broken = results.filter((r) => !r.ok).length;

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Audit chain verifier</h1>
      <p className="rp-page-sub">
        Recomputes the SHA-256 hash chain in your browser and flags any row whose stored{' '}
        <code>integrityChain</code> diverges from the recomputed value. Tampering with any prior
        row breaks the chain at that row and at every row after it.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-toolbar">
        <button className="primary" onClick={verify} disabled={busy || events.length === 0}>
          {busy ? 'Verifying…' : `Verify ${events.length} events`}
        </button>
        {results.length > 0 && (
          broken === 0
            ? <span className="badge ok">Chain intact ({results.length} rows)</span>
            : <span className="badge danger">{broken} broken row{broken === 1 ? '' : 's'}</span>
        )}
      </div>

      {results.length > 0 && (
        <div className="rp-panel">
          <table className="rp-table">
            <thead>
              <tr><th>Event</th><th>Expected (stored)</th><th>Computed</th><th>Status</th></tr>
            </thead>
            <tbody>
              {results.map((r) => (
                <tr key={r.id}>
                  <td><code style={{ fontSize: 11 }}>{r.id.slice(0, 8)}…</code></td>
                  <td><code style={{ fontSize: 11 }}>{r.expected.slice(0, 12)}…</code></td>
                  <td><code style={{ fontSize: 11 }}>{r.actual.slice(0, 12)}…</code></td>
                  <td>{r.ok ? <span className="badge ok">ok</span> : <span className="badge danger">mismatch</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
