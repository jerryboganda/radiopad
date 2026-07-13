'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Check, Copy, Search } from 'lucide-react';
import { api, type ReportTemplate } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { useToast } from '@/components/ui/ToastProvider';

// A template section as stored in ReportTemplate.sectionsJson.
type TemplateSection = {
  id: string;
  label: string;
  placeholder?: string;
  required?: boolean;
};

// One reusable phrase extracted from a template section.
type Phrase = {
  key: string;
  sectionLabel: string;
  text: string;
};

// A template together with the phrases extracted from its sections.
type PhraseGroup = {
  template: ReportTemplate;
  phrases: Phrase[];
};

const TRUNCATE_AT = 180;

function parseSections(t: ReportTemplate): TemplateSection[] {
  try {
    const parsed = JSON.parse(t.sectionsJson) as
      | { sections?: TemplateSection[] }
      | TemplateSection[];
    const sections = Array.isArray(parsed) ? parsed : (parsed.sections ?? []);
    return Array.isArray(sections) ? sections : [];
  } catch {
    return [];
  }
}

function toGroups(templates: ReportTemplate[]): PhraseGroup[] {
  return templates
    .map((template) => ({
      template,
      phrases: parseSections(template)
        .filter((s) => (s.placeholder ?? '').trim().length > 0)
        .map((s) => ({
          key: `${template.id}:${s.id}`,
          sectionLabel: s.label || s.id,
          text: (s.placeholder ?? '').trim(),
        })),
    }))
    .filter((g) => g.phrases.length > 0)
    .sort((a, b) => a.template.name.localeCompare(b.template.name));
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

export default function FindingsLibraryPage() {
  const [templates, setTemplates] = useState<ReportTemplate[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState('');
  const [modality, setModality] = useState('');

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    api.templates
      .list()
      .then(setTemplates)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const groups = useMemo(() => (templates ? toGroups(templates) : []), [templates]);

  const modalityOptions = useMemo(
    () => [...new Set(groups.map((g) => g.template.modality).filter(Boolean))].sort(),
    [groups],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return groups
      .filter((g) => !modality || g.template.modality === modality)
      .map((g) => {
        if (!q) return g;
        const templateHit =
          g.template.name.toLowerCase().includes(q) ||
          g.template.bodyPart.toLowerCase().includes(q) ||
          g.template.modality.toLowerCase().includes(q);
        if (templateHit) return g;
        const phrases = g.phrases.filter(
          (p) => p.text.toLowerCase().includes(q) || p.sectionLabel.toLowerCase().includes(q),
        );
        return { ...g, phrases };
      })
      .filter((g) => g.phrases.length > 0);
  }, [groups, query, modality]);

  const phraseCount = useMemo(
    () => filtered.reduce((n, g) => n + g.phrases.length, 0),
    [filtered],
  );

  const hasFilters = query.trim().length > 0 || modality !== '';

  return (
    <Container>
      <PageHeader
        title="Findings Library"
        description="Reusable phrasing from your report templates — search it, copy it, and paste it straight into a report."
      />

      {error && !templates && (
        <ErrorState
          title="Couldn't load the findings library"
          message={error}
          onRetry={load}
        />
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

      {templates && (
        <>
          <div className="rp-filter-bar">
            <div className="rp-search" style={{ position: 'relative', flex: '1 1 260px' }}>
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
                aria-label="Search findings phrases"
              />
            </div>
            <select
              className="rp-input"
              aria-label="Filter by modality"
              value={modality}
              onChange={(e) => setModality(e.target.value)}
              style={{ maxWidth: 200 }}
            >
              <option value="">All modalities</option>
              {modalityOptions.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
            {hasFilters && (
              <button
                type="button"
                className="subtle"
                onClick={() => {
                  setQuery('');
                  setModality('');
                }}
              >
                Clear
              </button>
            )}
            <span className="rp-page-sub" style={{ marginLeft: 'auto' }}>
              {phraseCount} {phraseCount === 1 ? 'phrase' : 'phrases'}
            </span>
          </div>

          {groups.length === 0 ? (
            <EmptyState
              title="No reusable phrases yet"
              description="Phrases come from the section text in your report templates. Add section text to a template and it will show up here."
            />
          ) : filtered.length === 0 ? (
            <EmptyState
              title="No phrases match"
              description="Try a different search term, or clear the modality filter."
              action={
                <button
                  type="button"
                  className="primary-ghost"
                  onClick={() => {
                    setQuery('');
                    setModality('');
                  }}
                >
                  Clear filters
                </button>
              }
            />
          ) : (
            <div
              className="rp-stagger"
              style={{ display: 'flex', flexDirection: 'column', gap: 16 }}
              aria-live="polite"
            >
              {filtered.map((g) => (
                <TemplateGroupCard key={g.template.id} group={g} />
              ))}
            </div>
          )}
        </>
      )}
    </Container>
  );
}

function TemplateGroupCard({ group }: { group: PhraseGroup }) {
  const t = group.template;
  const contrast = contrastLabel(t.contrast);

  return (
    <div className="rp-card" style={{ cursor: 'default' }}>
      <div className="rp-card-head">
        <div style={{ minWidth: 0 }}>
          <h2 className="rp-card-title">{t.name || t.templateId}</h2>
          <code className="rp-card-id">{t.templateId}</code>
        </div>
      </div>

      <div className="rp-chip-row" style={{ marginBottom: 10 }}>
        {t.modality && <span className="rp-chip">{t.modality}</span>}
        {t.bodyPart && <span className="rp-chip">{t.bodyPart}</span>}
        {contrast && <span className="rp-chip">{contrast}</span>}
        {t.subspecialty && <span className="rp-chip">{t.subspecialty}</span>}
      </div>

      <ul
        style={{
          margin: 0,
          padding: 0,
          listStyle: 'none',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {group.phrases.map((p) => (
          <PhraseRow key={p.key} phrase={p} />
        ))}
      </ul>
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
    <li
      className="border-b border-rule last:border-b-0"
      style={{ display: 'flex', gap: 12, alignItems: 'flex-start', padding: '10px 0' }}
    >
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="rp-card-meta" style={{ marginBottom: 4 }}>
          {phrase.sectionLabel}
        </div>
        <p
          className="text-ink text-[13px]"
          style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'break-word' }}
        >
          {shown}
        </p>
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
      <button
        type="button"
        className="ghost"
        onClick={copyPhrase}
        aria-label={`Copy the ${phrase.sectionLabel} phrase`}
        style={{ display: 'inline-flex', alignItems: 'center', gap: 6, flexShrink: 0 }}
      >
        {copied ? (
          <Check size={14} strokeWidth={1.8} aria-hidden />
        ) : (
          <Copy size={14} strokeWidth={1.8} aria-hidden />
        )}
        {copied ? 'Copied' : 'Copy'}
      </button>
    </li>
  );
}
