'use client';

// F3 — Snippets (autotext) manager. Device-local shortcuts that expand into canned report prose,
// optionally with ${field} fill-in placeholders the radiologist tabs through after inserting. Stored
// in localStorage (never PHI), like Hotkeys. Consumed by the editor's snippet insertion.

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Plus, Pencil, Trash2, Check, X, Type } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import {
  getSnippets,
  saveSnippet,
  deleteSnippet,
  findFields,
  SNIPPETS_CHANGE_EVENT,
  type Snippet,
} from '@/lib/snippets';

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
    // Adding a new trigger that already exists (and isn't the one being edited) would overwrite it.
    const clash = rows.find((s) => s.id !== editingId && s.trigger.toLowerCase() === t.toLowerCase());
    if (clash && !editingId) {
      setFormError(`A snippet for “${clash.trigger}” already exists — editing it instead.`);
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
      <div className="rp-panel rp-anim-fade-in-up">
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
    </Container>
  );
}
