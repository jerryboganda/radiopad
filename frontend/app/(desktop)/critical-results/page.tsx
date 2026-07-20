'use client';

/**
 * PRD §14.15 (CR-001..010) — the radiologist's critical-results queue.
 *
 * Every critical finding whose communication loop is still open, most urgent
 * deadline first, with the overdue ones called out. Acknowledging from here is
 * the common case (you already made the call, you just need to record the
 * read-back), so it is one click per row. Nothing is ever acknowledged or
 * closed automatically — the server re-checks permission and audits each change.
 */

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { api } from '@/lib/api';
import {
  CRITICALITY_BADGE_TONE,
  CRITICALITY_LABELS,
  CRITICAL_STATUS_BADGE_TONE,
  COMMUNICATION_METHOD_LABELS,
} from '@/lib/api';
import type { CriticalResult } from '@/lib/api';
import { usePermissions } from '@/lib/permissions';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { formatDueIn } from '@/components/critical/CriticalResultPanel';

type Filter = 'open' | 'overdue' | 'all';

const FILTER_LABELS: Record<Filter, string> = {
  open: 'Still open',
  overdue: 'Overdue',
  all: 'Everything',
};

function isOpenLoop(c: CriticalResult): boolean {
  return c.status !== 'Acknowledged' && c.status !== 'Closed';
}

export default function CriticalResultsQueuePage() {
  const { can } = usePermissions();
  const canManage = can('critical_results.manage');

  const [filter, setFilter] = useState<Filter>('open');
  const [items, setItems] = useState<CriticalResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionErr, setActionErr] = useState<string | null>(null);

  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(t);
  }, []);

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    const request =
      filter === 'overdue'
        ? api.criticalResults.list({ overdue: true, take: 500 })
        : api.criticalResults.list({ take: 500 });

    request
      .then((rows) => setItems(filter === 'open' ? rows.filter(isOpenLoop) : rows))
      .catch((e: Error) => setErr(e.message))
      .finally(() => setLoading(false));
  }, [filter]);

  useEffect(() => {
    load();
  }, [load]);

  async function acknowledge(c: CriticalResult) {
    setBusyId(c.id);
    setActionErr(null);
    try {
      const updated = await api.criticalResults.acknowledge(c.id);
      setItems((prev) =>
        filter === 'open'
          ? prev.filter((row) => row.id !== updated.id) // loop closed → leaves the open queue
          : prev.map((row) => (row.id === updated.id ? updated : row)),
      );
    } catch (e) {
      setActionErr((e as Error).message);
    } finally {
      setBusyId(null);
    }
  }

  const overdueCount = items.filter((c) => c.isOverdue).length;

  return (
    <Container>
      <PageHeader
        title="Critical results"
        description="Critical findings you've logged and the state of each communication loop. Most urgent deadline first."
      />

      <div className="rp-panel" aria-live="polite" aria-busy={loading}>
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 8,
            alignItems: 'center',
            marginBottom: 12,
          }}
        >
          <div style={{ display: 'flex', gap: 6 }} role="group" aria-label="Filter critical results">
            {(Object.keys(FILTER_LABELS) as Filter[]).map((f) => (
              <button
                key={f}
                type="button"
                className={filter === f ? 'primary-ghost' : 'subtle'}
                aria-pressed={filter === f}
                onClick={() => setFilter(f)}
              >
                {FILTER_LABELS[f]}
              </button>
            ))}
          </div>
          {!loading && !err && overdueCount > 0 && (
            <span className="badge danger">{overdueCount} overdue</span>
          )}
        </div>

        {actionErr && (
          <p className="rp-page-sub" role="alert" style={{ marginBottom: 10 }}>
            <span className="badge danger">Action failed</span> {actionErr}
          </p>
        )}

        {loading ? (
          <TableSkeleton rows={5} cols={6} />
        ) : err ? (
          <ErrorState title="Couldn't load critical results" message={err} onRetry={load} />
        ) : items.length === 0 ? (
          <EmptyState
            title={filter === 'overdue' ? 'Nothing overdue' : 'No critical results'}
            description={
              filter === 'overdue'
                ? 'Every critical result has been communicated inside its deadline.'
                : 'Critical findings you log from a report will appear here until the loop is closed.'
            }
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Criticality</th>
                <th>Finding</th>
                <th>Status</th>
                <th>Deadline</th>
                <th>Communicated to</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.id} data-testid="critical-result-row">
                  <td>
                    <span className={`badge ${CRITICALITY_BADGE_TONE[c.criticality]}`}>
                      {CRITICALITY_LABELS[c.criticality]}
                    </span>
                  </td>
                  <td>{c.findingSummary}</td>
                  <td>
                    <span className={`badge ${CRITICAL_STATUS_BADGE_TONE[c.status]}`}>
                      {c.status}
                    </span>
                  </td>
                  <td>
                    {isOpenLoop(c) ? (
                      <span className={`badge ${c.isOverdue ? 'danger' : 'info'}`}>
                        {formatDueIn(c.dueAt, now)}
                      </span>
                    ) : (
                      <span className="badge ok">closed</span>
                    )}
                  </td>
                  <td>
                    {c.communicatedTo ? (
                      <>
                        {c.communicatedTo}
                        {c.communicationMethod && (
                          <span className="rp-page-sub">
                            {' '}
                            ({COMMUNICATION_METHOD_LABELS[c.communicationMethod].toLowerCase()})
                          </span>
                        )}
                      </>
                    ) : (
                      <span className="badge warn">not yet communicated</span>
                    )}
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {canManage && isOpenLoop(c) && c.communicatedAt !== null && (
                      <button
                        type="button"
                        className="primary-ghost"
                        disabled={busyId === c.id}
                        onClick={() => acknowledge(c)}
                      >
                        Acknowledge
                      </button>
                    )}{' '}
                    <Link href={reportHref(c.reportId)}>Open report →</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Container>
  );
}
