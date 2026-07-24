'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Trash2 } from 'lucide-react';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import StatusBadge, { reportStatusTone } from '@/components/ui/StatusBadge';
import { useToast } from '@/components/ui/ToastProvider';

export default function ReportsPage() {
  const { toast } = useToast();
  const [reports, setReports] = useState<Report[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Date/time filter — both bounds are optional and applied client-side to the
  // already-fetched worklist (`datetime-local`, so minute granularity).
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const fetchReports = useCallback(() => {
    setLoading(true);
    setError(null);
    return api.reports.list()
      .then(setReports)
      .catch((e) => setError(e?.message || 'Failed to load'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { void fetchReports(); }, [fetchReports]);

  const filtered = useMemo(() => {
    const fromMs = from ? new Date(from).getTime() : null;
    const toMs = to ? new Date(to).getTime() : null;
    return reports.filter((r) => {
      const t = new Date(r.updatedAt).getTime();
      if (fromMs !== null && t < fromMs) return false;
      if (toMs !== null && t > toMs) return false;
      return true;
    });
  }, [reports, from, to]);

  const filterActive = from !== '' || to !== '';

  async function handleDelete(r: Report) {
    const accession = r.study.accessionNumber || 'this report';
    if (!window.confirm(
      `Delete report ${accession}?\n\nIt will be removed from your worklist. This is a reversible archive — the clinical record and audit trail are preserved and it can be recovered by an admin.`,
    )) return;
    setDeletingId(r.id);
    try {
      await api.reports.archive(r.id);
      setReports((prev) => prev.filter((x) => x.id !== r.id));
      toast({ tone: 'success', title: 'Report deleted', message: `${accession} was removed from your worklist.` });
    } catch (e) {
      toast({ tone: 'danger', title: 'Could not delete report', message: (e as { message?: string })?.message || 'Please try again.' });
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <Container>
      <PageHeader title="Reports" description="All radiology reports in your workspace." />

      {!loading && !error && reports.length > 0 && (
        <div className="rp-reports-filter" role="search">
          <div className="section-block">
            <label htmlFor="rp-filter-from">Updated from</label>
            <input
              id="rp-filter-from"
              type="datetime-local"
              value={from}
              max={to || undefined}
              onChange={(e) => setFrom(e.target.value)}
            />
          </div>
          <div className="section-block">
            <label htmlFor="rp-filter-to">Updated to</label>
            <input
              id="rp-filter-to"
              type="datetime-local"
              value={to}
              min={from || undefined}
              onChange={(e) => setTo(e.target.value)}
            />
          </div>
          {filterActive && (
            <button type="button" className="ghost" onClick={() => { setFrom(''); setTo(''); }}>
              Clear filter
            </button>
          )}
        </div>
      )}

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
        ) : filtered.length === 0 ? (
          <EmptyState
            title="No reports in this date range"
            description="Adjust or clear the date filter to see more reports."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr><th>Accession</th><th>Modality</th><th>Body part</th><th>Status</th><th>Updated</th><th aria-label="Actions" /></tr>
            </thead>
            <tbody className="rp-stagger">
              {filtered.map((r) => (
                <tr key={r.id}>
                  <td>{r.study.accessionNumber}</td>
                  <td>{r.study.modality}</td>
                  <td>{r.study.bodyPart}</td>
                  <td><StatusBadge tone={reportStatusTone(r.status)}>{statusLabel(r.status)}</StatusBadge></td>
                  <td className="muted">{new Date(r.updatedAt).toLocaleString()}</td>
                  <td className="rp-reports-actions">
                    <Link href={reportHref(r.id)}>Open →</Link>
                    <button
                      type="button"
                      className="ghost rp-reports-delete"
                      onClick={() => { void handleDelete(r); }}
                      disabled={deletingId === r.id}
                      aria-label={`Delete report ${r.study.accessionNumber || ''}`.trim()}
                      title="Delete report"
                    >
                      <Trash2 size={15} aria-hidden />
                      <span>{deletingId === r.id ? 'Deleting…' : 'Delete'}</span>
                    </button>
                  </td>
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
