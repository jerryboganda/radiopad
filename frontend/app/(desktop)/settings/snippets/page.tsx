'use client';

// F3 — Snippets (autotext) manager. Device-local shortcuts that expand into canned report prose,
// optionally with ${field} fill-in placeholders the radiologist tabs through after inserting. Stored
// in localStorage (never PHI), like Hotkeys. Consumed by the editor's snippet insertion.

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Plus, Pencil, Trash2, Check, X, Type } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import {
  getSnippets,
  saveSnippet,
  deleteSnippet,
  findFields,
  SNIPPETS_CHANGE_EVENT,
  type Snippet,
} from '@/lib/snippets';
import { invalidateSharedMacros, loadSharedMacros } from '@/lib/sharedMacros';
import { api, type SharedMacro } from '@/lib/api';
import { usePermissions } from '@/lib/permissions';

export default function SnippetsPage() {
  const [rows, setRows] = useState<Snippet[]>([]);
  const [trigger, setTrigger] = useState('');
  const [body, setBody] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const refresh = useCallback(() => setRows(getSnippets().slice()), []);

  useEffect(() => {
    refresh();
    window.addEventListener(SNIPPETS_CHANGE_EVENT, refresh);
    return () => window.removeEventListener(SNIPPETS_CHANGE_EVENT, refresh);
  }, [refresh]);

  const sorted = [...rows].sort((a, b) => a.trigger.localeCompare(b.trigger, undefined, { sensitivity: 'base' }));

  function resetForm() {
    setTrigger('');
    setBody('');
    setEditingId(null);
    setFormError(null);
  }

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const t = trigger.trim();
    if (!t || body.trim().length === 0) {
      setFormError('Enter both a trigger and the text it expands to.');
      return;
    }
    // Expansion matches the single whitespace-delimited word before the caret, so a trigger with a
    // space in it can never be typed into existence. Refuse it here rather than storing a snippet
    // that looks saved and silently never fires.
    if (/\s/.test(t)) {
      setFormError('A trigger must be a single word — it is matched against the word you just typed.');
      return;
    }
    // Adding a trigger that already exists would overwrite the snippet stored under it. This used
    // to set the message and then save anyway, and resetForm() cleared the message on the way out —
    // so the body was replaced with no warning shown at all. Stop instead.
    const clash = rows.find((s) => s.id !== editingId && s.trigger.toLowerCase() === t.toLowerCase());
    if (clash && !editingId) {
      setFormError(`A snippet for “${clash.trigger}” already exists. Edit that one, or pick another trigger.`);
      return;
    }
    saveSnippet({ id: editingId ?? undefined, trigger: t, body });
    resetForm();
    refresh();
  }

  function beginEdit(s: Snippet) {
    setEditingId(s.id);
    setTrigger(s.trigger);
    setBody(s.body);
    setFormError(null);
  }

  function remove(s: Snippet) {
    deleteSnippet(s.id);
    if (editingId === s.id) resetForm();
    refresh();
  }

  return (
    <Container>
      <Link
        href="/settings"
        className="ghost"
        style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6, marginBottom: 12 }}
      >
        <ArrowLeft size={15} strokeWidth={1.8} aria-hidden />
        Settings
      </Link>

      <PageHeader
        title="Snippets"
        description="Type a short trigger to expand it into canned report text. Add ${field} placeholders to tab through fill-in blanks after inserting."
      />

      {/* ── Add / edit ─────────────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">
          <Type size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
          {editingId ? 'Edit snippet' : 'Add a snippet'}
        </div>
        <form onSubmit={submit}>
          <div className="section-block" style={{ marginBottom: 12 }}>
            <label htmlFor="snip-trigger">Trigger</label>
            <input
              id="snip-trigger"
              className="rp-input"
              value={trigger}
              onChange={(e) => {
                setTrigger(e.target.value);
                setFormError(null);
              }}
              placeholder="nlchest"
              autoComplete="off"
              spellCheck={false}
              style={{ maxWidth: 240 }}
            />
          </div>
          <div className="section-block" style={{ marginBottom: 12 }}>
            <label htmlFor="snip-body">Expands to</label>
            <textarea
              id="snip-body"
              className="rp-input"
              value={body}
              onChange={(e) => {
                setBody(e.target.value);
                setFormError(null);
              }}
              placeholder={'The ${vessel} is patent. No ${finding} identified.'}
              rows={4}
              spellCheck={false}
              style={{ width: '100%', resize: 'vertical' }}
            />
            <p className="rp-page-sub" style={{ marginTop: 6 }}>
              Use <code>${'{name}'}</code> for a fill-in blank you tab through after inserting.
            </p>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="submit" className="primary" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              {editingId ? <Check size={15} strokeWidth={2} aria-hidden /> : <Plus size={15} strokeWidth={2} aria-hidden />}
              {editingId ? 'Save changes' : 'Add snippet'}
            </button>
            {editingId && (
              <button type="button" className="ghost" onClick={resetForm}>
                Cancel
              </button>
            )}
          </div>
          {formError && (
            <p className="text-warning" role="alert" style={{ marginTop: 10, fontSize: 13 }}>
              {formError}
            </p>
          )}
        </form>
      </div>

      {/* ── List ───────────────────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">Your snippets</div>
        {sorted.length === 0 ? (
          <EmptyState
            title="No snippets yet"
            description="Add one above. Type its trigger in a report to expand it."
          />
        ) : (
          <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
            {sorted.map((s) => {
              const fieldCount = findFields(s.body).length;
              return (
                <li key={s.id} className="rp-card" style={{ display: 'flex', alignItems: 'flex-start', gap: 12, padding: '10px 14px' }}>
                  <code style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{s.trigger}</code>
                  <span style={{ flex: 1, minWidth: 0 }}>
                    <span style={{ display: 'block', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{s.body}</span>
                    {fieldCount > 0 && (
                      <span className="rp-page-sub">
                        {fieldCount} fill-in field{fieldCount === 1 ? '' : 's'}
                      </span>
                    )}
                  </span>
                  <div style={{ display: 'flex', gap: 6 }}>
                    <button type="button" className="ghost" onClick={() => beginEdit(s)} aria-label={`Edit ${s.trigger}`} title="Edit">
                      <Pencil size={15} strokeWidth={1.8} aria-hidden />
                    </button>
                    <button type="button" className="ghost" onClick={() => remove(s)} aria-label={`Delete ${s.trigger}`} title="Delete">
                      <Trash2 size={15} strokeWidth={1.8} aria-hidden className="text-danger" />
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      <SharedMacrosPanel />
    </Container>
  );
}

/* ── RPT-021 — shared macros ──────────────────────────────────────────── */

/**
 * Tenant / subspecialty macros published by the department. Read-only here:
 * everyone can see what they can expand, but authoring lives with the
 * reporting-governance roles (the API enforces it). A personal snippet with
 * the same trigger overrides the shared one, which the list makes explicit.
 */
function SharedMacrosPanel() {
  const [rows, setRows] = useState<SharedMacro[]>([]);
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  // Authoring is a governance action (it changes what everyone expands), so it
  // rides the same permission as report templates. The API enforces it too.
  const { can: allowed } = usePermissions();
  const canAuthor = allowed('templates.manage');
  const [mTrigger, setMTrigger] = useState('');
  const [mBody, setMBody] = useState('');
  const [mDescription, setMDescription] = useState('');
  const [mSubspecialty, setMSubspecialty] = useState('');
  const [mEditingId, setMEditingId] = useState<string | null>(null);
  const [mError, setMError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const personalTriggers = useMemo(
    () => new Set(getSnippets().map((s) => s.trigger.trim().toLowerCase())),
    [],
  );

  const load = useCallback(() => {
    setState('loading');
    invalidateSharedMacros();
    loadSharedMacros()
      .then((list) => {
        setRows(list);
        setState('ready');
      })
      .catch((e: Error) => {
        setError(e.message);
        setState('error');
      });
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  function resetMacroForm() {
    setMEditingId(null);
    setMTrigger('');
    setMBody('');
    setMDescription('');
    setMSubspecialty('');
    setMError(null);
  }

  async function submitMacro(e: React.FormEvent) {
    e.preventDefault();
    const t = mTrigger.trim();
    if (!t) { setMError('Give the macro a trigger.'); return; }
    if (!mBody.trim()) { setMError('Give the macro something to expand into.'); return; }
    setSaving(true);
    setMError(null);
    try {
      await api.macros.save({
        id: mEditingId ?? undefined,
        trigger: t,
        body: mBody,
        description: mDescription,
        scope: mSubspecialty.trim() ? 'Subspecialty' : 'Tenant',
        subspecialty: mSubspecialty.trim(),
      });
      resetMacroForm();
      load();
    } catch (err) {
      setMError((err as Error).message);
    } finally {
      setSaving(false);
    }
  }

  function beginEditMacro(m: SharedMacro) {
    setMEditingId(m.id);
    setMTrigger(m.trigger);
    setMBody(m.body);
    setMDescription(m.description);
    setMSubspecialty(m.scope === 'Subspecialty' ? m.subspecialty : '');
    setMError(null);
  }

  async function removeMacro(m: SharedMacro) {
    setMError(null);
    try {
      await api.macros.delete(m.id);
      if (mEditingId === m.id) resetMacroForm();
      load();
    } catch (err) {
      setMError((err as Error).message);
    }
  }

  return (
    <div className="rp-panel rp-anim-fade-in-up">
      <div className="rp-panel-title">Shared macros</div>
      <p className="rp-page-sub" style={{ marginTop: 4 }}>
        Published for your workspace or your subspecialty. They expand the same way your own
        snippets do — and one of your snippets with the same trigger always wins.
      </p>

      {canAuthor && (
        <form onSubmit={submitMacro} style={{ marginBottom: 16 }}>
          <div className="rp-row" style={{ gap: 12, flexWrap: 'wrap', alignItems: 'flex-end' }}>
            <div className="section-block" style={{ marginBottom: 0 }}>
              <label htmlFor="macro-trigger">Trigger</label>
              <input
                id="macro-trigger"
                className="rp-input"
                value={mTrigger}
                onChange={(e) => setMTrigger(e.target.value)}
                placeholder="nlctchest"
                autoComplete="off"
                spellCheck={false}
                style={{ maxWidth: 200 }}
              />
            </div>
            <div className="section-block" style={{ marginBottom: 0 }}>
              <label htmlFor="macro-subspecialty">Subspecialty (blank = whole workspace)</label>
              <input
                id="macro-subspecialty"
                className="rp-input"
                value={mSubspecialty}
                onChange={(e) => setMSubspecialty(e.target.value)}
                placeholder="Neuro"
                autoComplete="off"
                style={{ maxWidth: 200 }}
              />
            </div>
          </div>
          <div className="section-block" style={{ marginTop: 12 }}>
            <label htmlFor="macro-body">Expands to</label>
            <textarea
              id="macro-body"
              className="rp-input"
              value={mBody}
              onChange={(e) => setMBody(e.target.value)}
              placeholder={'No acute intracranial abnormality. The ${vessel} is patent.'}
              rows={3}
              spellCheck={false}
              style={{ width: '100%', resize: 'vertical' }}
            />
          </div>
          <div className="section-block">
            <label htmlFor="macro-description">Note (optional)</label>
            <input
              id="macro-description"
              className="rp-input"
              value={mDescription}
              onChange={(e) => setMDescription(e.target.value)}
              placeholder="Agreed departmental normal for non-contrast head CT"
              style={{ width: '100%' }}
            />
          </div>
          <div className="rp-row" style={{ gap: 8 }}>
            <button type="submit" className="primary" disabled={saving}>
              {saving && <span className="rp-spinner sm" aria-hidden />}
              {mEditingId ? 'Save macro' : 'Publish macro'}
            </button>
            {mEditingId && (
              <button type="button" className="ghost" onClick={resetMacroForm}>Cancel</button>
            )}
          </div>
          {mError && (
            <p className="text-warning" role="alert" style={{ marginTop: 10, fontSize: 13 }}>{mError}</p>
          )}
        </form>
      )}
      {state === 'loading' ? (
        <TableSkeleton rows={3} cols={2} />
      ) : state === 'error' ? (
        <ErrorState title="Couldn't load shared macros" message={error ?? ''} onRetry={load} />
      ) : rows.length === 0 ? (
        <EmptyState
          title="No shared macros yet"
          description="Your workspace hasn't published any. Ask a reporting admin to add one."
        />
      ) : (
        <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {rows.map((m) => {
            const overridden = personalTriggers.has(m.trigger.trim().toLowerCase());
            return (
              <li key={m.id} className="rp-card" style={{ display: 'flex', alignItems: 'flex-start', gap: 12, padding: '10px 14px' }}>
                <code style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{m.trigger}</code>
                <span style={{ flex: 1, minWidth: 0 }}>
                  <span style={{ display: 'block', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{m.body}</span>
                  {m.description && <span className="rp-page-sub">{m.description}</span>}
                </span>
                <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
                  <span className="badge">
                    {m.scope === 'Subspecialty' ? m.subspecialty || 'Subspecialty' : 'Workspace'}
                  </span>
                  {overridden && (
                    <span className="status-badge" data-tone="review" title="One of your own snippets uses this trigger and takes priority">
                      Overridden by yours
                    </span>
                  )}
                  {canAuthor && (
                    <>
                      <button type="button" className="ghost" onClick={() => beginEditMacro(m)} aria-label={`Edit macro ${m.trigger}`} title="Edit">
                        <Pencil size={15} strokeWidth={1.8} aria-hidden />
                      </button>
                      <button type="button" className="ghost" onClick={() => void removeMacro(m)} aria-label={`Delete macro ${m.trigger}`} title="Delete">
                        <Trash2 size={15} strokeWidth={1.8} aria-hidden className="text-danger" />
                      </button>
                    </>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
