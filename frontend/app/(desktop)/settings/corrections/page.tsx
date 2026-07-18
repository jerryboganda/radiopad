'use client';

// F7 (dictation brief §6) — Personal correction dictionary. The radiologist's own find→replace
// entries, applied deterministically BEFORE the LLM and layered over the org lexicon (the user's
// entry wins for the same term). Scoped to the signed-in user; consumed by the dictation-draft
// pipeline via the backend `CorrectionDictionary.Resolve`. Per-device chrome, but the data itself
// lives server-side (`/api/user-corrections`), so this is a real data screen with
// loading / empty / error states.

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, Plus, Pencil, Trash2, Check, X, SpellCheck } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { api } from '@/lib/api';
import {
  validateCorrection,
  sortCorrections,
  type UserCorrection,
} from '@/lib/userCorrections';

export default function CorrectionsPage() {
  const [rows, setRows] = useState<UserCorrection[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Add-form state.
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Inline row editing.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTo, setEditTo] = useState('');

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      const list = await api.userCorrections.list();
      setRows(list);
    } catch (e) {
      setRows(null);
      setLoadError(e instanceof Error ? e.message : 'Could not load your corrections.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const sorted = useMemo(() => (rows ? sortCorrections(rows) : []), [rows]);

  const liveCheck = useMemo(
    () => validateCorrection(from, to, rows ?? []),
    [from, to, rows],
  );

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    const check = validateCorrection(from, to, rows ?? []);
    if (!check.ok || !check.value) {
      setFormError(check.error ?? 'Please check the entry.');
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      const saved = await api.userCorrections.save(check.value);
      setRows((prev) => {
        const next = (prev ?? []).filter((r) => r.id !== saved.id && r.from !== saved.from);
        return [...next, saved];
      });
      setFrom('');
      setTo('');
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Could not save the correction.');
    } finally {
      setSaving(false);
    }
  }

  function beginEdit(row: UserCorrection) {
    setEditingId(row.id);
    setEditTo(row.to);
    setFormError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setEditTo('');
  }

  async function saveEdit(row: UserCorrection) {
    const check = validateCorrection(row.from, editTo, rows ?? [], row.id);
    if (!check.ok || !check.value) {
      setFormError(check.error ?? 'Please check the entry.');
      return;
    }
    setSaving(true);
    try {
      const saved = await api.userCorrections.save(check.value);
      setRows((prev) => (prev ?? []).map((r) => (r.id === saved.id ? saved : r)));
      cancelEdit();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Could not save the change.');
    } finally {
      setSaving(false);
    }
  }

  async function remove(row: UserCorrection) {
    setSaving(true);
    try {
      await api.userCorrections.delete(row.id);
      setRows((prev) => (prev ?? []).filter((r) => r.id !== row.id));
      if (editingId === row.id) cancelEdit();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Could not delete the correction.');
    } finally {
      setSaving(false);
    }
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
        title="Dictation corrections"
        description="Your personal spoken-word fixes. Applied automatically before your dictation is formatted — and they take priority over your organisation's shared list."
      />

      {/* ── Add a correction ───────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">
          <SpellCheck size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
          Add a correction
        </div>
        <p className="rp-page-sub" style={{ marginBottom: 12 }}>
          When the engine hears the word on the left, it writes the word on the right instead — for
          example “apendix” → “appendix”, or “em-eye” → “MRI”.
        </p>
        <form onSubmit={handleAdd} style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div className="section-block" style={{ margin: 0, flex: '1 1 220px' }}>
            <label htmlFor="corr-from">Heard as</label>
            <input
              id="corr-from"
              className="rp-input"
              value={from}
              onChange={(e) => {
                setFrom(e.target.value);
                setFormError(null);
              }}
              placeholder="apendix"
              autoComplete="off"
              spellCheck={false}
            />
          </div>
          <span aria-hidden style={{ paddingBottom: 10 }}>
            <ArrowRight size={16} strokeWidth={1.8} className="text-ink-soft" />
          </span>
          <div className="section-block" style={{ margin: 0, flex: '1 1 220px' }}>
            <label htmlFor="corr-to">Write instead</label>
            <input
              id="corr-to"
              className="rp-input"
              value={to}
              onChange={(e) => {
                setTo(e.target.value);
                setFormError(null);
              }}
              placeholder="appendix"
              autoComplete="off"
              spellCheck={false}
            />
          </div>
          <button
            type="submit"
            className="primary"
            disabled={saving || !liveCheck.ok}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
          >
            <Plus size={15} strokeWidth={2} aria-hidden />
            Add
          </button>
        </form>
        {formError && (
          <p className="text-danger" role="alert" style={{ marginTop: 10, fontSize: 13 }}>
            {formError}
          </p>
        )}
        {!formError && liveCheck.warning && (from.trim() || to.trim()) && (
          <p className="text-warning" role="status" style={{ marginTop: 10, fontSize: 13 }}>
            {liveCheck.warning}
          </p>
        )}
      </div>

      {/* ── The list ───────────────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Your corrections</div>

        {loadError && (
          <ErrorState
            title="Couldn’t load your corrections"
            message={loadError}
            onRetry={() => void load()}
          />
        )}

        {!loadError && rows === null && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 8 }}>
            <Skeleton height={40} />
            <Skeleton height={40} />
            <Skeleton height={40} />
          </div>
        )}

        {!loadError && rows !== null && sorted.length === 0 && (
          <EmptyState
            title="No personal corrections yet"
            description="Add one above. They’re applied to your dictation automatically, before formatting."
          />
        )}

        {!loadError && sorted.length > 0 && (
          <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
            {sorted.map((row) => {
              const isEditing = editingId === row.id;
              return (
                <li
                  key={row.id}
                  className="rp-card"
                  style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 14px' }}
                >
                  <code style={{ fontWeight: 600 }}>{row.from}</code>
                  <ArrowRight size={14} strokeWidth={1.8} aria-hidden className="text-ink-soft" />
                  {isEditing ? (
                    <input
                      className="rp-input"
                      value={editTo}
                      onChange={(e) => setEditTo(e.target.value)}
                      aria-label={`Replacement for ${row.from}`}
                      autoFocus
                      spellCheck={false}
                      style={{ flex: 1, maxWidth: 320 }}
                    />
                  ) : (
                    <code style={{ flex: 1 }}>{row.to}</code>
                  )}

                  <div style={{ display: 'flex', gap: 6 }}>
                    {isEditing ? (
                      <>
                        <button
                          type="button"
                          className="subtle"
                          onClick={() => void saveEdit(row)}
                          disabled={saving}
                          aria-label="Save"
                          title="Save"
                        >
                          <Check size={15} strokeWidth={2} aria-hidden />
                        </button>
                        <button
                          type="button"
                          className="ghost"
                          onClick={cancelEdit}
                          aria-label="Cancel"
                          title="Cancel"
                        >
                          <X size={15} strokeWidth={2} aria-hidden />
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => beginEdit(row)}
                          aria-label={`Edit ${row.from}`}
                          title="Edit"
                        >
                          <Pencil size={15} strokeWidth={1.8} aria-hidden />
                        </button>
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => void remove(row)}
                          disabled={saving}
                          aria-label={`Delete ${row.from}`}
                          title="Delete"
                        >
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
    </Container>
  );
}
