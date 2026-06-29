'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type Rulebook } from '@/lib/api';
import { rulebookHref, rulebookEditorHref } from '@/lib/routes';
import { statusLabel, statusBadge, relativeTime } from '@/lib/rulebookStatus';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

const STATUS_FILTERS = ['All', 'Draft', 'In review', 'Approved', 'Deprecated'] as const;
type StatusFilter = (typeof STATUS_FILTERS)[number];

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

  const refresh = useCallback(() => {
    setLoading(true);
    setError(null);
    api.rulebooks.list()
      .then((rows) => setItems(rows))
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const openDetail = useCallback((id: string) => router.push(rulebookHref(id)), [router]);
  const openEditor = useCallback((id?: string) => router.push(rulebookEditorHref(id)), [router]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return items.filter((rb) => {
      const matchesQuery = !q
        || rb.name.toLowerCase().includes(q)
        || rb.rulebookId.toLowerCase().includes(q);
      const matchesStatus = status === 'All' || statusLabel(rb.status) === status;
      return matchesQuery && matchesStatus;
    });
  }, [items, query, status]);

  const newRulebookBtn = (
    <button type="button" className="primary" onClick={() => openEditor()}>+ New rulebook</button>
  );

  return (
    <Container>
      <PageHeader
        title="Rulebooks"
        description="Your clinic's approved playbooks for AI drafting and quality checks — versioned, testable, and reviewed by your team before going live."
        primaryAction={newRulebookBtn}
      />

      {error && <ErrorState title="Couldn't load rulebooks" message={error} onRetry={refresh} />}

      {!error && (
        <>
          <div className="rp-filter-bar">
            <input
              type="search"
              className="rp-input rp-search"
              placeholder="Search by name or id…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              aria-label="Search rulebooks"
            />
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
          ) : filtered.length === 0 ? (
            <EmptyState
              title="No matches"
              description={`No rulebooks match "${query.trim()}"${status !== 'All' ? ` in ${status}` : ''}.`}
            />
          ) : (
            <div className="rp-card-grid rp-stagger" aria-live="polite">
              {filtered.map((rb) => {
                const chips = [...splitCsv(rb.appliesToModalities), ...splitCsv(rb.appliesToBodyParts)];
                const updated = relativeTime(rb.updatedAt);
                return (
                  <div
                    key={rb.id}
                    className="rp-card"
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
                      <span className={`badge ${statusBadge(rb.status)}`}>{statusLabel(rb.status)}</span>
                    </div>

                    {chips.length > 0 && (
                      <div className="rp-chip-row">
                        {chips.map((c, i) => <span key={`${c}-${i}`} className="rp-chip">{c}</span>)}
                      </div>
                    )}

                    <div className="rp-card-meta">
                      v{rb.version}
                      {rb.owner ? ` · ${rb.owner}` : ''}
                      {updated ? ` · updated ${updated}` : ''}
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
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </>
      )}
    </Container>
  );
}
