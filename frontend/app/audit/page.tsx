'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type AuditEvent = {
  id: string;
  tenantId: string;
  userId: string | null;
  reportId: string | null;
  action: number | string;
  detailsJson: string;
  createdAt: string;
  integrityChain: string;
};

const ACTION_LABEL: Record<number, string> = {
  0: 'AI request',
  1: 'AI response',
  2: 'Report edited',
  3: 'Report exported',
  4: 'Report acknowledged',
  5: 'Provider blocked',
  6: 'Rulebook approved',
  7: 'Rulebook deprecated',
  8: 'User login',
  9: 'Policy violation',
};

export default function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.audit.query({ take: 200 })
      .then((e) => setEvents(e as AuditEvent[]))
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Audit log</h1>
      <p className="rp-page-sub">
        Append-only record of every AI event, report change, and policy decision. Each event carries an{' '}
        <code>integrityChain</code> hash so tampering is detectable.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <table className="rp-table">
          <thead>
            <tr>
              <th>Time</th>
              <th>Action</th>
              <th>Details</th>
              <th>Chain</th>
            </tr>
          </thead>
          <tbody>
            {events.map((e) => (
              <tr key={e.id}>
                <td style={{ whiteSpace: 'nowrap', color: 'var(--text-muted)' }}>
                  {new Date(e.createdAt).toLocaleString()}
                </td>
                <td><span className={`badge ${badgeFor(e.action)}`}>{actionLabel(e.action)}</span></td>
                <td><code style={{ fontSize: 11 }}>{truncate(e.detailsJson, 80)}</code></td>
                <td><code style={{ fontSize: 11 }}>{e.integrityChain.slice(0, 12)}…</code></td>
              </tr>
            ))}
            {events.length === 0 && <tr><td colSpan={4} style={{ color: 'var(--text-muted)' }}>No audit events yet.</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function actionLabel(a: AuditEvent['action']) {
  if (typeof a === 'string') return a;
  return ACTION_LABEL[a] ?? `Action ${a}`;
}
function badgeFor(a: AuditEvent['action']) {
  const n = typeof a === 'number' ? a : -1;
  if (n === 9 || n === 5) return 'danger';
  if (n === 4 || n === 6) return 'ok';
  if (n === 0 || n === 1) return 'ai';
  return '';
}
function truncate(s: string, n: number) {
  return s.length > n ? s.slice(0, n) + '…' : s;
}
