'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  BookOpenText,
  Check,
  Clock,
  ExternalLink,
  Info,
  RefreshCw,
  Search,
  Tag,
  Tags,
} from 'lucide-react';
import { api, type RadLexHit, type RadsEntry } from '@/lib/api';
import Container from '@/components/shell/Container';

const RADS_SYSTEMS = [
  { id: 'BI-RADS', label: 'BI-RADS (breast)' },
  { id: 'LI-RADS', label: 'LI-RADS (liver)' },
  { id: 'TI-RADS', label: 'TI-RADS (thyroid)' },
  { id: 'PI-RADS', label: 'PI-RADS (prostate)' },
  { id: 'Lung-RADS', label: 'Lung-RADS' },
  { id: 'O-RADS', label: 'O-RADS (ovarian)' },
];

// The bundled RadLex subset's real taxonomy (RadLexService.cs / radlex_subset.yaml) —
// a grammatical bucket per concept, not a body-region/system grouping.
const CATEGORIES = ['anatomy', 'finding', 'modality', 'technique', 'qualifier'] as const;
const CATEGORY_LABEL: Record<string, string> = {
  anatomy: 'Anatomy',
  finding: 'Finding',
  modality: 'Modality',
  technique: 'Technique',
  qualifier: 'Qualifier',
};

type Tab = 'radlex' | 'rads';
type SortMode = 'relevance' | 'recent' | 'alpha';
const PAGE_SIZES = [5, 10, 20, 50];
const RECENT_KEY = 'radiopad.terminology.recentCodes';
const RADLEX_DOCS_URL = 'https://www.rsna.org/practice-tools/data-tools-and-standards/radlex-radiology-lexicon';

function loadRecent(): string[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = window.localStorage.getItem(RECENT_KEY);
    return raw ? (JSON.parse(raw) as string[]) : [];
  } catch {
    return [];
  }
}

function pushRecent(code: string): string[] {
  if (typeof window === 'undefined') return [];
  const next = [code, ...loadRecent().filter((c) => c !== code)].slice(0, 20);
  window.localStorage.setItem(RECENT_KEY, JSON.stringify(next));
  return next;
}

export default function TerminologyPage() {
  const [tab, setTab] = useState<Tab>('radlex');
  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<RadLexHit[]>([]);
  const [total, setTotal] = useState(0);
  const [radsSystem, setRadsSystem] = useState(RADS_SYSTEMS[0].id);
  const [radsEntries, setRadsEntries] = useState<RadsEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [category, setCategory] = useState<string>('all');
  const [sort, setSort] = useState<SortMode>('relevance');
  const [pageSize, setPageSize] = useState(5);
  const [showAll, setShowAll] = useState(false);
  const [recent, setRecent] = useState<string[]>([]);
  const [copiedCode, setCopiedCode] = useState<string | null>(null);

  useEffect(() => {
    setRecent(loadRecent());
  }, []);

  function runRadLexSearch() {
    const q = query.trim();
    if (q.length < 2) {
      setHits([]);
      setTotal(0);
      setError(null);
      return;
    }
    setBusy(true);
    setError(null);
    // A generous batch — the client applies category/sort/page-size on top of it,
    // so those feel instant. The bundled subset is ~180 concepts, well under this cap.
    api.terminology
      .radlexSearch(q, 200)
      .then((res) => {
        setHits(res.hits);
        setTotal(res.total);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setBusy(false));
  }

  // Debounced RadLex search.
  useEffect(() => {
    if (tab !== 'radlex') return;
    const handle = setTimeout(runRadLexSearch, 200);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, tab]);

  // Load RADS entries on system change.
  useEffect(() => {
    if (tab !== 'rads') return;
    setBusy(true);
    setError(null);
    api.terminology
      .rads(radsSystem)
      .then(setRadsEntries)
      .catch((e: Error) => setError(e.message))
      .finally(() => setBusy(false));
  }, [radsSystem, tab]);

  useEffect(() => {
    setShowAll(false);
  }, [query, category, sort, pageSize]);

  const filtered = useMemo(
    () => (category === 'all' ? hits : hits.filter((h) => h.category === category)),
    [hits, category],
  );

  const sorted = useMemo(() => {
    if (sort === 'alpha') {
      return [...filtered].sort((a, b) => a.preferredName.localeCompare(b.preferredName));
    }
    if (sort === 'recent') {
      const rank = new Map(recent.map((c, i) => [c, i]));
      return [...filtered].sort((a, b) => {
        const ra = rank.has(a.code) ? rank.get(a.code)! : Infinity;
        const rb = rank.has(b.code) ? rank.get(b.code)! : Infinity;
        return ra - rb;
      });
    }
    return filtered; // 'relevance' — the backend's own ordering.
  }, [filtered, sort, recent]);

  const visible = showAll ? sorted : sorted.slice(0, pageSize);
  const truncated = total > hits.length; // backend matched more than this batch fetched.

  function copyCode(code: string) {
    setRecent(pushRecent(code));
    navigator.clipboard?.writeText(code).catch(() => undefined);
    setCopiedCode(code);
    setTimeout(() => setCopiedCode((c) => (c === code ? null : c)), 1500);
  }

  return (
    <Container>
      <div className="rp-term-hero">
        <span className="rp-term-hero-icon" aria-hidden>
          <BookOpenText size={26} strokeWidth={1.8} />
        </span>
        <div className="rp-term-hero-text">
          <h1 className="rp-page-title">Terminology</h1>
          <p className="rp-page-sub">
            Look up official terminology for findings (RadLex) and structured reporting
            categories (RADS). For reference only — not clinical advice.
          </p>
        </div>
      </div>

      <div className="rp-term-tabs" role="tablist" aria-label="Terminology source">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'radlex'}
          className={`rp-term-tab ${tab === 'radlex' ? 'active' : ''}`}
          onClick={() => setTab('radlex')}
        >
          RadLex
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'rads'}
          className={`rp-term-tab ${tab === 'rads' ? 'active' : ''}`}
          onClick={() => setTab('rads')}
        >
          RADS systems
        </button>
      </div>

      {tab === 'radlex' && (
        <>
          <div className="rp-term-toolbar">
            <div className="rp-term-search">
              <Search size={16} strokeWidth={2} aria-hidden />
              <input
                aria-label="Search term, synonym, or code"
                placeholder="Search term, synonym, or code"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                autoFocus
              />
              <kbd>Ctrl F</kbd>
            </div>
            <label className="rp-term-select">
              <Tags size={14} strokeWidth={2} aria-hidden />
              <select value={category} onChange={(e) => setCategory(e.target.value)}>
                <option value="all">All categories</option>
                {CATEGORIES.map((c) => (
                  <option key={c} value={c}>{CATEGORY_LABEL[c]}</option>
                ))}
              </select>
            </label>
            <div className="rp-term-chips">
              {CATEGORIES.map((c) => (
                <button
                  key={c}
                  type="button"
                  className={`rp-term-chip ${category === c ? 'active' : ''}`}
                  onClick={() => setCategory((cur) => (cur === c ? 'all' : c))}
                  aria-pressed={category === c}
                >
                  {CATEGORY_LABEL[c]}
                </button>
              ))}
            </div>
            <label className="rp-term-select">
              <Clock size={14} strokeWidth={2} aria-hidden />
              <select value={sort} onChange={(e) => setSort(e.target.value as SortMode)}>
                <option value="relevance">Relevance</option>
                <option value="recent">Recent</option>
                <option value="alpha">A–Z</option>
              </select>
            </label>
          </div>

          {error && (
            <div className="rp-term-error">
              <AlertTriangle size={20} strokeWidth={2} aria-hidden />
              <div className="rp-term-error-text">
                <strong>Lookup service unavailable</strong>
                <span>{error}</span>
              </div>
              <div className="rp-term-error-actions">
                <button type="button" className="ghost" onClick={runRadLexSearch}>
                  <RefreshCw size={14} strokeWidth={2} aria-hidden /> Retry
                </button>
                <a className="ghost" href={RADLEX_DOCS_URL} target="_blank" rel="noreferrer">
                  <ExternalLink size={14} strokeWidth={2} aria-hidden /> View docs
                </a>
              </div>
            </div>
          )}

          <div className="rp-page-grid">
            <div className="rp-page-main">
              <div className="rp-panel">
                <div className="rp-term-results-head">
                  <div>
                    <div className="rp-panel-title">RadLex search</div>
                    <p className="rp-page-sub">Results for reference only.</p>
                  </div>
                  {sorted.length > 0 && (
                    <div className="rp-term-results-meta">
                      <span className="rp-page-sub">
                        Showing {visible.length} of {sorted.length} results
                        {truncated ? ` (top ${hits.length} of ${total} matches)` : ''}
                      </span>
                      <label className="rp-term-select sm">
                        <select value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}>
                          {PAGE_SIZES.map((n) => (
                            <option key={n} value={n}>Show {n}</option>
                          ))}
                        </select>
                      </label>
                    </div>
                  )}
                </div>

                {query.trim().length < 2 ? (
                  <p className="rp-page-sub rp-term-empty">Type at least 2 characters to search.</p>
                ) : busy && hits.length === 0 ? (
                  <p className="rp-page-sub rp-term-empty">Searching…</p>
                ) : sorted.length === 0 ? (
                  <p className="rp-page-sub rp-term-empty">No matches.</p>
                ) : (
                  <>
                    <table className="rp-table rp-term-table">
                      <thead>
                        <tr>
                          <th>Code</th>
                          <th>Preferred name</th>
                          <th>Synonyms</th>
                          <th>Category</th>
                        </tr>
                      </thead>
                      <tbody>
                        {visible.map((h) => (
                          <tr key={h.code}>
                            <td>
                              <button
                                type="button"
                                className="rp-term-code"
                                onClick={() => copyCode(h.code)}
                                title="Copy code"
                              >
                                {copiedCode === h.code ? <Check size={13} strokeWidth={2.4} aria-hidden /> : null}
                                {h.code}
                              </button>
                            </td>
                            <td>{h.preferredName}</td>
                            <td className="rp-page-sub">{(h.synonyms ?? []).join('; ') || '—'}</td>
                            <td>
                              {h.category ? (
                                <span className="badge">
                                  <Tag size={11} strokeWidth={2} aria-hidden /> {CATEGORY_LABEL[h.category] ?? h.category}
                                </span>
                              ) : '—'}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {!showAll && sorted.length > visible.length && (
                      <div className="rp-term-view-all">
                        <button type="button" className="subtle" onClick={() => setShowAll(true)}>
                          View all results
                        </button>
                      </div>
                    )}
                  </>
                )}
              </div>
            </div>

            <aside className="rp-page-aside">
              <div className="rp-help">
                <div className="rp-help-title"><Info size={14} strokeWidth={2} aria-hidden /> Reference tips</div>
                <ul className="rp-term-tips">
                  <li>
                    <Search size={15} strokeWidth={2} aria-hidden />
                    <div>
                      <strong>Search by any field</strong>
                      <p>Enter an official term, common synonym, or RadLex code (e.g., RID21542).</p>
                    </div>
                  </li>
                  <li>
                    <Tags size={15} strokeWidth={2} aria-hidden />
                    <div>
                      <strong>Browse categories</strong>
                      <p>Use filters to narrow results by category.</p>
                    </div>
                  </li>
                  <li>
                    <RefreshCw size={15} strokeWidth={2} aria-hidden />
                    <div>
                      <strong>Explore synonyms</strong>
                      <p>Synonyms help you find the terminology you use most.</p>
                    </div>
                  </li>
                  <li>
                    <BookOpenText size={15} strokeWidth={2} aria-hidden />
                    <div>
                      <strong>RADS systems</strong>
                      <p>Switch to the RADS systems tab to explore structured reporting categories.</p>
                    </div>
                  </li>
                </ul>
              </div>
              <div className="rp-term-disclaimer">
                <Info size={14} strokeWidth={2} aria-hidden />
                This is a reference tool only and does not constitute clinical advice.
              </div>
            </aside>
          </div>
        </>
      )}

      {tab === 'rads' && (
        <div className="rp-panel rp-anim-fade-in" key="rads">
          <div className="rp-panel-title">RADS categories</div>
          <div className="section-block">
            <label htmlFor="rads-sys">System</label>
            <select
              id="rads-sys"
              className="rp-input"
              value={radsSystem}
              onChange={(e) => setRadsSystem(e.target.value)}
            >
              {RADS_SYSTEMS.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.label}
                </option>
              ))}
            </select>
          </div>

          {error && (
            <div className="rp-term-error">
              <AlertTriangle size={20} strokeWidth={2} aria-hidden />
              <div className="rp-term-error-text">
                <strong>Lookup service unavailable</strong>
                <span>{error}</span>
              </div>
            </div>
          )}

          <ul className="rp-list" aria-live="polite" aria-busy={busy}>
            <li className="rp-row between rp-divider-row">
              <span className="rp-stat-label rp-cell f1">Code</span>
              <span className="rp-stat-label rp-cell f2">Label</span>
              <span className="rp-stat-label rp-cell f2">Description</span>
            </li>
            {busy && <li className="rp-page-sub rp-divider-row">Loading…</li>}
            {!busy && radsEntries.length === 0 && (
              <li className="rp-page-sub rp-divider-row">No entries published for this system.</li>
            )}
            {radsEntries.map((e) => (
              <li key={`${e.system}:${e.code}`} className="rp-row between rp-divider-row">
                <span className="rp-cell f1">
                  <code>{e.code}</code>
                </span>
                <span className="rp-cell f2">{e.label}</span>
                <span className="rp-cell f2 rp-page-sub">{e.description ?? '—'}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Container>
  );
}
