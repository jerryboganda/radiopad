'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Eye, GraduationCap, Lock, Users } from 'lucide-react';
import {
  api,
  TEACHING_DIFFICULTY_LABELS,
  type TeachingCase,
  type TeachingDifficulty,
} from '@/lib/api';
import { teachingCaseHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

const DIFFICULTIES: readonly TeachingDifficulty[] = [0, 1, 2];

/**
 * PRD §14.14 (TF-004 / TF-007) — browse and search the tenant teaching library.
 *
 * Filtering is done SERVER-side (the endpoint takes modality / bodyPart /
 * difficulty / tag / free text) rather than fetching everything and narrowing
 * in the browser: the library grows without bound, and the server is also where
 * the visibility rule lives — a client-side filter would have to be trusted not
 * to reveal another author's private draft.
 */
export default function TeachingLibraryPage() {
  const [cases, setCases] = useState<TeachingCase[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [modality, setModality] = useState('');
  const [bodyPart, setBodyPart] = useState('');
  const [difficulty, setDifficulty] = useState<'' | TeachingDifficulty>('');
  const [tag, setTag] = useState('');
  const [query, setQuery] = useState('');
  const [mine, setMine] = useState(false);

  const refresh = useCallback(() => {
    setLoading(true);
    setError(null);
    api.teachingCases
      .search({
        modality: modality || undefined,
        bodyPart: bodyPart || undefined,
        difficulty: difficulty === '' ? undefined : difficulty,
        tag: tag || undefined,
        q: query.trim() || undefined,
        mine: mine || undefined,
        take: 200,
      })
      .then((res) => {
        setCases(res.items);
        setTotal(res.total);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [modality, bodyPart, difficulty, tag, query, mine]);

  // Debounced so typing in the search box doesn't fire a request per keystroke.
  useEffect(() => {
    const t = setTimeout(refresh, 250);
    return () => clearTimeout(t);
  }, [refresh]);

  // Filter dropdowns are built from what is actually in the library, so every
  // option leads somewhere. They intentionally reflect the CURRENT result set
  // plus the active selection, which is never filtered away.
  const modalityOptions = useMemo(
    () => uniqueSorted(cases.map((c) => c.modality), modality),
    [cases, modality],
  );
  const bodyPartOptions = useMemo(
    () => uniqueSorted(cases.map((c) => c.bodyPart), bodyPart),
    [cases, bodyPart],
  );
  const tagOptions = useMemo(
    () => uniqueSorted(cases.flatMap((c) => splitTags(c.tags)), tag),
    [cases, tag],
  );

  const hasFilters = Boolean(modality || bodyPart || difficulty !== '' || tag || query || mine);

  const clearFilters = () => {
    setModality('');
    setBodyPart('');
    setDifficulty('');
    setTag('');
    setQuery('');
    setMine(false);
  };

  return (
    <Container>
      <PageHeader
        title="Teaching file"
        description="De-identified teaching cases from your workspace. Every case is anonymised when it is saved — patient names, identifiers, and dates are removed before anything is stored."
      />

      {error && (
        <ErrorState title="Couldn't load the teaching library" message={error} onRetry={refresh} />
      )}

      {!error && (
        <>
          <div className="rp-filter-bar">
            <select
              className="rp-input"
              aria-label="Filter by modality"
              value={modality}
              onChange={(e) => setModality(e.target.value)}
              style={{ maxWidth: 180 }}
            >
              <option value="">All modalities</option>
              {modalityOptions.map((m) => (
                <option key={m} value={m}>{m}</option>
              ))}
            </select>

            <select
              className="rp-input"
              aria-label="Filter by body part"
              value={bodyPart}
              onChange={(e) => setBodyPart(e.target.value)}
              style={{ maxWidth: 180 }}
            >
              <option value="">All body parts</option>
              {bodyPartOptions.map((b) => (
                <option key={b} value={b}>{b}</option>
              ))}
            </select>

            <select
              className="rp-input"
              aria-label="Filter by difficulty"
              value={difficulty === '' ? '' : String(difficulty)}
              onChange={(e) =>
                setDifficulty(e.target.value === '' ? '' : (Number(e.target.value) as TeachingDifficulty))
              }
              style={{ maxWidth: 180 }}
            >
              <option value="">All levels</option>
              {DIFFICULTIES.map((d) => (
                <option key={d} value={d}>{TEACHING_DIFFICULTY_LABELS[d]}</option>
              ))}
            </select>

            <select
              className="rp-input"
              aria-label="Filter by tag"
              value={tag}
              onChange={(e) => setTag(e.target.value)}
              style={{ maxWidth: 180 }}
            >
              <option value="">All tags</option>
              {tagOptions.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>

            <input
              type="search"
              className="rp-input rp-search"
              placeholder="Search titles, diagnoses, teaching points…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              aria-label="Search teaching cases"
            />

            <button
              type="button"
              className={mine ? 'primary-ghost' : 'subtle'}
              aria-pressed={mine}
              onClick={() => setMine((v) => !v)}
            >
              My cases
            </button>

            {hasFilters && (
              <button type="button" className="subtle" onClick={clearFilters}>
                Clear
              </button>
            )}
          </div>

          {loading ? (
            <div className="rp-card-grid" aria-busy="true" aria-live="polite">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="rp-panel" style={{ margin: 0 }}>
                  <Skeleton variant="block" height={132} />
                </div>
              ))}
            </div>
          ) : cases.length === 0 ? (
            hasFilters ? (
              <EmptyState
                title="No matching teaching cases"
                description="Try a different search, a broader filter, or clear the filters to see the whole library."
                icon={<GraduationCap size={18} strokeWidth={1.6} aria-hidden />}
                action={
                  <button type="button" className="primary-ghost" onClick={clearFilters}>
                    Clear filters
                  </button>
                }
              />
            ) : (
              <EmptyState
                title="No teaching cases yet"
                description="Open a report and choose “Save as teaching case” to add the first one. The report is de-identified automatically before it is saved."
                icon={<GraduationCap size={18} strokeWidth={1.6} aria-hidden />}
                action={
                  <Link href="/worklist" className="primary-ghost" style={{ textDecoration: 'none' }}>
                    Go to worklist
                  </Link>
                }
              />
            )
          ) : (
            <>
              <p className="rp-card-meta" style={{ margin: '0 0 10px' }} aria-live="polite">
                {total} {total === 1 ? 'case' : 'cases'}
              </p>
              <div className="rp-card-grid rp-stagger">
                {cases.map((c) => <TeachingCaseCard key={c.id} teachingCase={c} />)}
              </div>
            </>
          )}
        </>
      )}
    </Container>
  );
}

function TeachingCaseCard({ teachingCase: c }: { teachingCase: TeachingCase }) {
  const tags = splitTags(c.tags);
  const published = c.visibility === 1;
  const summary = c.teachingPoints || c.diagnosis || c.impressionText;

  return (
    <Link href={teachingCaseHref(c.id)} className="rp-card" style={{ textDecoration: 'none' }}>
      <div className="rp-card-head">
        <div style={{ minWidth: 0 }}>
          <h2 className="rp-card-title">{c.title}</h2>
          <code className="rp-card-id">
            {[c.modality, c.bodyPart].filter(Boolean).join(' / ') || 'Unclassified'}
          </code>
        </div>
        <span className={`badge ${published ? 'ok' : 'info'}`}>
          {published ? (
            <><Users size={11} strokeWidth={1.9} aria-hidden /> Shared</>
          ) : (
            <><Lock size={11} strokeWidth={1.9} aria-hidden /> Private</>
          )}
        </span>
      </div>

      <div className="rp-chip-row">
        <span className="rp-chip">{TEACHING_DIFFICULTY_LABELS[c.difficulty] ?? c.difficultyName}</span>
        {tags.slice(0, 3).map((t) => (
          <span key={t} className="rp-chip">{t}</span>
        ))}
        {tags.length > 3 && <span className="rp-chip">+{tags.length - 3}</span>}
      </div>

      {c.diagnosis && (
        <p className="rp-card-meta" style={{ margin: 0 }}>{c.diagnosis}</p>
      )}
      {summary && summary !== c.diagnosis && (
        <p className="rp-card-meta" style={{ margin: 0 }}>{truncate(summary, 140)}</p>
      )}

      <div className="rp-card-meta" style={{ display: 'flex', alignItems: 'center', gap: 5, marginTop: 'auto' }}>
        <Eye size={12} strokeWidth={1.8} aria-hidden />
        {c.viewCount} {c.viewCount === 1 ? 'view' : 'views'}
      </div>
    </Link>
  );
}

function splitTags(csv: string): string[] {
  return (csv || '').split(',').map((s) => s.trim()).filter(Boolean);
}

/** Distinct, sorted, and always including `keep` so an active filter never vanishes. */
function uniqueSorted(values: string[], keep: string): string[] {
  const set = new Set(values.map((v) => v.trim()).filter(Boolean));
  if (keep) set.add(keep);
  return [...set].sort((a, b) => a.localeCompare(b));
}

function truncate(text: string, max: number): string {
  const clean = text.replace(/\s+/g, ' ').trim();
  return clean.length <= max ? clean : `${clean.slice(0, max - 1)}…`;
}
