'use client';

import { useCallback, useEffect, useState } from 'react';
import { api } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

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
  54: 'Provider configured',
};

export default function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    api.audit.query({ take: 200 })
      .then((e) => setEvents(e as AuditEvent[]))
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <Container>
      <PageHeader
        title="Activity log"
        description="A tamper-proof record of every important action in your workspace — AI requests, report edits, exports, and policy decisions."
      />

      {error && <ErrorState message={error} onRetry={load} />}

      {!error && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div aria-live="polite" aria-busy={loading}>
            {loading ? (
              <TableSkeleton rows={8} cols={3} />
            ) : events.length === 0 ? (
              <EmptyState
                title="No activity yet"
                description="Actions across your workspace — AI requests, edits, exports, and policy decisions — will appear here."
              />
            ) : (
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
                </tbody>
              </table>
            )}
          </div>
          <details className="rp-advanced">
            <summary>About tamper-proof logging</summary>
            <p className="rp-page-sub">
              Each entry is sealed with a cryptographic fingerprint that links to the previous one, so any tampering is detectable. Entries can never be edited or deleted.
            </p>
          </details>
        </div>
      )}
    </Container>
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
