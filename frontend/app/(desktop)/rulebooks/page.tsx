'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Archive,
  BookOpen,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  FileText,
  FlaskConical,
  Layers,
  MoreVertical,
  Search,
  Workflow,
} from 'lucide-react';
import { api, type Rulebook } from '@/lib/api';
import { rulebookHref, rulebookEditorHref } from '@/lib/routes';
import { statusLabel, statusBadge, relativeTime } from '@/lib/rulebookStatus';
import Container from '@/components/shell/Container';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

const STATUS_FILTERS = ['All', 'Draft', 'In review', 'Approved', 'Deprecated'] as const;
type StatusFilter = (typeof STATUS_FILTERS)[number];
type SortMode = 'updated' | 'name' | 'status';
const PAGE_SIZES = [12, 24, 48];

function splitCsv(csv: string): string[] {
  return (csv || '').split(',').map((s) => s.trim()).filter(Boolean);
}

export default function RulebooksPage() {
  const router = useRouter();
  const [items, setItems] = useState<Rulebook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState<StatusFilter>('All');
  const [modality, setModality] = useState('all');
  const [bodyPart, setBodyPart] = useState('all');
  const [sort, setSort] = useState<SortMode>('updated');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(12);
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

  const refresh = useCallback(() => {
    setLoading(true);
    setError(null);
    api.rulebooks.list()
      .then((rows) => setItems(rows))
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  useEffect(() => {
    if (!openMenuId) return;
    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setOpenMenuId(null);
    };
    document.addEventListener('click', onDocClick);
    return () => document.removeEventListener('click', onDocClick);
  }, [openMenuId]);

  const openDetail = useCallback((id: string) => router.push(rulebookHref(id)), [router]);
  const openEditor = useCallback((id?: string) => router.push(rulebookEditorHref(id)), [router]);

  const modalities = useMemo(
    () => Array.from(new Set(items.flatMap((rb) => splitCsv(rb.appliesToModalities)))).sort(),
    [items],
  );
  const bodyParts = useMemo(
    () => Array.from(new Set(items.flatMap((rb) => splitCsv(rb.appliesToBodyParts)))).sort(),
    [items],
  );

  const statusCounts = useMemo(() => {
    const counts: Record<string, number> = { Draft: 0, 'In review': 0, Approved: 0, Deprecated: 0 };
    for (const rb of items) {
      const label = statusLabel(rb.status);
      if (label in counts) counts[label] += 1;
    }
    return counts;
  }, [items]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return items.filter((rb) => {
      const matchesQuery = !q
        || rb.name.toLowerCase().includes(q)
        || rb.rulebookId.toLowerCase().includes(q);
      const matchesStatus = status === 'All' || statusLabel(rb.status) === status;
      const matchesModality = modality === 'all' || splitCsv(rb.appliesToModalities).includes(modality);
      const matchesBodyPart = bodyPart === 'all' || splitCsv(rb.appliesToBodyParts).includes(bodyPart);
      return matchesQuery && matchesStatus && matchesModality && matchesBodyPart;
    });
  }, [items, query, status, modality, bodyPart]);

  const sorted = useMemo(() => {
    const list = [...filtered];
    if (sort === 'name') return list.sort((a, b) => (a.name || a.rulebookId).localeCompare(b.name || b.rulebookId));
    if (sort === 'status') return list.sort((a, b) => statusLabel(a.status).localeCompare(statusLabel(b.status)));
    return list.sort((a, b) => new Date(b.updatedAt ?? 0).getTime() - new Date(a.updatedAt ?? 0).getTime());
  }, [filtered, sort]);

  useEffect(() => { setPage(1); }, [query, status, modality, bodyPart, sort, pageSize]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paged = sorted.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  async function doApprove(id: string) {
    setOpenMenuId(null);
    setActionError(null);
    try {
      await api.rulebooks.approve(id);
      refresh();
    } catch (e) {
      setActionError((e as { body?: { error?: string }; message?: string }).body?.error ?? (e as Error).message);
    }
  }
  async function doDeprecate(id: string) {
    setOpenMenuId(null);
    setActionError(null);
    try {
      await api.rulebooks.deprecate(id);
      refresh();
    } catch (e) {
      setActionError((e as { body?: { error?: string }; message?: string }).body?.error ?? (e as Error).message);
    }
  }

  const newRulebookBtn = (
    <button type="button" className="primary" onClick={() => openEditor()}>+ New rulebook</button>
  );

  return (
    <Container>
      <div className="rp-rb-hero">
        <div className="rp-rb-hero-main">
          <span className="rp-rb-hero-icon" aria-hidden>
            <BookOpen size={26} strokeWidth={1.8} />
          </span>
          <div className="rp-rb-hero-text">
            <h1 className="rp-page-title">Rulebooks</h1>
            <p className="rp-page-sub">
              Your clinic&apos;s approved playbooks for AI drafting and quality checks — versioned,
              testable, and reviewed before going live.
            </p>
          </div>
        </div>
        <div className="rp-rb-hero-actions">{newRulebookBtn}</div>
      </div>

      {error && <ErrorState title="Couldn't load rulebooks" message={error} onRetry={refresh} />}
      {actionError && <div className="banner warn">{actionError}</div>}

      {!error && (
        <>
          <div className="rp-rb-stats">
            <div className="rp-rb-stat">
              <span className="rp-rb-stat-icon total" aria-hidden><Workflow size={18} strokeWidth={1.8} /></span>
              <div>
                <div className="rp-rb-stat-value">{items.length}</div>
                <div className="rp-rb-stat-label">Total Rulebooks</div>
                <div className="rp-rb-stat-sub">Across all statuses</div>
              </div>
            </div>
            <div className="rp-rb-stat">
              <span className="rp-rb-stat-icon draft" aria-hidden><BookOpen size={18} strokeWidth={1.8} /></span>
              <div>
                <div className="rp-rb-stat-value">{statusCounts.Draft}</div>
                <div className="rp-rb-stat-label">Draft</div>
                <div className="rp-rb-stat-sub">In progress</div>
              </div>
            </div>
            <div className="rp-rb-stat">
              <span className="rp-rb-stat-icon review" aria-hidden><FileText size={18} strokeWidth={1.8} /></span>
              <div>
                <div className="rp-rb-stat-value">{statusCounts['In review']}</div>
                <div className="rp-rb-stat-label">In Review</div>
                <div className="rp-rb-stat-sub">Awaiting approval</div>
              </div>
            </div>
            <div className="rp-rb-stat">
              <span className="rp-rb-stat-icon approved" aria-hidden><CheckCircle2 size={18} strokeWidth={1.8} /></span>
              <div>
                <div className="rp-rb-stat-value">{statusCounts.Approved}</div>
                <div className="rp-rb-stat-label">Approved</div>
                <div className="rp-rb-stat-sub">Active rulebooks</div>
              </div>
            </div>
            <div className="rp-rb-stat">
              <span className="rp-rb-stat-icon deprecated" aria-hidden><Archive size={18} strokeWidth={1.8} /></span>
              <div>
                <div className="rp-rb-stat-value">{statusCounts.Deprecated}</div>
                <div className="rp-rb-stat-label">Deprecated</div>
                <div className="rp-rb-stat-sub">Retired rulebooks</div>
              </div>
            </div>
          </div>

          <div className="rp-rb-toolbar">
            <div className="rp-search" style={{ position: 'relative', flex: '1 1 240px' }}>
              <Search
                size={15}
                strokeWidth={2}
                aria-hidden
                style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-faint)' }}
              />
              <input
                type="search"
                className="rp-input"
                style={{ paddingLeft: 30 }}
                placeholder="Search rulebooks…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search rulebooks"
              />
            </div>
            <div className="rp-tabs" role="tablist" aria-label="Filter by status">
              {STATUS_FILTERS.map((s) => (
                <button
                  key={s}
                  type="button"
                  role="tab"
                  aria-selected={status === s}
                  className={`rp-tab ${status === s ? 'active' : ''}`}
                  onClick={() => setStatus(s)}
                >
                  {s}
                </button>
              ))}
            </div>
            <label className="rp-rb-select">
              <select value={modality} onChange={(e) => setModality(e.target.value)} aria-label="Filter by modality">
                <option value="all">All Modalities</option>
                {modalities.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </label>
            <label className="rp-rb-select">
              <select value={bodyPart} onChange={(e) => setBodyPart(e.target.value)} aria-label="Filter by body part">
                <option value="all">All Body Parts</option>
                {bodyParts.map((b) => <option key={b} value={b}>{b}</option>)}
              </select>
            </label>
            <label className="rp-rb-select">
              <select value={sort} onChange={(e) => setSort(e.target.value as SortMode)} aria-label="Sort rulebooks">
                <option value="updated">Sort: Recently updated</option>
                <option value="name">Sort: Name A–Z</option>
                <option value="status">Sort: Status</option>
              </select>
            </label>
          </div>

          {loading ? (
            <div className="rp-card-grid" aria-busy="true" aria-live="polite">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="rp-panel" style={{ margin: 0 }}>
                  <Skeleton variant="block" height={96} />
                </div>
              ))}
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              title="No rulebooks yet"
              description="Create your first rulebook to define how AI drafts and validates reports for a study type."
              action={newRulebookBtn}
            />
          ) : sorted.length === 0 ? (
            <EmptyState
              title="No matches"
              description={`No rulebooks match "${query.trim()}"${status !== 'All' ? ` in ${status}` : ''}.`}
            />
          ) : (
            <>
              <div className="rp-card-grid rp-rb-grid rp-stagger" aria-live="polite">
                {paged.map((rb) => {
                  const chips = [...splitCsv(rb.appliesToModalities), ...splitCsv(rb.appliesToBodyParts)];
                  const updated = relativeTime(rb.updatedAt);
                  const label = statusLabel(rb.status);
                  return (
                    <div
                      key={rb.id}
                      className="rp-card rp-rb-card"
                      role="link"
                      tabIndex={0}
                      onClick={() => openDetail(rb.id)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          openDetail(rb.id);
                        }
                      }}
                    >
                      <div className="rp-card-head">
                        <div style={{ minWidth: 0 }}>
                          <h2 className="rp-card-title">{rb.name || rb.rulebookId}</h2>
                          <code className="rp-card-id">{rb.rulebookId}</code>
                        </div>
                        <span className={`badge ${statusBadge(rb.status)}`}>{label}</span>
                      </div>

                      {chips.length > 0 && (
                        <div className="rp-chip-row">
                          {chips.map((c, i) => <span key={`${c}-${i}`} className="rp-chip">{c}</span>)}
                        </div>
                      )}

                      <div className="rp-card-meta">
                        v{rb.version}
                        {rb.owner ? ` · ${rb.owner}` : ''}
                        {updated ? ` · Updated ${updated}` : ''}
                      </div>

                      <div className="rp-rb-card-stats">
                        <span className="rp-rb-card-stat"><FlaskConical size={13} strokeWidth={2} aria-hidden /> {rb.testsCount ?? 0} tests</span>
                        <span className="rp-rb-card-stat"><Layers size={13} strokeWidth={2} aria-hidden /> {rb.templatesCount ?? 0} templates</span>
                      </div>

                      <div className="rp-card-actions">
                        <button
                          type="button"
                          className="ghost"
                          onClick={(e) => { e.stopPropagation(); openDetail(rb.id); }}
                        >
                          Open
                        </button>
                        <button
                          type="button"
                          className="primary-ghost"
                          onClick={(e) => { e.stopPropagation(); openEditor(rb.id); }}
                        >
                          Edit
                        </button>
                        <div className="rp-rb-card-menu" ref={openMenuId === rb.id ? menuRef : null}>
                          <button
                            type="button"
                            className="icon-btn ghost"
                            aria-label="More actions"
                            onClick={(e) => { e.stopPropagation(); setOpenMenuId((cur) => (cur === rb.id ? null : rb.id)); }}
                          >
                            <MoreVertical size={14} strokeWidth={2} aria-hidden />
                          </button>
                          {openMenuId === rb.id && (
                            <div className="rp-rb-card-menu-list" onClick={(e) => e.stopPropagation()}>
                              {label !== 'Approved' && (
                                <button type="button" onClick={() => doApprove(rb.id)}>Approve</button>
                              )}
                              {label !== 'Deprecated' && (
                                <button type="button" onClick={() => doDeprecate(rb.id)}>Deprecate</button>
                              )}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>

              <div className="rp-rb-pagination">
                <span className="rp-page-sub">
                  Showing {(currentPage - 1) * pageSize + 1} to {Math.min(currentPage * pageSize, sorted.length)} of {sorted.length} rulebooks
                </span>
                <div className="rp-rb-pagination-pages">
                  <button
                    type="button"
                    className="rp-rb-page-btn"
                    disabled={currentPage <= 1}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    aria-label="Previous page"
                  >
                    <ChevronLeft size={14} strokeWidth={2} />
                  </button>
                  {Array.from({ length: totalPages }, (_, i) => i + 1)
                    .filter((n) => n === 1 || n === totalPages || Math.abs(n - currentPage) <= 2)
                    .reduce<number[]>((acc, n) => {
                      if (acc.length > 0 && n - acc[acc.length - 1] > 1) acc.push(-1);
                      acc.push(n);
                      return acc;
                    }, [])
                    .map((n, i) => n === -1 ? (
                      <span key={`gap-${i}`} className="rp-page-sub" style={{ padding: '0 4px' }}>…</span>
                    ) : (
                      <button
                        key={n}
                        type="button"
                        className={`rp-rb-page-btn ${n === currentPage ? 'active' : ''}`}
                        onClick={() => setPage(n)}
                      >
                        {n}
                      </button>
                    ))}
                  <button
                    type="button"
                    className="rp-rb-page-btn"
                    disabled={currentPage >= totalPages}
                    onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                    aria-label="Next page"
                  >
                    <ChevronRight size={14} strokeWidth={2} />
                  </button>
                </div>
                <label className="rp-rb-select">
                  <select value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))} aria-label="Rulebooks per page">
                    {PAGE_SIZES.map((n) => <option key={n} value={n}>{n} / page</option>)}
                  </select>
                </label>
              </div>
            </>
          )}
        </>
      )}
    </Container>
  );
}
