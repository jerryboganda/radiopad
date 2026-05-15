'use client';

import Link from 'next/link';
import { useEffect, useMemo, useRef, useState } from 'react';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';

const PAGE_SIZE = 25;

const STATUS_TO_INT: Record<string, number | undefined> = {
  all: undefined,
  draft: 0,
  validated: 1,
  acknowledged: 2,
  exported: 3,
};

export default function DashboardPage() {
  const handledNewReport = useRef(false);
  const [reports, setReports] = useState<Report[]>([]);
  const [total, setTotal] = useState(0);
  const [me, setMe] = useState<{ tenant: { displayName: string }; user: { email: string } } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [modalityFilter, setModalityFilter] = useState<string>('all');
  const [search, setSearch] = useState<string>('');
  const [page, setPage] = useState<number>(0);

  useEffect(() => {
    api.me().then(setMe).catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    const handle = setTimeout(() => {
      api.reports
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
        .catch((e: Error) => setError(e.message));
    }, 200);
    return () => clearTimeout(handle);
  }, [statusFilter, modalityFilter, search, page]);

  // Reset to page 0 whenever filters change.
  useEffect(() => { setPage(0); }, [statusFilter, modalityFilter, search]);

  async function newReport() {
    const r = await api.reports.create({ modality: 'CT', bodyPart: 'Chest', indication: 'New report' });
    location.href = reportHref(r.id);
  }

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const params = new URLSearchParams(window.location.search);
    if (params.get('new') !== '1' || handledNewReport.current) return;
    handledNewReport.current = true;
    void newReport();
  }, []);

  const modalities = useMemo(
    () => Array.from(new Set(reports.map((r) => r.study.modality))).sort(),
    [reports],
  );

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Reports</h1>
      <p className="rp-page-sub">{me ? `${me.tenant.displayName} — signed in as ${me.user.email}` : 'Loading…'}</p>

      {error && (
        <div className="banner warn">
          Backend not reachable: {error}. Start the API with <code>dotnet run --project backend/RadioPad.Api/src/RadioPad.Api</code>.
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-row between" style={{ marginBottom: 12 }}>
          <div className="rp-panel-title" style={{ marginBottom: 0 }}>Recent reports</div>
          <button className="primary" onClick={newReport}>+ New report</button>
        </div>

        <div className="rp-toolbar" style={{ marginBottom: 12 }}>
          <input
            className="rp-input"
            placeholder="Search accession / body part / indication"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{ minWidth: 280 }}
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
            {/* fallback common modalities */}
            {modalities.length === 0 && ['CT', 'MRI', 'XR', 'US'].map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
          <span className="badge info">{total} total</span>
        </div>

        <table className="rp-table">
          <thead>
            <tr>
              <th>Accession</th>
              <th>Modality</th>
              <th>Body part</th>
              <th>Status</th>
              <th>Updated</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {reports.length === 0 && (
              <tr><td colSpan={6} style={{ color: 'var(--text-muted)' }}>
                {total === 0 ? 'No reports yet. Create one to begin.' : 'No reports match the current filters.'}
              </td></tr>
            )}
            {reports.map((r) => (
              <tr key={r.id}>
                <td><code>{r.study.accessionNumber}</code></td>
                <td>{r.study.modality}</td>
                <td>{r.study.bodyPart}</td>
                <td><span className="badge">{statusLabel(r.status)}</span></td>
                <td style={{ color: 'var(--text-muted)' }}>{new Date(r.updatedAt).toLocaleString()}</td>
                <td><Link href={reportHref(r.id)}>Open →</Link></td>
              </tr>
            ))}
          </tbody>
        </table>

        {total > PAGE_SIZE && (
          <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
            <button className="ghost" disabled={page === 0} onClick={() => setPage((p) => Math.max(0, p - 1))}>← Prev</button>
            <span className="badge" style={{ alignSelf: 'center' }}>{page + 1} / {totalPages}</span>
            <button className="ghost" disabled={page + 1 >= totalPages} onClick={() => setPage((p) => p + 1)}>Next →</button>
          </div>
        )}
      </div>
    </div>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}
