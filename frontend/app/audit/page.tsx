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
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Activity log</h1>
          <p className="rp-page-sub">
            A tamper-proof record of every important action in your workspace — AI requests, report edits, exports, and policy decisions.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <table className="rp-table">
          <thead>
            <tr>
              <th>When</th>
              <th>What happened</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {events.map((e) => (
              <tr key={e.id}>
                <td style={{ whiteSpace: 'nowrap', color: 'var(--text-muted)' }}>
                  {new Date(e.createdAt).toLocaleString()}
                </td>
                <td><span className={`badge ${badgeFor(e.action)}`}>{actionLabel(e.action)}</span></td>
                <td style={{ color: 'var(--text-muted)', fontSize: 13 }}>{truncate(e.detailsJson, 80)}</td>
              </tr>
            ))}
            {events.length === 0 && <tr><td colSpan={3} style={{ color: 'var(--text-muted)' }}>No activity yet.</td></tr>}
          </tbody>
        </table>
        <details className="rp-advanced">
          <summary>About tamper-proof logging</summary>
          <p className="rp-page-sub">
            Each entry is sealed with a cryptographic fingerprint that links to the previous one, so any tampering is detectable. Entries can never be edited or deleted.
          </p>
        </details>
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
