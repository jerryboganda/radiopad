'use client';

import { useEffect, useState } from 'react';
import { api, type RadLexHit, type RadsEntry } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';

const RADS_SYSTEMS = [
  { id: 'BI-RADS', label: 'BI-RADS (breast)' },
  { id: 'LI-RADS', label: 'LI-RADS (liver)' },
  { id: 'TI-RADS', label: 'TI-RADS (thyroid)' },
  { id: 'PI-RADS', label: 'PI-RADS (prostate)' },
  { id: 'Lung-RADS', label: 'Lung-RADS' },
  { id: 'O-RADS', label: 'O-RADS (ovarian)' },
];

type Tab = 'radlex' | 'rads';

export default function TerminologyPage() {
  const [tab, setTab] = useState<Tab>('radlex');
  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<RadLexHit[]>([]);
  const [radsSystem, setRadsSystem] = useState(RADS_SYSTEMS[0].id);
  const [radsEntries, setRadsEntries] = useState<RadsEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Debounced RadLex search.
  useEffect(() => {
    if (tab !== 'radlex') return;
    const q = query.trim();
    if (q.length < 2) {
      setHits([]);
      return;
    }
    const handle = setTimeout(() => {
      setBusy(true);
      setError(null);
      api.terminology
        .radlexSearch(q)
        .then(setHits)
        .catch((e: Error) => setError(e.message))
        .finally(() => setBusy(false));
    }, 200);
    return () => clearTimeout(handle);
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

  return (
    <Container>
      <PageHeader
        title="Terminology"
        description="Look up the official term for a finding (RadLex) or the standard categories used in structured reporting (RADS). For reference only — not clinical advice."
      />

      <div className="rp-tabs" role="tablist" aria-label="Terminology source">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'radlex'}
          className={`rp-tab ${tab === 'radlex' ? 'active' : ''}`}
          onClick={() => setTab('radlex')}
        >
          RadLex
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'rads'}
          className={`rp-tab ${tab === 'rads' ? 'active' : ''}`}
          onClick={() => setTab('rads')}
        >
          RADS systems
        </button>
      </div>

      <div aria-live="polite">
        {error && <Banner tone="warn" title="Lookup failed">{error}</Banner>}
      </div>

      {tab === 'radlex' && (
        <div className="rp-panel rp-anim-fade-in" key="radlex">
          <div className="rp-panel-title">RadLex search</div>
          <div className="section-block">
            <label htmlFor="radlex-q">Term</label>
            <input
              id="radlex-q"
              className="rp-input"
              placeholder="e.g. ground-glass opacity"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              autoFocus
            />
          </div>
          <ul className="rp-list" aria-live="polite" aria-busy={busy}>
            <li className="rp-row between rp-divider-row">
              <span className="rp-stat-label rp-cell f1">Code</span>
              <span className="rp-stat-label rp-cell f2">Preferred name</span>
              <span className="rp-stat-label rp-cell f2">Synonyms</span>
            </li>
            {!busy && query.trim().length < 2 && (
              <li className="rp-page-sub rp-divider-row">
                Type at least 2 characters to search.
              </li>
            )}
            {busy && <li className="rp-page-sub rp-divider-row">Searching…</li>}
            {!busy && query.trim().length >= 2 && hits.length === 0 && (
              <li className="rp-page-sub rp-divider-row">No matches.</li>
            )}
            {hits.map((h) => (
              <li key={h.code} className="rp-row between rp-divider-row">
                <span className="rp-cell f1">
                  <code>{h.code}</code>
                </span>
                <span className="rp-cell f2 rp-narrative">{h.preferredName}</span>
                <span className="rp-cell f2 rp-page-sub">
                  {(h.synonyms ?? []).join(' · ') || '—'}
                </span>
              </li>
            ))}
          </ul>
        </div>
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
