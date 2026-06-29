'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import StatusBadge, { reportStatusTone } from '@/components/ui/StatusBadge';

export default function ReportsPage() {
  const [reports, setReports] = useState<Report[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchReports = useCallback(() => {
    setLoading(true);
    setError(null);
    return api.reports.list()
      .then(setReports)
      .catch((e) => setError(e?.message || 'Failed to load'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { void fetchReports(); }, [fetchReports]);

  return (
    <Container>
      <PageHeader title="Reports" description="All radiology reports in your workspace." />

      <section className="rp-panel" aria-live="polite" aria-busy={loading}>
        {loading ? (
          <TableSkeleton rows={6} cols={6} />
        ) : error ? (
          <ErrorState title="Couldn't load reports" message={error} onRetry={() => { void fetchReports(); }} />
        ) : reports.length === 0 ? (
          <EmptyState
            title="No reports yet"
            description="Reports created in your workspace will appear here."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr><th>Accession</th><th>Modality</th><th>Body part</th><th>Status</th><th>Updated</th><th aria-label="Actions" /></tr>
            </thead>
            <tbody className="rp-stagger">
              {reports.map((r) => (
                <tr key={r.id}>
                  <td>{r.study.accessionNumber}</td>
                  <td>{r.study.modality}</td>
                  <td>{r.study.bodyPart}</td>
                  <td><StatusBadge tone={reportStatusTone(r.status)}>{statusLabel(r.status)}</StatusBadge></td>
                  <td className="muted">{new Date(r.updatedAt).toLocaleString()}</td>
                  <td><Link href={reportHref(r.id)}>Open →</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </Container>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}
