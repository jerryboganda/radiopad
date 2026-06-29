'use client';

/**
 * Iter-36 — shared CRUD surface for the admin-managed Modality / BodyPart
 * catalogs. Both `/modalities` and `/body-parts` render this with their own
 * api client + copy. Reads are open; create/edit/delete are gated on the
 * `manage` permission (backend still enforces).
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { CatalogItem } from '@/lib/api';
import { usePermissions, type PermissionKey } from '@/lib/permissions';

type CatalogClient = {
  list: () => Promise<CatalogItem[]>;
  save: (body: { id?: string; code: string; name?: string; active?: boolean; sortOrder?: number }) => Promise<CatalogItem>;
  remove: (id: string) => Promise<void>;
};

type Props = {
  title: string;
  subtitle: string;
  /** Singular noun for empty/placeholder copy, e.g. "modality". */
  itemNoun: string;
  client: CatalogClient;
  managePermission: PermissionKey;
};

export default function CatalogManager({ title, subtitle, itemNoun, client, managePermission }: Props) {
  const { can, loading: permsLoading } = usePermissions();
  const canManage = permsLoading || can(managePermission);

  const [items, setItems] = useState<CatalogItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const codeInputRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLDivElement>(null);

  const reload = useCallback(() => {
    setError(null);
    client.list().then(setItems).catch((e: Error) => { setItems([]); setError(e.message); });
  }, [client]);

  useEffect(() => { reload(); }, [reload]);

  function resetForm() {
    setEditingId(null);
    setCode('');
    setName('');
  }

  function startEdit(item: CatalogItem) {
    setEditingId(item.id);
    setCode(item.code);
    setName(item.name);
    setError(null);
    // Bring the (now-active) edit form into view and focus it — the form lives at
    // the top of the page, away from the row the operator just clicked.
    formRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    codeInputRef.current?.focus();
  }

  async function save() {
    const trimmed = code.trim();
    if (!trimmed) { setError('Code is required.'); return; }
    setSaving(true);
    setError(null);
    try {
      await client.save({ id: editingId ?? undefined, code: trimmed, name: name.trim() });
      resetForm();
      reload();
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setSaving(false);
    }
  }

  async function remove(item: CatalogItem) {
    if (!confirm(`Delete ${itemNoun} “${item.code}”? Existing reports keep their stored value.`)) return;
    setError(null);
    try {
      await client.remove(item.id);
      if (editingId === item.id) resetForm();
      reload();
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    }
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">{title}</h1>
          <p className="rp-page-sub">{subtitle}</p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}

      {canManage && (
        <div className="rp-panel" ref={formRef}>
          <div className="rp-panel-title">{editingId ? `Edit ${itemNoun}` : `Add ${itemNoun}`}</div>
          <div className="rp-row rp-gap-sm">
            <div className="section-block" style={{ flex: 1 }}>
              <label htmlFor="cat-code">Code</label>
              <input
                id="cat-code"
                ref={codeInputRef}
                className="rp-input"
                placeholder="e.g. CT"
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
            </div>
            <div className="section-block" style={{ flex: 2 }}>
              <label htmlFor="cat-name">Display name</label>
              <input
                id="cat-name"
                className="rp-input"
                placeholder="e.g. Computed Tomography"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
          </div>
          <div className="rp-toolbar">
            <button className="primary" type="button" disabled={saving || !code.trim()} onClick={save}>
              {saving ? '…' : editingId ? 'Save changes' : `Add ${itemNoun}`}
            </button>
            {editingId && (
              <button className="ghost" type="button" disabled={saving} onClick={resetForm}>
                Cancel
              </button>
            )}
          </div>
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-panel-title">{title}</div>
        <ul className="rp-list">
          <li className="rp-row between rp-divider-row">
            <span className="rp-stat-label rp-cell f1">Code</span>
            <span className="rp-stat-label rp-cell f2">Display name</span>
            {canManage && <span className="rp-stat-label rp-cell f1 r">Actions</span>}
          </li>
          {items === null && <li className="rp-page-sub rp-divider-row">Loading…</li>}
          {items !== null && items.length === 0 && (
            <li className="rp-page-sub rp-divider-row">No {itemNoun} entries yet.</li>
          )}
          {(items ?? []).map((item) => (
            <li key={item.id} className="rp-row between rp-divider-row">
              <span className="rp-cell f1"><code>{item.code}</code></span>
              <span className="rp-cell f2">{item.name || '—'}</span>
              {canManage && (
                <span className="rp-cell f1 r rp-row rp-gap-sm" style={{ justifyContent: 'flex-end' }}>
                  <button className="ghost" type="button" onClick={() => startEdit(item)}>Edit</button>
                  <button className="ghost" type="button" onClick={() => remove(item)}>Delete</button>
                </span>
              )}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
