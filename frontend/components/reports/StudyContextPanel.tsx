'use client';

// RC-01 study context panel — the collapsible left rail of the report
// composer. Hosts the study metadata card (the editable modality / body part /
// contrast / age / gender selection key plus the template & rulebook binding
// pickers relocated from the old inspector Context tab) and the case queue.
// Rows render only for data that exists; priors surface as a blue link that
// opens the priors comparison tray.
import type { Report, ReportTemplate, Rulebook, CatalogItem } from '@/lib/api';
import { ChevronsLeft, ChevronsRight, FileText, Link2 } from 'lucide-react';
import SearchableSelect from '@/components/ui/SearchableSelect';
import Skeleton from '@/components/ui/Skeleton';
import CaseQueue from './CaseQueue';

export interface StudyContextPanelProps {
  report: Report;
  collapsed: boolean;
  onToggleCollapse: () => void;

  // Selection-key + binding editing (existing behaviour, relocated).
  modalities: CatalogItem[];
  bodyParts: CatalogItem[];
  templates: ReportTemplate[];
  rulebooks: Rulebook[];
  onStudyChange: (patch: { modality?: string; bodyPart?: string; contrast?: string; age?: number | null; gender?: string }) => void;
  onTemplateChange: (templateId: string) => void;
  onTemplateReset: () => void;
  onRulebookChange: (rulebookId: string) => void;
  onRulebookReset: () => void;
  canEdit: boolean;
  primarySigned: boolean;

  /** null = still loading; true/false = prior lookup result. */
  priorAvailable: boolean | null;
  onOpenPriors: () => void;
}

// Shared contrast token → user-facing label.
function contrastLabel(token: string): string {
  switch (token) {
    case 'None': return 'Without contrast';
    case 'With': return 'With contrast';
    case 'WithAndWithout': return 'With and without contrast';
    default: return 'Any contrast';
  }
}

export default function StudyContextPanel(p: StudyContextPanelProps) {
  if (p.collapsed) {
    return (
      <aside className="rp-studyctx is-collapsed" aria-label="Study context (collapsed)">
        <button
          className="icon-btn rp-studyctx-expand"
          type="button"
          onClick={p.onToggleCollapse}
          aria-label="Expand study context"
          title="Expand study context"
        >
          <ChevronsRight size={15} aria-hidden />
        </button>
        <span className="rp-studyctx-vertical" aria-hidden>Study context</span>
      </aside>
    );
  }

  return (
    <aside className="rp-studyctx" aria-label="Study context">
      <div className="rp-studyctx-head">
        <span className="rp-studyctx-title">Study context</span>
        <button
          className="icon-btn"
          type="button"
          onClick={p.onToggleCollapse}
          aria-label="Collapse study context"
          title="Collapse study context"
        >
          <ChevronsLeft size={15} aria-hidden />
        </button>
      </div>

      <MetadataCard {...p} />
      <CaseQueue currentId={p.report.id} />
    </aside>
  );
}

function MetadataCard(p: StudyContextPanelProps) {
  const study = p.report.study;
  const matchedTemplate = p.templates.find((t) => t.id === p.report.templateId);
  const matchedRulebook = p.rulebooks.find((r) => r.id === p.report.rulebookId);

  // One flat, searchable list per binding (the locked SearchableSelect
  // pattern): options matching the current study context sort first, but
  // nothing hides behind a toggle.
  const eq = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();
  const csvHas = (csv: string, value: string) => csv.split(',').some((part) => eq(part, value));

  const templateMatchesStudy = (t: ReportTemplate) =>
    eq(t.modality, study.modality) && eq(t.bodyPart, study.bodyPart);
  const templateOptions = p.templates
    .filter((t) => t.status === undefined || t.status === 1) // Approved only
    .sort((a, b) => {
      const am = templateMatchesStudy(a) ? 0 : 1;
      const bm = templateMatchesStudy(b) ? 0 : 1;
      return am !== bm ? am - bm : a.name.localeCompare(b.name);
    });
  if (matchedTemplate && !templateOptions.some((t) => t.id === matchedTemplate.id)) {
    templateOptions.unshift(matchedTemplate);
  }

  const rulebookMatchesStudy = (r: Rulebook) =>
    csvHas(r.appliesToModalities, study.modality) && csvHas(r.appliesToBodyParts, study.bodyPart);
  const rulebookOptions = [...p.rulebooks].sort((a, b) => {
    const am = rulebookMatchesStudy(a) ? 0 : 1;
    const bm = rulebookMatchesStudy(b) ? 0 : 1;
    return am !== bm ? am - bm : a.name.localeCompare(b.name);
  });
  if (matchedRulebook && !rulebookOptions.some((r) => r.id === matchedRulebook.id)) {
    rulebookOptions.unshift(matchedRulebook);
  }

  // Contrast-tier hint — which resolution tier the bound template came from.
  const tplContrast = matchedTemplate?.contrast ?? '';
  const studyContrast = study.contrast || '';
  const contrastHint = !matchedTemplate ? null
    : !tplContrast
      ? { text: 'Contrast-agnostic template', warn: false }
      : eq(tplContrast, studyContrast)
        ? { text: `Exact contrast match — ${contrastLabel(tplContrast)}`, warn: false }
        : {
            text: `Closest available — template is “${contrastLabel(tplContrast)}”, study is ${studyContrast ? `“${contrastLabel(studyContrast)}”` : 'unspecified'}`,
            warn: true,
          };

  const bindingsLocked = p.primarySigned || !p.canEdit;

  // Keep a PACS/legacy value selectable even if it's no longer in the catalog.
  const modalityOptions = study.modality && !p.modalities.some((m) => m.code === study.modality)
    ? [{ id: `_${study.modality}`, code: study.modality, name: study.modality, active: true, sortOrder: -1 } as CatalogItem, ...p.modalities]
    : p.modalities;
  const bodyPartOptions = study.bodyPart && !p.bodyParts.some((b) => b.code === study.bodyPart)
    ? [{ id: `_${study.bodyPart}`, code: study.bodyPart, name: study.bodyPart, active: true, sortOrder: -1 } as CatalogItem, ...p.bodyParts]
    : p.bodyParts;

  return (
    <div className="rp-studyctx-card">
      <div className="rp-studyctx-card-title">
        <FileText size={13} aria-hidden /> Study metadata
      </div>

      {study.accessionNumber && (
        <div className="rp-studyctx-row">
          <span className="rp-studyctx-row-label">Accession / Study ID</span>
          <code className="rp-studyctx-row-code">{study.accessionNumber}</code>
        </div>
      )}

      <div className="section-block">
        <label>Modality</label>
        <select
          className="rp-input"
          aria-label="Modality"
          value={study.modality}
          onChange={(e) => p.onStudyChange({ modality: e.target.value })}
        >
          <option value="">— select —</option>
          {modalityOptions.map((m) => (
            <option key={m.id} value={m.code}>{m.name || m.code}</option>
          ))}
        </select>
      </div>
      <div className="section-block">
        <label>Body part</label>
        <select
          className="rp-input"
          aria-label="Body part"
          value={study.bodyPart}
          onChange={(e) => p.onStudyChange({ bodyPart: e.target.value })}
        >
          <option value="">— select —</option>
          {bodyPartOptions.map((b) => (
            <option key={b.id} value={b.code}>{b.name || b.code}</option>
          ))}
        </select>
      </div>
      <div className="section-block">
        <label>Contrast</label>
        <select
          className="rp-input"
          aria-label="Contrast"
          value={study.contrast || ''}
          onChange={(e) => p.onStudyChange({ contrast: e.target.value })}
        >
          <option value="">— select —</option>
          <option value="None">Without contrast</option>
          <option value="With">With contrast</option>
          <option value="WithAndWithout">With and without contrast</option>
        </select>
      </div>
      <div className="rp-row rp-gap-sm">
        <div className="section-block" style={{ flex: 1 }}>
          <label>Age</label>
          <input
            className="rp-input"
            type="number"
            min={0}
            max={150}
            aria-label="Patient age"
            value={study.age ?? ''}
            onChange={(e) => {
              const v = e.target.value;
              p.onStudyChange({ age: v === '' ? null : Number(v) });
            }}
          />
        </div>
        <div className="section-block" style={{ flex: 1 }}>
          <label>Gender</label>
          <select
            className="rp-input"
            aria-label="Patient gender"
            value={study.gender || ''}
            onChange={(e) => p.onStudyChange({ gender: e.target.value })}
          >
            <option value="">— select —</option>
            <option value="Male">Male</option>
            <option value="Female">Female</option>
            <option value="Other">Other</option>
            <option value="Unknown">Unknown</option>
          </select>
        </div>
      </div>

      {(study.comparison || '').trim() && (
        <div className="rp-studyctx-row">
          <span className="rp-studyctx-row-label">Comparison</span>
          <span className="rp-studyctx-row-value">{study.comparison}</span>
        </div>
      )}

      <div className="rp-studyctx-row">
        <span className="rp-studyctx-row-label">Prior study status</span>
        {p.priorAvailable === null ? (
          <Skeleton variant="text" width={90} />
        ) : p.priorAvailable ? (
          <button type="button" className="rp-studyctx-link" onClick={p.onOpenPriors}>
            <Link2 size={11} aria-hidden /> 1 prior available
          </button>
        ) : (
          <span className="rp-studyctx-row-value">No priors found</span>
        )}
      </div>

      <div className="rp-studyctx-divider" aria-hidden />

      <div className="section-block">
        <div className="rp-row between">
          <label htmlFor="rp-ctx-template">Matched template</label>
          <span className={`badge ${p.report.templatePinned ? 'info' : ''}`}>
            {p.report.templatePinned ? 'Manual' : 'Auto'}
          </span>
        </div>
        <SearchableSelect
          id="rp-ctx-template"
          ariaLabel="Matched template"
          value={p.report.templateId}
          disabled={bindingsLocked}
          placeholder="— none —"
          searchPlaceholder="Search templates…"
          options={[
            ...(p.report.templateId && !matchedTemplate
              ? [{ value: p.report.templateId, label: '(unavailable template)' }]
              : []),
            ...templateOptions.map((t) => ({
              value: t.id,
              label: t.name,
              searchText: `${t.modality} ${t.bodyPart} ${contrastLabel(t.contrast ?? '')} ${t.subspecialty}`,
            })),
          ]}
          onChange={(v) => { if (v) p.onTemplateChange(v); }}
        />
        {contrastHint && (
          <p className="rp-page-sub" style={{ marginTop: 4 }}>
            {contrastHint.warn && <span className="badge warn">Closest</span>} {contrastHint.text}
          </p>
        )}
        {p.report.templatePinned && !bindingsLocked && (
          <button className="ghost" type="button" style={{ marginTop: 4 }} onClick={p.onTemplateReset}>
            Reset to auto
          </button>
        )}
      </div>

      <div className="section-block">
        <div className="rp-row between">
          <label htmlFor="rp-ctx-rulebook">Matched rulebook (prompts)</label>
          <span className={`badge ${p.report.rulebookPinned ? 'info' : ''}`}>
            {p.report.rulebookPinned ? 'Manual' : 'Auto'}
          </span>
        </div>
        <SearchableSelect
          id="rp-ctx-rulebook"
          ariaLabel="Matched rulebook"
          value={p.report.rulebookId}
          disabled={bindingsLocked}
          placeholder="— none —"
          searchPlaceholder="Search rulebooks…"
          options={[
            ...(p.report.rulebookId && !matchedRulebook
              ? [{ value: p.report.rulebookId, label: '(unavailable rulebook)' }]
              : []),
            ...rulebookOptions.map((r) => ({
              value: r.id,
              label: `${r.name} · v${r.version}`,
              searchText: `${r.appliesToModalities} ${r.appliesToBodyParts}`,
            })),
          ]}
          onChange={(v) => { if (v) p.onRulebookChange(v); }}
        />
        {p.report.rulebookPinned && !bindingsLocked && (
          <button className="ghost" type="button" style={{ marginTop: 4 }} onClick={p.onRulebookReset}>
            Reset to auto
          </button>
        )}
      </div>
    </div>
  );
}
