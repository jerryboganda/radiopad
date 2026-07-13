'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookOpen, FileText } from 'lucide-react';
import { api, type CatalogItem, type ReportTemplate, type Rulebook } from '@/lib/api';
import { rulebookHref } from '@/lib/routes';
import { statusLabel as rulebookStatusLabel, statusBadge as rulebookStatusBadge } from '@/lib/rulebookStatus';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';

/**
 * Protocols reference — one card per modality × body part combination that has
 * library content, showing the contrast variants covered and which template(s)
 * and rulebook(s) the app resolves for that study context. Read-only: content
 * itself is managed on /templates and /rulebooks, catalogs under Admin.
 *
 * Resolution mirrors the report editor's binding logic (ReportInspector):
 * templates match on exact modality + bodyPart (contrast picks the variant),
 * rulebooks match when both CSV "applies to" lists contain the study values.
 */

type Combo = {
  key: string;
  modalityCode: string;
  modalityName: string;
  bodyPartCode: string;
  bodyPartName: string;
  mSort: number;
  bSort: number;
  templates: ReportTemplate[];
  rulebooks: Rulebook[];
};

const eq = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();
const csvHas = (csv: string, value: string) => (csv || '').split(',').some((part) => eq(part, value));
const splitCsv = (csv: string) => (csv || '').split(',').map((s) => s.trim()).filter(Boolean);
const norm = (s: string) => s.trim().toUpperCase();

/** Contrast token → friendly label. Tokens: "" (agnostic) | None | With | WithAndWithout. */
function contrastLabel(token: string): string {
  switch (token) {
    case 'None': return 'Without contrast';
    case 'With': return 'With contrast';
    case 'WithAndWithout': return 'With and without contrast';
    default: return 'Any contrast';
  }
}

/** TemplateStatus enum → label + badge tone. Missing status means legacy row (treated as approved). */
function templateStatus(t: ReportTemplate): { label: string; badge: string } {
  switch (t.status) {
    case 0: return { label: 'Draft', badge: 'info' };
    case 2: return { label: 'Deprecated', badge: 'danger' };
    case 3: return { label: 'In review', badge: 'warn' };
    default: return { label: 'Approved', badge: 'ok' };
  }
}

function isApprovedTemplate(t: ReportTemplate): boolean {
  return t.status === undefined || t.status === 1;
}

export default function ProtocolsPage() {
  const [modalities, setModalities] = useState<CatalogItem[]>([]);
  const [bodyParts, setBodyParts] = useState<CatalogItem[]>([]);
  const [templates, setTemplates] = useState<ReportTemplate[]>([]);
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [modalityFilter, setModalityFilter] = useState('');
  const [bodyPartFilter, setBodyPartFilter] = useState('');
  const [query, setQuery] = useState('');

  const refresh = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      api.modalities.list(),
      api.bodyParts.list(),
      api.templates.list(),
      api.rulebooks.list(),
    ])
      .then(([m, b, t, r]) => {
        setModalities(m);
        setBodyParts(b);
        setTemplates(t);
        setRulebooks(r);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  // Every modality × body part pair that could carry content: the admin catalog
  // cross-product, plus any pair referenced by a template or rulebook whose
  // values aren't (or are no longer) in the catalog — those still resolve in
  // the editor, so the reference stays honest about them.
  const combos = useMemo<Combo[]>(() => {
    const byCode = (list: CatalogItem[], code: string) =>
      list.find((c) => eq(c.code, code));

    const pairs = new Map<string, { m: string; b: string }>();
    const addPair = (m: string, b: string) => {
      const mc = m.trim();
      const bc = b.trim();
      if (!mc || !bc) return;
      const key = `${norm(mc)}|${norm(bc)}`;
      if (!pairs.has(key)) pairs.set(key, { m: mc, b: bc });
    };

    for (const m of modalities.filter((x) => x.active)) {
      for (const b of bodyParts.filter((x) => x.active)) {
        addPair(m.code, b.code);
      }
    }
    for (const t of templates) addPair(t.modality, t.bodyPart);
    for (const r of rulebooks) {
      for (const m of splitCsv(r.appliesToModalities)) {
        for (const b of splitCsv(r.appliesToBodyParts)) {
          addPair(m, b);
        }
      }
    }

    const out: Combo[] = [];
    for (const [key, { m, b }] of pairs) {
      const matchedTemplates = templates
        .filter((t) => eq(t.modality, m) && eq(t.bodyPart, b))
        .sort((a, x) => {
          const am = isApprovedTemplate(a) ? 0 : 1;
          const xm = isApprovedTemplate(x) ? 0 : 1;
          return am !== xm ? am - xm : a.name.localeCompare(x.name);
        });
      const matchedRulebooks = rulebooks
        .filter((r) => csvHas(r.appliesToModalities, m) && csvHas(r.appliesToBodyParts, b))
        .sort((a, x) => a.name.localeCompare(x.name));
      if (matchedTemplates.length === 0 && matchedRulebooks.length === 0) continue;

      const mCat = byCode(modalities, m);
      const bCat = byCode(bodyParts, b);
      out.push({
        key,
        modalityCode: mCat?.code ?? m,
        modalityName: mCat?.name || m,
        bodyPartCode: bCat?.code ?? b,
        bodyPartName: bCat?.name || b,
        mSort: mCat ? mCat.sortOrder : Number.MAX_SAFE_INTEGER,
        bSort: bCat ? bCat.sortOrder : Number.MAX_SAFE_INTEGER,
        templates: matchedTemplates,
        rulebooks: matchedRulebooks,
      });
    }

    return out.sort(
      (a, x) =>
        a.mSort - x.mSort ||
        a.modalityName.localeCompare(x.modalityName) ||
        a.bSort - x.bSort ||
        a.bodyPartName.localeCompare(x.bodyPartName),
    );
  }, [modalities, bodyParts, templates, rulebooks]);

  // Filter dropdowns list only values that actually appear in a combination,
  // so every selection can lead somewhere.
  const modalityOptions = useMemo(() => {
    const seen = new Map<string, { code: string; name: string; sort: number }>();
    for (const c of combos) {
      const k = norm(c.modalityCode);
      if (!seen.has(k)) seen.set(k, { code: c.modalityCode, name: c.modalityName, sort: c.mSort });
    }
    return [...seen.values()].sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name));
  }, [combos]);

  const bodyPartOptions = useMemo(() => {
    const seen = new Map<string, { code: string; name: string; sort: number }>();
    for (const c of combos) {
      const k = norm(c.bodyPartCode);
      if (!seen.has(k)) seen.set(k, { code: c.bodyPartCode, name: c.bodyPartName, sort: c.bSort });
    }
    return [...seen.values()].sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name));
  }, [combos]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return combos.filter((c) => {
      if (modalityFilter && !eq(c.modalityCode, modalityFilter)) return false;
      if (bodyPartFilter && !eq(c.bodyPartCode, bodyPartFilter)) return false;
      if (!q) return true;
      const haystack = [
        c.modalityCode,
        c.modalityName,
        c.bodyPartCode,
        c.bodyPartName,
        ...c.templates.map((t) => `${t.name} ${t.templateId}`),
        ...c.rulebooks.map((r) => `${r.name} ${r.rulebookId}`),
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(q);
    });
  }, [combos, modalityFilter, bodyPartFilter, query]);

  const bothFiltersSet = Boolean(modalityFilter && bodyPartFilter);
  const filterEmptyTitle = bothFiltersSet
    ? `Nothing covers ${modalityFilter} · ${bodyPartFilter} yet`
    : 'No matching protocols';
  const filterEmptyDesc = bothFiltersSet
    ? 'No template or rulebook resolves for that combination. Add a template on the Templates page or a rulebook on the Rulebooks page to cover it.'
    : 'Try a different search or clear the filters.';

  return (
    <Container>
      <PageHeader
        title="Protocols"
        description="A reference of every modality and body part combination your library covers — the contrast variants available and the template and rulebook the app resolves for each."
      />

      {error && (
        <ErrorState title="Couldn't load the protocol reference" message={error} onRetry={refresh} />
      )}

      {!error && (
        <>
          <div className="rp-filter-bar">
            <select
              className="rp-input"
              aria-label="Filter by modality"
              value={modalityFilter}
              onChange={(e) => setModalityFilter(e.target.value)}
              style={{ maxWidth: 200 }}
            >
              <option value="">All modalities</option>
              {modalityOptions.map((m) => (
                <option key={m.code} value={m.code}>{m.name}</option>
              ))}
            </select>
            <select
              className="rp-input"
              aria-label="Filter by body part"
              value={bodyPartFilter}
              onChange={(e) => setBodyPartFilter(e.target.value)}
              style={{ maxWidth: 200 }}
            >
              <option value="">All body parts</option>
              {bodyPartOptions.map((b) => (
                <option key={b.code} value={b.code}>{b.name}</option>
              ))}
            </select>
            <input
              type="search"
              className="rp-input rp-search"
              placeholder="Search protocols, templates, rulebooks…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              aria-label="Search protocols"
            />
            {(modalityFilter || bodyPartFilter || query) && (
              <button
                type="button"
                className="subtle"
                onClick={() => { setModalityFilter(''); setBodyPartFilter(''); setQuery(''); }}
              >
                Clear
              </button>
            )}
          </div>

          {loading ? (
            <div className="rp-card-grid" aria-busy="true" aria-live="polite">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="rp-panel" style={{ margin: 0 }}>
                  <Skeleton variant="block" height={140} />
                </div>
              ))}
            </div>
          ) : combos.length === 0 ? (
            <EmptyState
              title="No protocols yet"
              description="Once your library has templates or rulebooks for a modality and body part, the covered combinations show up here."
              action={
                <Link href="/templates" className="primary-ghost" style={{ textDecoration: 'none' }}>
                  Go to Templates
                </Link>
              }
            />
          ) : filtered.length === 0 ? (
            <EmptyState title={filterEmptyTitle} description={filterEmptyDesc} />
          ) : (
            <div className="rp-card-grid rp-stagger" aria-live="polite">
              {filtered.map((c) => <ProtocolCard key={c.key} combo={c} />)}
            </div>
          )}
        </>
      )}
    </Container>
  );
}

function ProtocolCard({ combo }: { combo: Combo }) {
  // Contrast coverage comes from the template variants that exist for this
  // combination — the resolver picks the exact variant when one exists, else
  // falls back to a contrast-agnostic or closest-available template.
  const contrastVariants = [...new Set(combo.templates.map((t) => contrastLabel(t.contrast ?? '')))];

  return (
    <div className="rp-card" style={{ cursor: 'default' }}>
      <div className="rp-card-head">
        <div style={{ minWidth: 0 }}>
          <h2 className="rp-card-title">
            {combo.modalityName} · {combo.bodyPartName}
          </h2>
          <code className="rp-card-id">
            {combo.modalityCode} / {combo.bodyPartCode}
          </code>
        </div>
      </div>

      {contrastVariants.length > 0 && (
        <div className="rp-chip-row">
          {contrastVariants.map((v) => (
            <span key={v} className="rp-chip">{v}</span>
          ))}
        </div>
      )}

      <div className="section-block" style={{ marginBottom: 0 }}>
        <div className="rp-card-meta" style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <FileText size={12} strokeWidth={1.8} aria-hidden />
          Templates
        </div>
        {combo.templates.length === 0 ? (
          <p className="rp-card-meta">No template yet — reports fall back to a blank scaffold.</p>
        ) : (
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 4 }}>
            {combo.templates.map((t) => {
              const st = templateStatus(t);
              return (
                <li key={t.id} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                  <Link href="/templates" className="text-accent hover:underline text-[13px]">
                    {t.name || t.templateId}
                  </Link>
                  <span className="rp-card-meta">{contrastLabel(t.contrast ?? '')}</span>
                  {!isApprovedTemplate(t) && <span className={`badge ${st.badge}`}>{st.label}</span>}
                </li>
              );
            })}
          </ul>
        )}
      </div>

      <div className="section-block" style={{ marginBottom: 0 }}>
        <div className="rp-card-meta" style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <BookOpen size={12} strokeWidth={1.8} aria-hidden />
          Rulebooks
        </div>
        {combo.rulebooks.length === 0 ? (
          <p className="rp-card-meta">No rulebook yet — AI drafting and checks run without protocol rules.</p>
        ) : (
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 4 }}>
            {combo.rulebooks.map((r) => (
              <li key={r.id} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                <Link href={rulebookHref(r.id)} className="text-accent hover:underline text-[13px]">
                  {r.name || r.rulebookId}
                </Link>
                <span className="rp-card-meta">v{r.version}</span>
                <span className={`badge ${rulebookStatusBadge(r.status)}`}>{rulebookStatusLabel(r.status)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
