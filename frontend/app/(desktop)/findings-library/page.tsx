'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  BookOpen,
  Check,
  Clock,
  Copy,
  Eye,
  Lightbulb,
  MoreHorizontal,
  Plus,
  Search,
  Star,
  Upload,
  X,
} from 'lucide-react';
import {
  api,
  type LibraryRecentUse,
  type LibraryRef,
  type ReportTemplate,
  type Snippet,
} from '@/lib/api';
import Container from '@/components/shell/Container';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { useToast } from '@/components/ui/ToastProvider';

// A template section as stored in ReportTemplate.sectionsJson. Snippet.sectionsJson
// uses the same shape, with the body in `text` rather than `placeholder`.
type StoredSection = {
  id: string;
  label: string;
  placeholder?: string;
  text?: string;
  required?: boolean;
};

/** One reusable phrase, from either source. */
type Phrase = {
  key: string;
  sectionLabel: string;
  text: string;
};

/**
 * A library entry. `source` distinguishes the two backings: template groups are
 * derived read-only from report templates, snippets are tenant-authored rows that
 * can be edited and deleted.
 */
type LibraryItem = {
  key: string;
  source: 'template' | 'snippet';
  /** Stable id used for favourites / recents — the template row id or snippet id. */
  entityKey: string;
  title: string;
  subtitle: string;
  modality: string;
  bodyPart: string;
  category: string;
  updatedAt: string;
  phrases: Phrase[];
  /** Present only for snippets, so the ⋯ menu can offer Edit / Delete. */
  snippet?: Snippet;
};

const TRUNCATE_AT = 180;
const PAGE_SIZE = 10;

/** The section skeleton a new snippet starts from — the standard report spine. */
const DEFAULT_SECTIONS: Array<{ id: string; label: string }> = [
  { id: 'technique', label: 'Technique' },
  { id: 'comparison', label: 'Comparison' },
  { id: 'findings', label: 'Findings' },
  { id: 'impression', label: 'Impression' },
  { id: 'recommendation', label: 'Recommendation' },
];

function parseSections(raw: string): StoredSection[] {
  try {
    const parsed = JSON.parse(raw) as { sections?: StoredSection[] } | StoredSection[];
    const sections = Array.isArray(parsed) ? parsed : (parsed.sections ?? []);
    return Array.isArray(sections) ? sections : [];
  } catch {
    return [];
  }
}

function contrastLabel(token: string | undefined): string | null {
  switch (token) {
    case 'None':
      return 'Without contrast';
    case 'With':
      return 'With contrast';
    case 'WithAndWithout':
      return 'With and without contrast';
    default:
      return null;
  }
}

function templateToItem(t: ReportTemplate): LibraryItem | null {
  const phrases = parseSections(t.sectionsJson)
    .filter((s) => (s.placeholder ?? '').trim().length > 0)
    .map((s) => ({
      key: `${t.id}:${s.id}`,
      sectionLabel: s.label || s.id,
      text: (s.placeholder ?? '').trim(),
    }));
  if (phrases.length === 0) return null;
  const parts = [t.modality, t.bodyPart, contrastLabel(t.contrast)].filter(Boolean);
  return {
    key: `template:${t.id}`,
    source: 'template',
    entityKey: t.id,
    title: t.name || t.templateId,
    subtitle: parts.join(' · '),
    modality: t.modality,
    bodyPart: t.bodyPart,
    category: t.subspecialty || '',
    updatedAt: t.updatedAt,
    phrases,
  };
}

function snippetToItem(s: Snippet): LibraryItem {
  const phrases = parseSections(s.sectionsJson)
    .filter((sec) => (sec.text ?? sec.placeholder ?? '').trim().length > 0)
    .map((sec) => ({
      key: `${s.id}:${sec.id}`,
      sectionLabel: sec.label || sec.id,
      text: (sec.text ?? sec.placeholder ?? '').trim(),
    }));
  const parts = [s.modality, s.bodyPart].filter(Boolean);
  return {
    key: `snippet:${s.id}`,
    source: 'snippet',
    entityKey: s.id,
    title: s.name,
    subtitle: parts.join(' · '),
    modality: s.modality,
    bodyPart: s.bodyPart,
    category: s.category || '',
    updatedAt: s.updatedAt,
    phrases,
    snippet: s,
  };
}

function fullText(item: LibraryItem): string {
  return item.phrases.map((p) => `${p.sectionLabel}:\n${p.text}`).join('\n\n');
}

function refOf(item: LibraryItem): LibraryRef {
  return { entityType: item.source, entityKey: item.entityKey };
}

export default function FindingsLibraryPage() {
  const { toast } = useToast();

  const [templates, setTemplates] = useState<ReportTemplate[] | null>(null);
  const [snippets, setSnippets] = useState<Snippet[] | null>(null);
  const [favorites, setFavorites] = useState<Set<string>>(new Set());
  const [recent, setRecent] = useState<LibraryRecentUse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const [query, setQuery] = useState('');
  const [modality, setModality] = useState('');
  const [category, setCategory] = useState('');
  const [sort, setSort] = useState<'name' | 'recent' | 'phrases'>('name');
  const [scope, setScope] = useState<'all' | 'favorites' | 'recent' | 'snippets'>('all');
  const [page, setPage] = useState(1);

  const [editing, setEditing] = useState<Snippet | 'new' | null>(null);
  const [previewing, setPreviewing] = useState<LibraryItem | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      api.templates.list(),
      api.findingsLibrary.listSnippets(),
      api.findingsLibrary.favorites(),
      api.findingsLibrary.recent(200),
    ])
      .then(([t, s, f, r]) => {
        setTemplates(t);
        setSnippets(s);
        setFavorites(new Set(f.map((x) => `${x.entityType}:${x.entityKey}`)));
        setRecent(r);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const items = useMemo<LibraryItem[]>(() => {
    const fromTemplates = (templates ?? [])
      .map(templateToItem)
      .filter((x): x is LibraryItem => x !== null);
    const fromSnippets = (snippets ?? []).map(snippetToItem);
    return [...fromSnippets, ...fromTemplates];
  }, [templates, snippets]);

  const recentByKey = useMemo(() => {
    const map = new Map<string, LibraryRecentUse>();
    for (const r of recent) map.set(`${r.entityType}:${r.entityKey}`, r);
    return map;
  }, [recent]);

  const modalityOptions = useMemo(
    () => [...new Set(items.map((i) => i.modality).filter(Boolean))].sort(),
    [items],
  );
  const categoryOptions = useMemo(
    () => [...new Set(items.map((i) => i.category).filter(Boolean))].sort(),
    [items],
  );

  const counts = useMemo(
    () => ({
      all: items.length,
      favorites: items.filter((i) => favorites.has(i.key)).length,
      recent: items.filter((i) => recentByKey.has(i.key)).length,
      snippets: items.filter((i) => i.source === 'snippet').length,
    }),
    [items, favorites, recentByKey],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matched = items
      .filter((i) => {
        if (scope === 'favorites' && !favorites.has(i.key)) return false;
        if (scope === 'recent' && !recentByKey.has(i.key)) return false;
        if (scope === 'snippets' && i.source !== 'snippet') return false;
        if (modality && i.modality !== modality) return false;
        if (category && i.category !== category) return false;
        return true;
      })
      .map((i) => {
        if (!q) return i;
        const headHit =
          i.title.toLowerCase().includes(q) ||
          i.bodyPart.toLowerCase().includes(q) ||
          i.modality.toLowerCase().includes(q) ||
          i.category.toLowerCase().includes(q);
        if (headHit) return i;
        const phrases = i.phrases.filter(
          (p) => p.text.toLowerCase().includes(q) || p.sectionLabel.toLowerCase().includes(q),
        );
        return { ...i, phrases };
      })
      .filter((i) => i.phrases.length > 0);

    const sorted = [...matched];
    if (sort === 'name') sorted.sort((a, b) => a.title.localeCompare(b.title));
    else if (sort === 'phrases') sorted.sort((a, b) => b.phrases.length - a.phrases.length);
    else {
      sorted.sort((a, b) => {
        const ra = recentByKey.get(a.key)?.lastUsedAt ?? '';
        const rb = recentByKey.get(b.key)?.lastUsedAt ?? '';
        if (ra !== rb) return rb.localeCompare(ra);
        return b.updatedAt.localeCompare(a.updatedAt);
      });
    }
    return sorted;
  }, [items, query, modality, category, scope, sort, favorites, recentByKey]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageSafe = Math.min(page, totalPages);
  const visible = filtered.slice((pageSafe - 1) * PAGE_SIZE, pageSafe * PAGE_SIZE);
  const phraseCount = filtered.reduce((n, i) => n + i.phrases.length, 0);
  const hasFilters =
    query.trim().length > 0 || modality !== '' || category !== '' || scope !== 'all';

  // Any change to the filter set can shrink the result list past the current page.
  useEffect(() => {
    setPage(1);
  }, [query, modality, category, scope, sort]);

  function clearFilters() {
    setQuery('');
    setModality('');
    setCategory('');
    setScope('all');
  }

  async function toggleFavorite(item: LibraryItem) {
    const isFav = favorites.has(item.key);
    // Optimistic — the star must feel instant, and a failure re-syncs from the server.
    setFavorites((prev) => {
      const next = new Set(prev);
      if (isFav) next.delete(item.key);
      else next.add(item.key);
      return next;
    });
    try {
      await api.findingsLibrary.toggleFavorite({ ...refOf(item), favorite: !isFav });
    } catch (e) {
      setFavorites((prev) => {
        const next = new Set(prev);
        if (isFav) next.add(item.key);
        else next.delete(item.key);
        return next;
      });
      toast({ tone: 'danger', title: 'Could not update favourite', message: (e as Error).message });
    }
  }

  async function useInReport(item: LibraryItem) {
    try {
      await navigator.clipboard.writeText(fullText(item));
    } catch {
      toast({
        tone: 'danger',
        title: 'Copy failed',
        message: 'Your browser blocked clipboard access. Open Preview and copy the text manually.',
      });
      return;
    }
    toast({
      tone: 'success',
      title: 'Copied for your report',
      message: `${item.title} is on your clipboard — paste it into the section you are dictating.`,
    });
    try {
      await api.findingsLibrary.recordUse(refOf(item));
      setRecent(await api.findingsLibrary.recent(200));
    } catch {
      // The clipboard already has the text; a failed usage ping is not worth a second toast.
    }
  }

  async function deleteSnippet(item: LibraryItem) {
    if (!item.snippet) return;
    if (!window.confirm(`Delete the snippet "${item.title}"? This cannot be undone.`)) return;
    try {
      await api.findingsLibrary.deleteSnippet(item.snippet.id);
      setSnippets((prev) => (prev ?? []).filter((s) => s.id !== item.snippet!.id));
      toast({ tone: 'success', title: 'Snippet deleted', message: `${item.title} was removed.` });
    } catch (e) {
      toast({ tone: 'danger', title: 'Delete failed', message: (e as Error).message });
    }
  }

  async function onImportFile(file: File) {
    let rows: Array<Record<string, unknown>>;
    try {
      const parsed = JSON.parse(await file.text()) as unknown;
      const arr = Array.isArray(parsed)
        ? parsed
        : ((parsed as { snippets?: unknown[] })?.snippets ?? null);
      if (!Array.isArray(arr)) throw new Error('not an array');
      rows = arr as Array<Record<string, unknown>>;
    } catch {
      toast({
        tone: 'danger',
        title: "Couldn't read that file",
        message: 'Import expects a JSON array of snippets, or an object with a "snippets" array.',
      });
      return;
    }

    try {
      const result = await api.findingsLibrary.importSnippets(
        rows.map((r) => ({
          name: String(r.name ?? ''),
          modality: String(r.modality ?? ''),
          bodyPart: String(r.bodyPart ?? ''),
          category: String(r.category ?? ''),
          sectionsJson:
            typeof r.sectionsJson === 'string' ? r.sectionsJson : JSON.stringify(r.sections ?? []),
        })),
      );
      setSnippets(await api.findingsLibrary.listSnippets());
      toast({
        tone: 'success',
        title: `Imported ${result.imported} ${result.imported === 1 ? 'snippet' : 'snippets'}`,
        message:
          result.skipped.length > 0
            ? `Skipped ${result.skipped.length}: ${result.skipped.slice(0, 3).join(', ')}${result.skipped.length > 3 ? '…' : ''}`
            : 'They are in your library now.',
      });
    } catch (e) {
      toast({ tone: 'danger', title: 'Import failed', message: (e as Error).message });
    }
  }

  return (
    <Container>
      <div className="rp-fl-hero">
        <div className="rp-fl-hero-main">
          <div className="rp-fl-hero-icon" aria-hidden>
            <BookOpen size={26} strokeWidth={1.6} />
          </div>
          <div className="rp-fl-hero-text">
            <h1 className="rp-page-title">Findings Library</h1>
            <p className="rp-page-sub">
              Reusable phrasing from your report templates, plus the snippets your department has
              written. Search it, star what you use, and pull it straight into a report.
            </p>
          </div>
        </div>
        <div className="rp-fl-hero-actions">
          <input
            ref={fileInputRef}
            type="file"
            accept="application/json,.json"
            hidden
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = '';
              if (file) void onImportFile(file);
            }}
          />
          <button type="button" className="ghost" onClick={() => fileInputRef.current?.click()}>
            <Upload size={14} strokeWidth={1.8} aria-hidden /> Import
          </button>
          <button type="button" className="primary" onClick={() => setEditing('new')}>
            <Plus size={14} strokeWidth={1.8} aria-hidden /> New snippet
          </button>
        </div>
      </div>

      {error && !templates && (
        <ErrorState title="Couldn't load the findings library" message={error} onRetry={load} />
      )}

      {!error && loading && !templates && (
        <div aria-busy="true" aria-live="polite">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="rp-panel" style={{ marginBottom: 16 }}>
              <Skeleton variant="text" width="30%" />
              <Skeleton variant="block" height={90} style={{ marginTop: 12 }} />
            </div>
          ))}
        </div>
      )}

      {templates && snippets && (
        <div className="rp-fl-layout">
          <div>
            <div className="rp-fl-toolbar">
              <div className="rp-search" style={{ position: 'relative' }}>
                <Search
                  size={14}
                  strokeWidth={1.8}
                  aria-hidden
                  style={{
                    position: 'absolute',
                    left: 10,
                    top: '50%',
                    transform: 'translateY(-50%)',
                    pointerEvents: 'none',
                    opacity: 0.6,
                  }}
                />
                <input
                  type="search"
                  className="rp-input"
                  style={{ paddingLeft: 30, width: '100%' }}
                  placeholder="Search phrases, sections, templates…"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  aria-label="Search the findings library"
                />
              </div>
              <div className="rp-fl-select">
                <select
                  aria-label="Filter by modality"
                  value={modality}
                  onChange={(e) => setModality(e.target.value)}
                >
                  <option value="">All modalities</option>
                  {modalityOptions.map((m) => (
                    <option key={m} value={m}>
                      {m}
                    </option>
                  ))}
                </select>
              </div>
              <div className="rp-fl-select">
                <select
                  aria-label="Sort the library"
                  value={sort}
                  onChange={(e) => setSort(e.target.value as typeof sort)}
                >
                  <option value="name">Name A–Z</option>
                  <option value="recent">Recently used</option>
                  <option value="phrases">Most phrases</option>
                </select>
              </div>
              {hasFilters && (
                <button type="button" className="subtle" onClick={clearFilters}>
                  Clear
                </button>
              )}
            </div>

            {categoryOptions.length > 0 && (
              <div className="rp-fl-chips">
                <button
                  type="button"
                  className={`rp-fl-chip${category === '' ? ' active' : ''}`}
                  onClick={() => setCategory('')}
                  aria-pressed={category === ''}
                >
                  All categories
                </button>
                {categoryOptions.map((c) => (
                  <button
                    key={c}
                    type="button"
                    className={`rp-fl-chip${category === c ? ' active' : ''}`}
                    onClick={() => setCategory(category === c ? '' : c)}
                    aria-pressed={category === c}
                  >
                    {c}
                  </button>
                ))}
                <span className="rp-fl-count">
                  {filtered.length} {filtered.length === 1 ? 'entry' : 'entries'} · {phraseCount}{' '}
                  {phraseCount === 1 ? 'phrase' : 'phrases'}
                </span>
              </div>
            )}

            {items.length === 0 ? (
              <EmptyState
                title="No reusable phrases yet"
                description="Phrases come from the section text in your report templates. Add section text to a template, or write your first snippet, and it will show up here."
                action={
                  <button type="button" className="primary" onClick={() => setEditing('new')}>
                    New snippet
                  </button>
                }
              />
            ) : filtered.length === 0 ? (
              <EmptyState
                title="Nothing matches"
                description="Try a different search term, or clear the filters."
                action={
                  <button type="button" className="primary-ghost" onClick={clearFilters}>
                    Clear filters
                  </button>
                }
              />
            ) : (
              <>
                <div className="rp-fl-list rp-stagger" aria-live="polite">
                  {visible.map((item) => (
                    <LibraryCard
                      key={item.key}
                      item={item}
                      favorite={favorites.has(item.key)}
                      lastUsed={recentByKey.get(item.key)}
                      onToggleFavorite={() => void toggleFavorite(item)}
                      onUse={() => void useInReport(item)}
                      onPreview={() => setPreviewing(item)}
                      onEdit={item.snippet ? () => setEditing(item.snippet!) : undefined}
                      onDelete={item.snippet ? () => void deleteSnippet(item) : undefined}
                    />
                  ))}
                </div>

                {totalPages > 1 && (
                  <div className="rp-fl-pagination">
                    <span className="rp-page-sub">
                      Page {pageSafe} of {totalPages}
                    </span>
                    <div className="rp-fl-pages">
                      <button
                        type="button"
                        className="rp-rb-page-btn"
                        onClick={() => setPage((p) => Math.max(1, p - 1))}
                        disabled={pageSafe === 1}
                      >
                        Previous
                      </button>
                      {Array.from({ length: totalPages }, (_, i) => i + 1).map((n) => (
                        <button
                          key={n}
                          type="button"
                          className={`rp-rb-page-btn${n === pageSafe ? ' active' : ''}`}
                          onClick={() => setPage(n)}
                          aria-current={n === pageSafe ? 'page' : undefined}
                        >
                          {n}
                        </button>
                      ))}
                      <button
                        type="button"
                        className="rp-rb-page-btn"
                        onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                        disabled={pageSafe === totalPages}
                      >
                        Next
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>

          <aside className="rp-fl-rail">
            <div className="rp-panel">
              <h2 className="rp-fl-rail-title">Browse</h2>
              {(
                [
                  { id: 'all', label: 'All phrasing', icon: BookOpen, count: counts.all },
                  { id: 'favorites', label: 'My favourites', icon: Star, count: counts.favorites },
                  { id: 'recent', label: 'Recently used', icon: Clock, count: counts.recent },
                  { id: 'snippets', label: 'Custom snippets', icon: Plus, count: counts.snippets },
                ] as const
              ).map((row) => (
                <button
                  key={row.id}
                  type="button"
                  className={`rp-fl-rail-row${scope === row.id ? ' active' : ''}`}
                  onClick={() => setScope(row.id)}
                  aria-pressed={scope === row.id}
                >
                  <row.icon size={15} strokeWidth={1.8} aria-hidden />
                  {row.label}
                  <span className="rp-fl-rail-count">{row.count}</span>
                </button>
              ))}
            </div>

            <div className="rp-help">
              <p className="rp-help-title">
                <Lightbulb size={14} strokeWidth={1.8} aria-hidden />
                Tips &amp; help
              </p>
              <ul>
                <li>
                  Template phrasing is read-only here — edit it on the template itself so every
                  report that uses the template stays consistent.
                </li>
                <li>Snippets are yours to write; only the author or an admin can change one.</li>
                <li>
                  <strong>Use in report</strong> copies the whole entry and records it under
                  Recently used.
                </li>
              </ul>
            </div>

            <div className="rp-help">
              <p className="rp-help-title">
                <Star size={14} strokeWidth={1.8} aria-hidden />
                Pro tip
              </p>
              <p>
                Star the handful of entries you reach for every session. The favourites view is the
                fastest way back to them, and it is per-user — your list never changes anyone
                else&apos;s.
              </p>
            </div>
          </aside>
        </div>
      )}

      {previewing && (
        <PreviewModal item={previewing} onClose={() => setPreviewing(null)} />
      )}

      {editing && (
        <SnippetModal
          snippet={editing === 'new' ? null : editing}
          modalityOptions={modalityOptions}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setSnippets((prev) => {
              const rest = (prev ?? []).filter((s) => s.id !== saved.id);
              return [...rest, saved].sort((a, b) => a.name.localeCompare(b.name));
            });
            setEditing(null);
          }}
        />
      )}
    </Container>
  );
}

function LibraryCard({
  item,
  favorite,
  lastUsed,
  onToggleFavorite,
  onUse,
  onPreview,
  onEdit,
  onDelete,
}: {
  item: LibraryItem;
  favorite: boolean;
  lastUsed?: LibraryRecentUse;
  onToggleFavorite: () => void;
  onUse: () => void;
  onPreview: () => void;
  onEdit?: () => void;
  onDelete?: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="rp-card rp-fl-item">
      <div className="rp-fl-item-head">
        <div style={{ minWidth: 0 }}>
          <h2 className="rp-fl-item-title">{item.title}</h2>
          {item.subtitle && <div className="rp-fl-item-sub">{item.subtitle}</div>}
        </div>
        <div className="rp-fl-item-tools">
          <button
            type="button"
            className={`rp-fl-star${favorite ? ' on' : ''}`}
            onClick={onToggleFavorite}
            aria-pressed={favorite}
            aria-label={favorite ? `Unstar ${item.title}` : `Star ${item.title}`}
            title={favorite ? 'Remove from favourites' : 'Add to favourites'}
          >
            <Star size={16} strokeWidth={1.8} fill={favorite ? 'currentColor' : 'none'} aria-hidden />
          </button>
          {(onEdit || onDelete) && (
            <div className="rp-fl-menu">
              <button
                type="button"
                className="ghost"
                onClick={() => setMenuOpen((v) => !v)}
                aria-expanded={menuOpen}
                aria-label={`More actions for ${item.title}`}
              >
                <MoreHorizontal size={15} strokeWidth={1.8} aria-hidden />
              </button>
              {menuOpen && (
                <div className="rp-fl-menu-list" role="menu">
                  {onEdit && (
                    <button
                      type="button"
                      role="menuitem"
                      onClick={() => {
                        setMenuOpen(false);
                        onEdit();
                      }}
                    >
                      Edit snippet
                    </button>
                  )}
                  {onDelete && (
                    <button
                      type="button"
                      role="menuitem"
                      className="danger"
                      onClick={() => {
                        setMenuOpen(false);
                        onDelete();
                      }}
                    >
                      Delete snippet
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      <div className="rp-chip-row">
        <span className={`rp-fl-source ${item.source}`}>
          {item.source === 'snippet' ? 'Snippet' : 'Template'}
        </span>
        {item.category && <span className="rp-chip">{item.category}</span>}
        <span className="rp-chip">
          {item.phrases.length} {item.phrases.length === 1 ? 'phrase' : 'phrases'}
        </span>
      </div>

      <ul className="rp-fl-phrases">
        {item.phrases.map((p) => (
          <PhraseRow key={p.key} phrase={p} />
        ))}
      </ul>

      <div className="rp-fl-item-foot">
        <button type="button" className="primary" onClick={onUse}>
          Use in report
        </button>
        <button type="button" className="ghost" onClick={onPreview}>
          <Eye size={14} strokeWidth={1.8} aria-hidden /> Preview
        </button>
        {lastUsed && (
          <span className="rp-fl-used">
            Used {lastUsed.useCount}× · last {new Date(lastUsed.lastUsedAt).toLocaleDateString()}
          </span>
        )}
      </div>
    </div>
  );
}

function PhraseRow({ phrase }: { phrase: Phrase }) {
  const { toast } = useToast();
  const [expanded, setExpanded] = useState(false);
  const [copied, setCopied] = useState(false);

  const isLong = phrase.text.length > TRUNCATE_AT;
  const shown = expanded || !isLong ? phrase.text : `${phrase.text.slice(0, TRUNCATE_AT).trimEnd()}…`;

  async function copyPhrase() {
    try {
      await navigator.clipboard.writeText(phrase.text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
      toast({
        tone: 'success',
        title: 'Phrase copied',
        message: `${phrase.sectionLabel} text is on your clipboard.`,
      });
    } catch {
      toast({
        tone: 'danger',
        title: 'Copy failed',
        message: 'Your browser blocked clipboard access. Select the text and copy it manually.',
      });
    }
  }

  return (
    <li className="rp-fl-phrase">
      <div className="rp-fl-phrase-body">
        <div className="rp-fl-phrase-label">{phrase.sectionLabel}</div>
        <p className="rp-fl-phrase-text">{shown}</p>
        {isLong && (
          <button
            type="button"
            className="subtle"
            style={{ marginTop: 6 }}
            onClick={() => setExpanded((v) => !v)}
            aria-expanded={expanded}
          >
            {expanded ? 'Show less' : 'Show more'}
          </button>
        )}
      </div>
      <div className="rp-fl-phrase-actions">
        <button
          type="button"
          className="ghost"
          onClick={copyPhrase}
          aria-label={`Copy the ${phrase.sectionLabel} phrase`}
        >
          {copied ? (
            <Check size={14} strokeWidth={1.8} aria-hidden />
          ) : (
            <Copy size={14} strokeWidth={1.8} aria-hidden />
          )}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
    </li>
  );
}

function PreviewModal({ item, onClose }: { item: LibraryItem; onClose: () => void }) {
  const { toast } = useToast();

  async function copyAll() {
    try {
      await navigator.clipboard.writeText(fullText(item));
      toast({ tone: 'success', title: 'Copied', message: `All of ${item.title} is on your clipboard.` });
    } catch {
      toast({
        tone: 'danger',
        title: 'Copy failed',
        message: 'Your browser blocked clipboard access.',
      });
    }
  }

  return (
    <div
      className="rp-fl-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label={`Preview ${item.title}`}
      onClick={onClose}
    >
      <div className="rp-panel rp-fl-modal" onClick={(e) => e.stopPropagation()}>
        <div className="rp-fl-modal-head">
          <div style={{ minWidth: 0 }}>
            <h2 className="rp-fl-item-title">{item.title}</h2>
            {item.subtitle && <div className="rp-fl-item-sub">{item.subtitle}</div>}
          </div>
          <button type="button" className="ghost" onClick={onClose} aria-label="Close preview">
            <X size={15} strokeWidth={1.8} aria-hidden />
          </button>
        </div>

        {item.phrases.map((p) => (
          <div key={p.key} className="rp-fl-section">
            <div className="rp-fl-phrase-label">{p.sectionLabel}</div>
            <p className="rp-fl-phrase-text">{p.text}</p>
          </div>
        ))}

        <div className="rp-fl-modal-foot">
          <button type="button" className="ghost" onClick={onClose}>
            Close
          </button>
          <button type="button" className="primary" onClick={() => void copyAll()}>
            Copy all
          </button>
        </div>
      </div>
    </div>
  );
}

function SnippetModal({
  snippet,
  modalityOptions,
  onClose,
  onSaved,
}: {
  snippet: Snippet | null;
  modalityOptions: string[];
  onClose: () => void;
  onSaved: (saved: Snippet) => void;
}) {
  const { toast } = useToast();
  const [name, setName] = useState(snippet?.name ?? '');
  const [modality, setModality] = useState(snippet?.modality ?? '');
  const [bodyPart, setBodyPart] = useState(snippet?.bodyPart ?? '');
  const [category, setCategory] = useState(snippet?.category ?? '');
  const [saving, setSaving] = useState(false);

  const [sections, setSections] = useState<Array<{ id: string; label: string; text: string }>>(
    () => {
      const existing = snippet ? parseSections(snippet.sectionsJson) : [];
      return DEFAULT_SECTIONS.map((d) => {
        const found = existing.find((s) => s.id === d.id);
        return { id: d.id, label: found?.label || d.label, text: found?.text ?? found?.placeholder ?? '' };
      });
    },
  );

  async function save() {
    if (!name.trim()) {
      toast({ tone: 'danger', title: 'Name required', message: 'Give the snippet a name first.' });
      return;
    }
    setSaving(true);
    try {
      const saved = await api.findingsLibrary.saveSnippet({
        id: snippet?.id,
        name: name.trim(),
        modality: modality.trim(),
        bodyPart: bodyPart.trim(),
        category: category.trim(),
        // Empty sections are dropped so the card never renders a blank row.
        sectionsJson: JSON.stringify(sections.filter((s) => s.text.trim().length > 0)),
      });
      toast({
        tone: 'success',
        title: snippet ? 'Snippet updated' : 'Snippet created',
        message: `${saved.name} is in your library.`,
      });
      onSaved(saved);
    } catch (e) {
      toast({ tone: 'danger', title: 'Save failed', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="rp-fl-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label={snippet ? 'Edit snippet' : 'New snippet'}
      onClick={onClose}
    >
      <div className="rp-panel rp-fl-modal" onClick={(e) => e.stopPropagation()}>
        <div className="rp-fl-modal-head">
          <h2 className="rp-fl-item-title">{snippet ? 'Edit snippet' : 'New snippet'}</h2>
          <button type="button" className="ghost" onClick={onClose} aria-label="Close">
            <X size={15} strokeWidth={1.8} aria-hidden />
          </button>
        </div>

        <div className="rp-fl-modal-grid">
          <div className="rp-fl-field">
            <label htmlFor="fl-name">Name</label>
            <input
              id="fl-name"
              className="rp-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Normal chest CT"
            />
          </div>
          <div className="rp-fl-field">
            <label htmlFor="fl-category">Category</label>
            <input
              id="fl-category"
              className="rp-input"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              placeholder="Chest"
            />
          </div>
          <div className="rp-fl-field">
            <label htmlFor="fl-modality">Modality</label>
            <input
              id="fl-modality"
              className="rp-input"
              list="fl-modality-options"
              value={modality}
              onChange={(e) => setModality(e.target.value)}
              placeholder="CT"
            />
            <datalist id="fl-modality-options">
              {modalityOptions.map((m) => (
                <option key={m} value={m} />
              ))}
            </datalist>
          </div>
          <div className="rp-fl-field">
            <label htmlFor="fl-bodypart">Body part</label>
            <input
              id="fl-bodypart"
              className="rp-input"
              value={bodyPart}
              onChange={(e) => setBodyPart(e.target.value)}
              placeholder="Chest"
            />
          </div>
        </div>

        {sections.map((s, i) => (
          <div key={s.id} className="rp-fl-section">
            <label className="rp-fl-phrase-label" htmlFor={`fl-sec-${s.id}`}>
              {s.label}
            </label>
            <textarea
              id={`fl-sec-${s.id}`}
              className="rp-input"
              value={s.text}
              onChange={(e) =>
                setSections((prev) =>
                  prev.map((p, j) => (j === i ? { ...p, text: e.target.value } : p)),
                )
              }
              placeholder={`${s.label} text…`}
            />
          </div>
        ))}

        <div className="rp-fl-modal-foot">
          <button type="button" className="ghost" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button type="button" className="primary" onClick={() => void save()} disabled={saving}>
            {saving ? 'Saving…' : snippet ? 'Save changes' : 'Create snippet'}
          </button>
        </div>
      </div>
    </div>
  );
}
