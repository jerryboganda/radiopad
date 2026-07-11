'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import StatusBadge, { reportStatusTone } from '@/components/ui/StatusBadge';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

const PAGE_SIZE = 25;

const STATUS_TO_INT: Record<string, number | undefined> = {
  all: undefined,
  draft: 0,
  validated: 1,
  acknowledged: 2,
  exported: 3,
};

export default function DashboardPage() {
  const router = useRouter();
  const handledNewReport = useRef(false);
  const [reports, setReports] = useState<Report[]>([]);
  const [total, setTotal] = useState(0);
  const [me, setMe] = useState<{ tenant: { displayName: string }; user: { email: string } } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [modalityFilter, setModalityFilter] = useState<string>('all');
  const [search, setSearch] = useState<string>('');
  const [page, setPage] = useState<number>(0);

  useEffect(() => {
    api.me().then(setMe).catch((e: Error) => setError(e.message));
  }, []);

  const fetchReports = useCallback(() => {
    setLoading(true);
    setError(null);
    return api.reports
      .listPaged({
        modality: modalityFilter === 'all' ? undefined : modalityFilter,
        status: STATUS_TO_INT[statusFilter],
        q: search.trim() || undefined,
        skip: page * PAGE_SIZE,
        take: PAGE_SIZE,
      })
      .then(({ items, total }) => {
        setReports(items);
        setTotal(total);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [modalityFilter, statusFilter, search, page]);

  useEffect(() => {
    const handle = setTimeout(() => { void fetchReports(); }, 200);
    return () => clearTimeout(handle);
  }, [fetchReports]);

  // Reset to page 0 whenever filters change.
  useEffect(() => { setPage(0); }, [statusFilter, modalityFilter, search]);

  // "+ New report" now opens the guided intake wizard (study context → findings →
  // history → provider → generate) instead of dropping into an empty editor. The
  // wizard creates the draft itself once the radiologist hits Generate.
  const newReport = useCallback(() => {
    router.push('/reports/new');
  }, [router]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const params = new URLSearchParams(window.location.search);
    if (params.get('new') !== '1' || handledNewReport.current) return;
    handledNewReport.current = true;
    void newReport();
  }, [newReport]);

  const modalities = useMemo(
    () => Array.from(new Set(reports.map((r) => r.study.modality))).sort(),
    [reports],
  );

  // Quick at-a-glance counts for the currently loaded page of reports.
  const pageCounts = useMemo(() => {
    const c = { draft: 0, validated: 0, signed: 0 };
    for (const r of reports) {
      const tone = reportStatusTone(r.status);
      if (tone === 'neutral') c.draft += 1;
      else if (tone === 'info') c.validated += 1;
      else if (tone === 'success') c.signed += 1;
    }
    return c;
  }, [reports]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const filtersActive = statusFilter !== 'all' || modalityFilter !== 'all' || search.trim() !== '';

  return (
    <Container>
      <PageHeader
        title="Reports"
        description={me ? `${me.tenant.displayName} — signed in as ${me.user.email}` : 'Loading workspace…'}
        primaryAction={
          <button className="primary" onClick={newReport}>+ New report</button>
        }
      />

      <div className="metric-grid rp-stagger" style={{ marginBottom: 16 }}>
        <div className="metric-card" data-tone="info">
          <div className="metric-card-label">Total reports</div>
          <div className="metric-card-value"><AnimatedNumber value={total} /></div>
        </div>
        <div className="metric-card">
          <div className="metric-card-label">Draft (this page)</div>
          <div className="metric-card-value"><AnimatedNumber value={pageCounts.draft} /></div>
        </div>
        <div className="metric-card" data-tone="review">
          <div className="metric-card-label">Validated (this page)</div>
          <div className="metric-card-value"><AnimatedNumber value={pageCounts.validated} /></div>
        </div>
        <div className="metric-card" data-tone="ready">
          <div className="metric-card-label">Signed / exported (this page)</div>
          <div className="metric-card-value"><AnimatedNumber value={pageCounts.signed} /></div>
        </div>
      </div>

      <section className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={loading}>
        <div className="rp-toolbar" style={{ marginBottom: 16 }}>
          <input
            className="rp-input"
            placeholder="Search accession / body part / indication"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{ minWidth: 280, flex: '1 1 280px' }}
            aria-label="Search reports"
          />
          <select className="rp-input" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} aria-label="Filter by status">
            <option value="all">All statuses</option>
            <option value="draft">Draft</option>
            <option value="validated">Validated</option>
            <option value="acknowledged">Acknowledged</option>
            <option value="exported">Exported</option>
          </select>
          <select className="rp-input" value={modalityFilter} onChange={(e) => setModalityFilter(e.target.value)} aria-label="Filter by modality">
            <option value="all">All modalities</option>
            {modalities.map((m) => <option key={m} value={m}>{m}</option>)}
            {modalities.length === 0 && ['CT', 'MRI', 'XR', 'US'].map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
          <StatusBadge tone="info">{total} total</StatusBadge>
        </div>

        {loading && reports.length === 0 ? (
          <TableSkeleton rows={6} cols={5} />
        ) : error ? (
          <ErrorState
            title="Couldn't load reports"
            message={`Backend not reachable: ${error}. Start the API with dotnet run --project backend/RadioPad.Api/src/RadioPad.Api`}
            onRetry={() => { void fetchReports(); }}
          />
        ) : reports.length === 0 ? (
          <EmptyState
            title={filtersActive ? 'No reports match your filters' : 'No reports yet'}
            description={filtersActive ? 'Try clearing filters or broadening your search.' : 'Create your first report to get started.'}
            action={
              filtersActive ? (
                <button className="ghost" onClick={() => { setStatusFilter('all'); setModalityFilter('all'); setSearch(''); }}>
                  Clear filters
                </button>
              ) : (
                <button className="primary" onClick={newReport}>+ New report</button>
              )
            }
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Accession</th>
                <th>Modality</th>
                <th>Body part</th>
                <th>Status</th>
                <th>Updated</th>
                <th aria-label="Actions" />
              </tr>
            </thead>
            <tbody className="rp-stagger">
              {reports.map((r) => (
                <tr key={r.id}>
                  <td><code>{r.study.accessionNumber}</code></td>
                  <td>{r.study.modality}</td>
                  <td>{r.study.bodyPart}</td>
                  <td><StatusBadge tone={reportStatusTone(r.status)}>{statusLabel(r.status)}</StatusBadge></td>
                  <td style={{ color: 'var(--text-muted)' }}>{new Date(r.updatedAt).toLocaleString()}</td>
                  <td><Link href={reportHref(r.id)}>Open →</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {!loading && !error && reports.length > 0 && total > PAGE_SIZE && (
          <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
            <button className="ghost" disabled={page === 0} onClick={() => setPage((p) => Math.max(0, p - 1))}>← Prev</button>
            <StatusBadge tone="neutral">{page + 1} / {totalPages}</StatusBadge>
            <button className="ghost" disabled={page + 1 >= totalPages} onClick={() => setPage((p) => p + 1)}>Next →</button>
          </div>
        )}
      </section>
    </Container>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}
