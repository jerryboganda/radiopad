'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import {
  ArrowUpDown,
  CheckCircle2,
  ChevronsLeft,
  ChevronsRight,
  ChevronLeft,
  ChevronRight,
  Download,
  Eye,
  FileText,
  MoreVertical,
  Search,
  Trash2,
  Upload,
  X,
} from 'lucide-react';
import { api, type CatalogItem, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import StatusBadge, { reportStatusTone } from '@/components/ui/StatusBadge';
import { useToast } from '@/components/ui/ToastProvider';
import Sparkline from '@/components/ui/Sparkline';

type SortBy = 'updatedAt' | 'accession' | 'modality' | 'status';
type SortDir = 'asc' | 'desc';
type StatsResponse = {
  total: number;
  validated: number;
  acknowledged: number;
  exported: number;
  trend: { total: number[]; validated: number[]; acknowledged: number[]; exported: number[] };
};

const STATUS_OPTIONS = [
  { value: '', label: 'All' },
  { value: '0', label: 'Draft' },
  { value: '1', label: 'Validated' },
  { value: '2', label: 'Acknowledged' },
  { value: '3', label: 'Exported' },
];
const SORT_FIELDS: Array<{ value: SortBy; label: string }> = [
  { value: 'updatedAt', label: 'Updated' },
  { value: 'accession', label: 'Accession' },
  { value: 'modality', label: 'Modality' },
  { value: 'status', label: 'Status' },
];
const PAGE_SIZES = [10, 25, 50, 100];
const EXPORT_SAFETY_CAP = 20000;

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

function csvEscape(v: string): string {
  return /[",\r\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v;
}

export default function ReportsPage() {
  const { toast } = useToast();
  const [reports, setReports] = useState<Report[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [stats, setStats] = useState<StatsResponse | null>(null);
  const [authors, setAuthors] = useState<Record<string, string>>({});
  const [modalities, setModalities] = useState<CatalogItem[]>([]);
  const [bodyPartsList, setBodyPartsList] = useState<CatalogItem[]>([]);

  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [modality, setModality] = useState('');
  const [bodyPart, setBodyPart] = useState('');
  const [status, setStatus] = useState('');
  const [sortBy, setSortBy] = useState<SortBy>('updatedAt');
  const [sortDir, setSortDir] = useState<SortDir>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [previewId, setPreviewId] = useState<string | null>(null);
  const [previewReport, setPreviewReport] = useState<Report | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [menuOpenId, setMenuOpenId] = useState<string | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => clearTimeout(t);
  }, [query]);

  const filterParams = useMemo(() => ({
    modality: modality || undefined,
    bodyPart: bodyPart || undefined,
    status: status === '' ? undefined : Number(status),
    q: debouncedQuery || undefined,
    updatedFrom: from ? new Date(from).toISOString() : undefined,
    updatedTo: to ? new Date(`${to}T23:59:59`).toISOString() : undefined,
    sortBy,
    sortDir,
  }), [modality, bodyPart, status, debouncedQuery, from, to, sortBy, sortDir]);

  const refreshStats = useCallback(() => {
    api.reports.stats().then(setStats).catch(() => undefined);
  }, []);

  const fetchPage = useCallback(() => {
    setLoading(true);
    setError(null);
    api.reports.listPaged({ ...filterParams, skip: (page - 1) * pageSize, take: pageSize })
      .then(({ items, total: t }) => { setReports(items); setTotal(t); })
      .catch((e) => setError((e as Error)?.message || 'Failed to load'))
      .finally(() => setLoading(false));
  }, [filterParams, page, pageSize]);

  useEffect(() => { void fetchPage(); }, [fetchPage]);
  useEffect(() => { setPage(1); setSelected(new Set()); }, [filterParams, pageSize]);
  useEffect(() => { refreshStats(); }, [refreshStats]);
  useEffect(() => { api.reports.authors().then(setAuthors).catch(() => undefined); }, []);
  useEffect(() => {
    api.modalities.list().then(setModalities).catch(() => undefined);
    api.bodyParts.list().then(setBodyPartsList).catch(() => undefined);
  }, []);

  useEffect(() => {
    if (!menuOpenId) return;
    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpenId(null);
    };
    document.addEventListener('click', onDocClick);
    return () => document.removeEventListener('click', onDocClick);
  }, [menuOpenId]);

  useEffect(() => {
    if (!previewId) { setPreviewReport(null); return; }
    setPreviewLoading(true);
    api.reports.get(previewId)
      .then(setPreviewReport)
      .catch((e) => toast({ tone: 'danger', title: 'Could not load preview', message: (e as Error).message }))
      .finally(() => setPreviewLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewId]);

  const filterActive = from !== '' || to !== '' || modality !== '' || bodyPart !== '' || status !== '' || debouncedQuery !== '';
  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  function radiologistName(r: Report): string {
    const id = r.createdByUserId;
    return (id && authors[id]) || '—';
  }

  async function handleDelete(r: Report) {
    const accession = r.study.accessionNumber || 'this report';
    if (!window.confirm(
      `Permanently delete report ${accession}?\n\nThis cannot be undone — the report and all of its data are removed for good. An audit-log entry is kept recording the deletion.`,
    )) return;
    setDeletingId(r.id);
    try {
      await api.reports.delete(r.id);
      toast({ tone: 'success', title: 'Report deleted', message: `${accession} was permanently deleted.` });
      void fetchPage();
      refreshStats();
    } catch (e) {
      toast({ tone: 'danger', title: 'Could not delete report', message: (e as { message?: string })?.message || 'Please try again.' });
    } finally {
      setDeletingId(null);
    }
  }

  async function handleBulkDelete() {
    const ids = Array.from(selected);
    if (ids.length === 0) return;
    if (!window.confirm(
      `Permanently delete ${ids.length} report${ids.length === 1 ? '' : 's'}?\n\nThis cannot be undone.`,
    )) return;
    setBulkBusy(true);
    let ok = 0;
    for (const id of ids) {
      try { await api.reports.delete(id); ok += 1; } catch { /* surfaced in the summary toast below */ }
    }
    setBulkBusy(false);
    setSelected(new Set());
    toast({
      tone: ok === ids.length ? 'success' : 'danger',
      title: 'Bulk delete',
      message: `${ok} of ${ids.length} report${ids.length === 1 ? '' : 's'} deleted.`,
    });
    void fetchPage();
    refreshStats();
  }

  async function exportCsv() {
    setExporting(true);
    try {
      const rows: Report[] = [];
      let skip = 0;
      const take = 500;
      for (;;) {
        const res = await api.reports.listPaged({ ...filterParams, skip, take });
        rows.push(...res.items);
        if (res.items.length === 0 || rows.length >= res.total || skip > EXPORT_SAFETY_CAP) break;
        skip += take;
      }
      const header = ['Accession', 'Modality', 'Body Part', 'Status', 'Updated', 'Radiologist'];
      const body = rows.map((r) => [
        r.study.accessionNumber,
        r.study.modality,
        r.study.bodyPart,
        statusLabel(r.status),
        new Date(r.updatedAt).toLocaleString(),
        radiologistName(r),
      ]);
      const csv = [header, ...body].map((row) => row.map(csvEscape).join(',')).join('\r\n');
      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `radiopad-reports-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      toast({ tone: 'success', title: 'Export ready', message: `${rows.length} report${rows.length === 1 ? '' : 's'} exported.` });
    } catch (e) {
      toast({ tone: 'danger', title: 'Export failed', message: (e as Error).message });
    } finally {
      setExporting(false);
    }
  }

  function toggleSelectAll() {
    setSelected((prev) => {
      const allOnPage = reports.every((r) => prev.has(r.id));
      if (allOnPage) return new Set();
      return new Set(reports.map((r) => r.id));
    });
  }
  function toggleSelect(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  const allSelectedOnPage = reports.length > 0 && reports.every((r) => selected.has(r.id));
  const someSelectedOnPage = reports.some((r) => selected.has(r.id)) && !allSelectedOnPage;

  return (
    <Container>
      <div className="rp-reports-hero">
        <div>
          <h1 className="rp-page-title">Reports</h1>
          <p className="rp-page-sub">All radiology reports in your workspace.</p>
        </div>
        <button type="button" className="primary" onClick={exportCsv} disabled={exporting}>
          <Download size={15} strokeWidth={2} aria-hidden /> {exporting ? 'Exporting…' : 'Export'}
        </button>
      </div>

      {stats && (
        <div className="rp-reports-stats">
          <div className="rp-reports-stat">
            <span className="rp-reports-stat-icon total" aria-hidden><FileText size={18} strokeWidth={1.8} /></span>
            <div className="rp-reports-stat-body">
              <div className="rp-reports-stat-value">{stats.total.toLocaleString()}</div>
              <div className="rp-reports-stat-label">Total Reports</div>
              <div className="rp-reports-stat-sub">All time</div>
            </div>
            <Sparkline data={stats.trend.total} className="rp-reports-stat-spark total" />
          </div>
          <div className="rp-reports-stat">
            <span className="rp-reports-stat-icon validated" aria-hidden><CheckCircle2 size={18} strokeWidth={1.8} /></span>
            <div className="rp-reports-stat-body">
              <div className="rp-reports-stat-value">{stats.validated.toLocaleString()}</div>
              <div className="rp-reports-stat-label">Validated</div>
              <div className="rp-reports-stat-sub">{stats.total > 0 ? `${((stats.validated / stats.total) * 100).toFixed(1)}% of total` : '—'}</div>
            </div>
            <Sparkline data={stats.trend.validated} className="rp-reports-stat-spark validated" />
          </div>
          <div className="rp-reports-stat">
            <span className="rp-reports-stat-icon acknowledged" aria-hidden><CheckCircle2 size={18} strokeWidth={1.8} /></span>
            <div className="rp-reports-stat-body">
              <div className="rp-reports-stat-value">{stats.acknowledged.toLocaleString()}</div>
              <div className="rp-reports-stat-label">Acknowledged</div>
              <div className="rp-reports-stat-sub">{stats.total > 0 ? `${((stats.acknowledged / stats.total) * 100).toFixed(1)}% of total` : '—'}</div>
            </div>
            <Sparkline data={stats.trend.acknowledged} className="rp-reports-stat-spark acknowledged" />
          </div>
          <div className="rp-reports-stat">
            <span className="rp-reports-stat-icon exported" aria-hidden><Upload size={18} strokeWidth={1.8} /></span>
            <div className="rp-reports-stat-body">
              <div className="rp-reports-stat-value">{stats.exported.toLocaleString()}</div>
              <div className="rp-reports-stat-label">Exported</div>
              <div className="rp-reports-stat-sub">{stats.total > 0 ? `${((stats.exported / stats.total) * 100).toFixed(1)}% of total` : '—'}</div>
            </div>
            <Sparkline data={stats.trend.exported} className="rp-reports-stat-spark exported" />
          </div>
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-reports-toolbar">
          <div className="rp-search" style={{ position: 'relative' }}>
            <Search size={15} strokeWidth={2} aria-hidden style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-faint)' }} />
            <input
              type="search"
              className="rp-input"
              style={{ paddingLeft: 30 }}
              placeholder="Search reports…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              aria-label="Search reports"
            />
          </div>
          <label className="rp-reports-field">
            <span>Updated From</span>
            <input type="date" className="rp-input" value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="rp-reports-field">
            <span>Updated To</span>
            <input type="date" className="rp-input" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
          </label>
          <label className="rp-reports-field">
            <span>Modality</span>
            <select className="rp-input" value={modality} onChange={(e) => setModality(e.target.value)}>
              <option value="">All</option>
              {modalities.map((m) => <option key={m.id} value={m.code}>{m.name || m.code}</option>)}
            </select>
          </label>
          <label className="rp-reports-field">
            <span>Body Part</span>
            <select className="rp-input" value={bodyPart} onChange={(e) => setBodyPart(e.target.value)}>
              <option value="">All</option>
              {bodyPartsList.map((b) => <option key={b.id} value={b.code}>{b.name || b.code}</option>)}
            </select>
          </label>
          <label className="rp-reports-field">
            <span>Status</span>
            <select className="rp-input" value={status} onChange={(e) => setStatus(e.target.value)}>
              {STATUS_OPTIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </label>
          <label className="rp-reports-field">
            <span>Sort By</span>
            <div className="rp-reports-sort">
              <select className="rp-input" value={sortBy} onChange={(e) => setSortBy(e.target.value as SortBy)}>
                {SORT_FIELDS.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
              </select>
              <button
                type="button"
                className="icon-btn ghost"
                aria-label={sortDir === 'desc' ? 'Sort descending — click for ascending' : 'Sort ascending — click for descending'}
                onClick={() => setSortDir((d) => (d === 'desc' ? 'asc' : 'desc'))}
              >
                <ArrowUpDown size={14} strokeWidth={2} aria-hidden />
              </button>
            </div>
          </label>
          {filterActive && (
            <button type="button" className="ghost" onClick={() => { setQuery(''); setFrom(''); setTo(''); setModality(''); setBodyPart(''); setStatus(''); }}>
              Clear filters
            </button>
          )}
        </div>

        {selected.size > 0 && (
          <div className="rp-reports-bulkbar">
            <span>{selected.size} selected</span>
            <button type="button" className="ghost rp-reports-delete" onClick={handleBulkDelete} disabled={bulkBusy}>
              <Trash2 size={14} strokeWidth={2} aria-hidden /> {bulkBusy ? 'Deleting…' : 'Delete selected'}
            </button>
            <button type="button" className="subtle" onClick={() => setSelected(new Set())}>Clear</button>
          </div>
        )}

        {loading ? (
          <TableSkeleton rows={6} cols={7} />
        ) : error ? (
          <ErrorState title="Couldn't load reports" message={error} onRetry={() => { void fetchPage(); }} />
        ) : total === 0 ? (
          <EmptyState
            title={filterActive ? 'No reports match your filters' : 'No reports yet'}
            description={filterActive ? 'Adjust or clear the filters to see more reports.' : 'Reports created in your workspace will appear here.'}
          />
        ) : (
          <>
            <table className="rp-table rp-reports-table">
              <thead>
                <tr>
                  <th style={{ width: 32 }}>
                    <input
                      type="checkbox"
                      aria-label="Select all reports on this page"
                      checked={allSelectedOnPage}
                      ref={(el) => { if (el) el.indeterminate = someSelectedOnPage; }}
                      onChange={toggleSelectAll}
                    />
                  </th>
                  <th>Accession</th>
                  <th>Modality</th>
                  <th>Body Part</th>
                  <th>Status</th>
                  <th>Updated</th>
                  <th>Radiologist</th>
                  <th aria-label="Actions" />
                </tr>
              </thead>
              <tbody className="rp-stagger">
                {reports.map((r) => (
                  <tr key={r.id}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`Select report ${r.study.accessionNumber}`}
                        checked={selected.has(r.id)}
                        onChange={() => toggleSelect(r.id)}
                      />
                    </td>
                    <td>{r.study.accessionNumber}</td>
                    <td><span className="badge">{r.study.modality}</span></td>
                    <td>{r.study.bodyPart}</td>
                    <td><StatusBadge tone={reportStatusTone(r.status)}>{statusLabel(r.status)}</StatusBadge></td>
                    <td className="muted rp-reports-updated">
                      {new Date(r.updatedAt).toLocaleString()}
                    </td>
                    <td className="muted">{radiologistName(r)}</td>
                    <td className="rp-reports-actions">
                      <Link href={reportHref(r.id)}>Open →</Link>
                      <button type="button" className="ghost" onClick={() => setPreviewId(r.id)}>
                        <Eye size={14} strokeWidth={2} aria-hidden /> Preview
                      </button>
                      <button
                        type="button"
                        className="ghost rp-reports-delete"
                        onClick={() => { void handleDelete(r); }}
                        disabled={deletingId === r.id}
                        aria-label={`Delete report ${r.study.accessionNumber || ''}`.trim()}
                        title="Delete report"
                      >
                        <Trash2 size={15} aria-hidden />
                      </button>
                      <div className="rp-reports-menu" ref={menuOpenId === r.id ? menuRef : null}>
                        <button
                          type="button"
                          className="icon-btn ghost"
                          aria-label="More actions"
                          onClick={() => setMenuOpenId((cur) => (cur === r.id ? null : r.id))}
                        >
                          <MoreVertical size={14} strokeWidth={2} aria-hidden />
                        </button>
                        {menuOpenId === r.id && (
                          <div className="rp-reports-menu-list">
                            <Link href={reportHref(r.id)}>Open report</Link>
                            <button type="button" onClick={() => { setMenuOpenId(null); setPreviewId(r.id); }}>Preview</button>
                          </div>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="rp-reports-pagination">
              <label className="rp-reports-pagesize">
                <span>Rows per page:</span>
                <select className="rp-input" value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}>
                  {PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
              <span className="rp-page-sub">
                {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} of {total.toLocaleString()}
              </span>
              <div className="rp-reports-pages">
                <button type="button" className="rp-rb-page-btn" disabled={page <= 1} onClick={() => setPage(1)} aria-label="First page"><ChevronsLeft size={14} /></button>
                <button type="button" className="rp-rb-page-btn" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label="Previous page"><ChevronLeft size={14} /></button>
                {Array.from({ length: totalPages }, (_, i) => i + 1)
                  .filter((n) => n === 1 || n === totalPages || Math.abs(n - page) <= 2)
                  .reduce<number[]>((acc, n) => {
                    if (acc.length > 0 && n - acc[acc.length - 1] > 1) acc.push(-1);
                    acc.push(n);
                    return acc;
                  }, [])
                  .map((n, i) => n === -1 ? (
                    <span key={`gap-${i}`} className="rp-page-sub" style={{ padding: '0 4px' }}>…</span>
                  ) : (
                    <button key={n} type="button" className={`rp-rb-page-btn ${n === page ? 'active' : ''}`} onClick={() => setPage(n)}>{n}</button>
                  ))}
                <button type="button" className="rp-rb-page-btn" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label="Next page"><ChevronRight size={14} /></button>
                <button type="button" className="rp-rb-page-btn" disabled={page >= totalPages} onClick={() => setPage(totalPages)} aria-label="Last page"><ChevronsRight size={14} /></button>
              </div>
            </div>
          </>
        )}
      </div>

      {previewId && (
        <div className="rp-reports-preview-backdrop" onClick={() => setPreviewId(null)}>
          <div className="rp-reports-preview rp-panel" onClick={(e) => e.stopPropagation()}>
            <div className="rp-reports-preview-head">
              <div className="rp-panel-title">Report preview</div>
              <button type="button" className="icon-btn ghost" aria-label="Close preview" onClick={() => setPreviewId(null)}>
                <X size={16} strokeWidth={2} aria-hidden />
              </button>
            </div>
            {previewLoading || !previewReport ? (
              <TableSkeleton rows={3} cols={1} />
            ) : (
              <>
                <div className="rp-reports-preview-meta">
                  <span><strong>{previewReport.study.accessionNumber}</strong></span>
                  <span className="badge">{previewReport.study.modality}</span>
                  <span>{previewReport.study.bodyPart}</span>
                  <StatusBadge tone={reportStatusTone(previewReport.status)}>{statusLabel(previewReport.status)}</StatusBadge>
                </div>
                {previewReport.indication && (
                  <div className="rp-reports-preview-section"><h4>Indication</h4><p>{previewReport.indication}</p></div>
                )}
                {previewReport.findings && (
                  <div className="rp-reports-preview-section"><h4>Findings</h4><p>{previewReport.findings}</p></div>
                )}
                {previewReport.impression && (
                  <div className="rp-reports-preview-section"><h4>Impression</h4><p>{previewReport.impression}</p></div>
                )}
                <div className="rp-card-actions">
                  <Link href={reportHref(previewReport.id)} className="primary">Open full report</Link>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </Container>
  );
}
