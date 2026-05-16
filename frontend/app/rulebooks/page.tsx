'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Rulebook } from '@/lib/api';
import { rulebookHref, rulebookEditorHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';

export default function RulebooksPage() {
  const [items, setItems] = useState<Rulebook[]>([]);
  const [yaml, setYaml] = useState('');
  const [busy, setBusy] = useState(false);
  const [problems, setProblems] = useState<string[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.rulebooks.list().then(setItems).catch((e: Error) => setError(e.message));
  }, []);

  async function loadOne(id: string) {
    const rb = await api.rulebooks.get(id);
    setYaml(rb.sourceYaml || '');
    setProblems(null);
  }

  async function validate() {
    setBusy(true);
    try {
      const r = await api.rulebooks.validateYaml(yaml);
      setProblems(r.problems);
    } finally {
      setBusy(false);
    }
  }

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await api.rulebooks.save(yaml);
      setItems(await api.rulebooks.list());
      setProblems([]);
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Container>
      <PageHeader
        title="Rulebooks"
        description="Versioned, testable, institution-approved configuration packages that govern AI generation and validation."
        primaryAction={
          <Link href={rulebookEditorHref()} className="primary-ghost" style={{ textDecoration: 'none' }}>+ Create new (visual)</Link>
        }
      />

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-workspace" style={{ gridTemplateColumns: 'minmax(280px, 360px) 1fr' }}>
        <div className="rp-panel">
          <div className="rp-panel-title">Library</div>
          <table className="rp-table">
            <thead>
              <tr><th>Id</th><th>Version</th><th>Status</th><th aria-label="Actions" /></tr>
            </thead>
            <tbody>
              {items.map((rb) => (
                <tr key={rb.id} onClick={() => loadOne(rb.id)} tabIndex={0} role="button" onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') loadOne(rb.id); }} style={{ cursor: 'pointer' }}>
                  <td><code>{rb.rulebookId}</code><div style={{ color: 'var(--text-muted)', fontSize: 12 }}>{rb.name}</div></td>
                  <td>{rb.version}</td>
                  <td><span className={`badge ${statusBadge(rb.status)}`}>{statusLabel(rb.status)}</span></td>
                  <td onClick={(e) => e.stopPropagation()}>
                    <Link href={rulebookHref(rb.id)}>Open →</Link>
                    {' '}
                    <Link href={rulebookEditorHref(rb.id)} className="primary-ghost" style={{ textDecoration: 'none', fontSize: 12, padding: '2px 8px' }}>Visual Editor</Link>
                  </td>
                </tr>
              ))}
              {items.length === 0 && <tr><td colSpan={4} style={{ color: 'var(--text-muted)' }}>No rulebooks yet.</td></tr>}
            </tbody>
          </table>
        </div>

        <div className="rp-panel">
          <div className="rp-row between" style={{ marginBottom: 12 }}>
            <div className="rp-panel-title" style={{ marginBottom: 0 }}>Editor</div>
            <div className="rp-row">
              <button className="ghost" onClick={validate} disabled={busy || !yaml}>Validate</button>
              <button className="primary" onClick={save} disabled={busy || !yaml}>Save</button>
            </div>
          </div>
          <textarea
            value={yaml}
            onChange={(e) => setYaml(e.target.value)}
            placeholder="rulebook_id: chest_ct_v1
name: Chest CT
version: 1.0.0
status: draft
..."
            style={{ minHeight: 420, fontFamily: 'var(--mono)', fontSize: 12.5 }}
          />
          {problems !== null && (
            <div style={{ marginTop: 12 }}>
              {problems.length === 0
                ? <span className="badge ok">OK — schema valid</span>
                : problems.map((p, i) => <div key={i} className="finding warning">{p}</div>)}
            </div>
          )}
        </div>
      </div>
    </Container>
  );
}

function statusLabel(s: Rulebook['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'In review', 'Approved', 'Deprecated'][s] ?? String(s);
}
function statusBadge(s: Rulebook['status']): string {
  const v = typeof s === 'number' ? s : 0;
  return ['', 'warn', 'ok', 'danger'][v] ?? '';
}
