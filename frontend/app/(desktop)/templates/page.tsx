'use client';

import { useCallback, useEffect, useState } from 'react';
import { api, type ReportTemplate } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';

type Section = { id: string; label: string; placeholder?: string; required?: boolean };

type ExtTemplate = ReportTemplate & {
  status?: number | string;
  variant?: number | string;
  approvedAt?: string | null;
  approvedBy?: string | null;
};

type Draft = {
  id?: string;
  templateId: string;
  name: string;
  modality: string;
  bodyPart: string;
  contrast: string;
  subspecialty: string;
  sections: Section[];
};

type PreviewState = Awaited<ReturnType<typeof api.templates.preview>> | null;

type UsageState = {
  templateId: string;
  counts: { last7d: number; last30d: number; last90d: number };
  byUser: Array<{ userId: string; count: number }>;
  byModality: Array<{ modality: string; count: number }>;
} | null;

const EMPTY: Draft = {
  templateId: '',
  name: '',
  modality: 'CT',
  bodyPart: '',
  contrast: '',
  subspecialty: '',
  sections: [
    { id: 'indication', label: 'Indication' },
    { id: 'technique', label: 'Technique' },
    { id: 'comparison', label: 'Comparison' },
    { id: 'findings', label: 'Findings', required: true },
    { id: 'impression', label: 'Impression', required: true },
  ],
};

export default function TemplatesPage() {
  const [items, setItems] = useState<ExtTemplate[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [saving, setSaving] = useState(false);
  const [preview, setPreview] = useState<PreviewState>(null);
  const [usage, setUsage] = useState<UsageState>(null);

  async function refresh() {
    setItems((await api.templates.list()) as ExtTemplate[]);
  }

  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    api.templates.list()
      .then((r) => setItems(r as ExtTemplate[]))
      .catch((e: Error) => setLoadError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { load(); }, [load]);

  function newTemplate() {
    setDraft({ ...EMPTY, sections: EMPTY.sections.map((s) => ({ ...s })) });
  }

  function editTemplate(t: ExtTemplate) {
    let sections: Section[] = [];
    try {
      const parsed = JSON.parse(t.sectionsJson) as { sections?: Section[] } | Section[];
      sections = Array.isArray(parsed) ? parsed : parsed.sections ?? [];
    } catch {
      sections = [];
    }
    setDraft({
      id: t.id,
      templateId: t.templateId,
      name: t.name,
      modality: t.modality,
      bodyPart: t.bodyPart,
      contrast: t.contrast ?? '',
      subspecialty: t.subspecialty ?? '',
      sections,
    });
  }

  async function save() {
    if (!draft) return;
    setSaving(true);
    try {
      await api.templates.save({
        templateId: draft.templateId,
        name: draft.name,
        modality: draft.modality,
        bodyPart: draft.bodyPart,
        contrast: draft.contrast,
        subspecialty: draft.subspecialty,
        sectionsJson: JSON.stringify({ sections: draft.sections }, null, 2),
      });
      setDraft(null);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  async function withRefresh<T>(fn: () => Promise<T>): Promise<void> {
    setError(null);
    try { await fn(); await refresh(); }
    catch (e) { setError((e as Error).message); }
  }

  async function showPreview(t: ExtTemplate) {
    setError(null);
    try { setPreview(await api.templates.preview(t.id)); }
    catch (e) { setError((e as Error).message); }
  }

  async function showUsage(t: ExtTemplate) {
    setError(null);
    try { setUsage(await api.templates.usage(t.id)); }
    catch (e) { setError((e as Error).message); }
  }

  return (
    <Container>
      <PageHeader
        title="Templates"
        description={<>Pre-built report structures (sections like Indication, Findings, Impression) for each kind of study. Built-in templates ship with the app; your edits stay inside your workspace.</>}
        primaryAction={<button className="primary" onClick={newTemplate}>+ New template</button>}
      />

      <div aria-live="polite">
        {error && <Banner tone="warn" title="Action failed" onDismiss={() => setError(null)}>{error}</Banner>}
      </div>

      {loadError ? (
        <ErrorState title="Couldn't load templates" message={loadError} onRetry={load} />
      ) : loading ? (
        <div className="rp-panel" aria-busy="true">
          <div className="rp-panel-title">Your templates</div>
          <TableSkeleton rows={5} cols={6} />
        </div>
      ) : items.length === 0 ? (
        <EmptyState
          title="No custom templates yet"
          description="Create a template to define the section structure for a study type. Built-in templates ship with the app."
          action={<button className="primary" onClick={newTemplate}>Create your first template</button>}
        />
      ) : (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Your templates</div>
          <table className="rp-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Name</th>
                <th>Modality</th>
                <th>Body part</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((t) => {
                const status = templateStatusLabel(t.status);
                return (
                  <tr key={t.id}>
                    <td><code>{t.templateId}</code></td>
                    <td>{t.name}</td>
                    <td>{t.modality}</td>
                    <td>{t.bodyPart}</td>
                    <td><span className={`badge ${templateStatusClass(t.status)}`}>{status}</span></td>
                    <td>
                      <button className="subtle" onClick={() => editTemplate(t)}>Edit</button>
                      <button className="subtle" onClick={() => showPreview(t)}>Preview</button>
                      <button className="subtle" onClick={() => showUsage(t)}>Usage</button>
                      {status !== 'Approved' && (
                        <button className="primary-ghost" onClick={() => withRefresh(() => api.templates.submitForReview(t.id))}>Submit</button>
                      )}
                      {status !== 'Approved' && (
                        <button className="primary-ghost" onClick={() => withRefresh(() => api.templates.approve(t.id))}>Approve</button>
                      )}
                      {status !== 'Deprecated' && (
                        <button className="ghost" onClick={() => withRefresh(() => api.templates.deprecate(t.id))}>Deprecate</button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {preview && (
        <div className="rp-modal-backdrop" onClick={() => setPreview(null)} onKeyDown={(e) => { if (e.key === 'Escape') setPreview(null); }}>
          <div className="rp-panel rp-modal rp-anim-scale-in" role="dialog" aria-modal="true" aria-label="Template preview" onClick={(e) => e.stopPropagation()} style={{ width: 'min(720px, 100%)' }}>
            <div className="rp-row between">
              <div className="rp-panel-title">Preview — {preview.name} <code>{preview.templateId}</code></div>
              <button className="ghost" onClick={() => setPreview(null)}>Close</button>
            </div>
            <p className="rp-page-sub">Variant: {preview.variant} · Status: {preview.status}</p>
            <div className="rp-section-list">
              {preview.sections.length === 0 ? (
                <em style={{ color: 'var(--text-faint)' }}>This template has no sections.</em>
              ) : preview.sections.map((s) => (
                <div key={s.key} className="rp-field">
                  <span>{s.label}</span>
                  <div style={{ font: '14px/1.6 var(--serif)', color: 'var(--text)' }}>{s.body}</div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {usage && (
        <div className="rp-modal-backdrop" onClick={() => setUsage(null)} onKeyDown={(e) => { if (e.key === 'Escape') setUsage(null); }}>
          <div className="rp-panel rp-modal rp-anim-scale-in" role="dialog" aria-modal="true" aria-label="Template usage" onClick={(e) => e.stopPropagation()} style={{ width: 'min(560px, 100%)' }}>
          <div className="rp-row between">
            <div className="rp-panel-title">Usage — <code>{usage.templateId}</code></div>
            <button className="ghost" onClick={() => setUsage(null)}>Close</button>
          </div>
          <div className="rp-row" style={{ gap: 24 }}>
            <div className="rp-field"><span>Last 7d</span><strong>{usage.counts.last7d}</strong></div>
            <div className="rp-field"><span>Last 30d</span><strong>{usage.counts.last30d}</strong></div>
            <div className="rp-field"><span>Last 90d</span><strong>{usage.counts.last90d}</strong></div>
          </div>
          <div className="rp-field">
            <span>By modality</span>
            <div>{usage.byModality.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>no usage</em>
              : usage.byModality.map((m) => (
                <span key={m.modality} className="badge" style={{ marginRight: 6 }}>{m.modality || '—'}: {m.count}</span>
              ))}</div>
          </div>
          <div className="rp-field">
            <span>By user</span>
            <div>{usage.byUser.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>no usage</em>
              : usage.byUser.map((u) => (
                <span key={u.userId} className="badge" style={{ marginRight: 6 }}><code>{u.userId.slice(0, 8)}</code>: {u.count}</span>
              ))}</div>
          </div>
          </div>
        </div>
      )}

      {draft && (
        <div className="rp-modal-backdrop" onClick={() => setDraft(null)} onKeyDown={(e) => { if (e.key === 'Escape') setDraft(null); }}>
          <div className="rp-panel rp-modal rp-anim-scale-in" role="dialog" aria-modal="true" aria-label={draft.id ? 'Edit template' : 'New template'} onClick={(e) => e.stopPropagation()} style={{ width: 'min(720px, 100%)' }}>
            <div className="rp-panel-title">{draft.id ? 'Edit template' : 'New template'}</div>

            <label className="rp-field">
              <span>Template id (stable, snake_case)</span>
              <input
                className="rp-input"
                value={draft.templateId}
                onChange={(e) => setDraft({ ...draft, templateId: e.target.value })}
                disabled={!!draft.id}
                placeholder="chest-ct-v2"
              />
            </label>

            <label className="rp-field">
              <span>Name</span>
              <input className="rp-input" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
            </label>

            <div className="rp-row" style={{ gap: 12 }}>
              <label className="rp-field" style={{ flex: 1 }}>
                <span>Modality</span>
                <select className="rp-input" value={draft.modality} onChange={(e) => setDraft({ ...draft, modality: e.target.value })}>
                  {['CT', 'MRI', 'XR', 'US', 'NM', 'PET', 'MG'].map((m) => <option key={m}>{m}</option>)}
                </select>
              </label>
              <label className="rp-field" style={{ flex: 1 }}>
                <span>Body part</span>
                <input className="rp-input" value={draft.bodyPart} onChange={(e) => setDraft({ ...draft, bodyPart: e.target.value })} />
              </label>
              <label className="rp-field" style={{ flex: 1 }}>
                <span>Contrast</span>
                <select className="rp-input" value={draft.contrast} onChange={(e) => setDraft({ ...draft, contrast: e.target.value })}>
                  <option value="">Any / agnostic</option>
                  <option value="None">Without contrast</option>
                  <option value="With">With contrast</option>
                  <option value="WithAndWithout">With and without</option>
                </select>
              </label>
              <label className="rp-field" style={{ flex: 1 }}>
                <span>Subspecialty</span>
                <input className="rp-input" value={draft.subspecialty} onChange={(e) => setDraft({ ...draft, subspecialty: e.target.value })} />
              </label>
            </div>

            <div className="rp-panel-title" style={{ marginTop: 12 }}>Sections</div>
            <div className="rp-section-list">
              {draft.sections.map((s, i) => (
                <div key={i} className="rp-row" style={{ gap: 8, marginBottom: 6, alignItems: 'center' }}>
                  <input
                    className="rp-input"
                    placeholder="id"
                    value={s.id}
                    onChange={(e) => mutate(setDraft, draft, i, { id: e.target.value })}
                    style={{ width: 140 }}
                  />
                  <input
                    className="rp-input"
                    placeholder="label"
                    value={s.label}
                    onChange={(e) => mutate(setDraft, draft, i, { label: e.target.value })}
                    style={{ width: 180 }}
                  />
                  <input
                    className="rp-input"
                    placeholder="placeholder"
                    value={s.placeholder ?? ''}
                    onChange={(e) => mutate(setDraft, draft, i, { placeholder: e.target.value })}
                    style={{ flex: 1 }}
                  />
                  <label className="rp-row" style={{ gap: 4, alignItems: 'center' }}>
                    <input
                      type="checkbox"
                      checked={!!s.required}
                      onChange={(e) => mutate(setDraft, draft, i, { required: e.target.checked })}
                    />
                    <span style={{ font: '500 12px var(--sans)', color: 'var(--text-muted)' }}>req</span>
                  </label>
                  <button
                    className="subtle"
                    onClick={() =>
                      setDraft({ ...draft, sections: draft.sections.filter((_, j) => j !== i) })
                    }
                  >×</button>
                </div>
              ))}
              <button
                className="ghost"
                onClick={() =>
                  setDraft({
                    ...draft,
                    sections: [...draft.sections, { id: `section_${draft.sections.length + 1}`, label: 'New section' }],
                  })
                }
              >+ Add section</button>
            </div>

            <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
              <button className="ghost" onClick={() => setDraft(null)} disabled={saving}>Cancel</button>
              <button className="primary" onClick={save} disabled={saving || !draft.templateId || !draft.name} aria-busy={saving}>
                {saving && <span className="rp-spinner sm" aria-hidden />}
                {saving ? 'Saving…' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </Container>
  );
}

function templateStatusLabel(s: number | string | undefined): string {
  if (typeof s === 'string') return s;
  if (typeof s === 'number') return ['Draft', 'Approved', 'Deprecated', 'Review'][s] ?? 'Draft';
  return 'Draft';
}

function templateStatusClass(s: number | string | undefined): string {
  const label = templateStatusLabel(s);
  if (label === 'Approved') return 'ok';
  if (label === 'Deprecated') return 'danger';
  if (label === 'Review') return 'warn';
  return '';
}

function mutate(
  setDraft: (d: Draft | null) => void,
  draft: Draft,
  i: number,
  patch: Partial<Section>,
) {
  const next = draft.sections.slice();
  next[i] = { ...next[i], ...patch };
  setDraft({ ...draft, sections: next });
}
