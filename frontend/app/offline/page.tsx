'use client';

import { useEffect, useState } from 'react';
import {
  listOfflineDrafts,
  syncOfflineDrafts,
  deleteOfflineDraft,
  saveOfflineDraft,
  type OfflineDraft,
} from '@/lib/offlineDrafts';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import Banner from '@/components/ui/Banner';

/**
 * PRD MOB-005 — visibility into the offline-draft buffer. Lets the radiologist
 * inspect, edit, force-sync or discard drafts that were created or edited
 * while the device was offline.
 *
 * Locked design tokens only: `.rp-page-title`, `.rp-panel`, `.rp-panel-title`,
 * `.rp-page-sub`, `.rp-input`, `.badge`, button variants `.primary` /
 * `.ghost` / `.subtle`.
 */
export default function OfflineDraftsPage() {
  const [drafts, setDrafts] = useState<OfflineDraft[]>([]);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string>('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draftBuf, setDraftBuf] = useState<OfflineDraft | null>(null);

  async function refresh() {
    setDrafts(await listOfflineDrafts());
  }
  useEffect(() => { refresh(); }, []);

  async function onSync() {
    setBusy(true); setMsg('');
    try {
      const r = await syncOfflineDrafts();
      setMsg(`Synced ${r.synced} draft(s); ${r.failed} failed.`);
      await refresh();
    } finally { setBusy(false); }
  }

  async function onDelete(id: string) {
    await deleteOfflineDraft(id);
    await refresh();
  }

  function startEdit(d: OfflineDraft) {
    setEditingId(d.localId);
    setDraftBuf({ ...d });
  }

  async function saveEdit() {
    if (!draftBuf) return;
    const next: OfflineDraft = {
      ...draftBuf,
      rev: draftBuf.rev + 1,
      updatedAt: Date.now(),
      dirty: true,
    };
    await saveOfflineDraft(next);
    setEditingId(null); setDraftBuf(null);
    await refresh();
  }

  return (
    <Container>
      <PageHeader
        title="Offline drafts"
        description="Drafts you wrote while your device was offline. They'll sync back to the server automatically when you reconnect — or you can sync them now."
      />

      <div className="rp-panel" aria-live="polite" aria-busy={busy}>
        <div className="rp-panel-title">
          {drafts.length} draft{drafts.length === 1 ? '' : 's'} waiting
        </div>
        <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
          <button className="primary" disabled={busy} aria-busy={busy} onClick={onSync}>
            {busy && <span className="rp-spinner sm" aria-hidden />}
            {busy ? 'Syncing…' : 'Sync now'}
          </button>
          <button className="ghost" onClick={refresh}>Refresh</button>
        </div>
        {msg && <Banner tone="success">{msg}</Banner>}

        {drafts.length === 0 ? (
          <EmptyState
            title="No offline drafts"
            description="Any new drafts you write while offline will appear here, ready to sync when you reconnect."
          />
        ) : (
        <ul className="rp-stagger" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {drafts.map((d) => (
            <li key={d.localId} className="rp-panel" style={{ marginTop: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
                <div>
                  <div className="rp-panel-title">
                    {d.modality || 'No modality'} · {d.bodyPart || 'No body part'}
                  </div>
                  <div className="rp-page-sub">
                    Accession {d.accessionNumber || '—'} · saved {new Date(d.updatedAt).toLocaleString()}{' '}
                    {d.dirty
                      ? <span className="badge warn">not synced</span>
                      : <span className="badge ok">synced</span>}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 6 }}>
                  <button className="ghost" onClick={() => startEdit(d)}>Edit</button>
                  <button className="subtle" onClick={() => onDelete(d.localId)}>Discard</button>
                </div>
              </div>
            </li>
          ))}
        </ul>
        )}
      </div>

      {editingId && draftBuf && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Edit draft</div>
          <label className="rp-field">
            <span>Findings</span>
            <textarea
              className="rp-input"
              rows={6}
              value={draftBuf.findings}
              onChange={(e) => setDraftBuf({ ...draftBuf, findings: e.target.value })}
            />
          </label>
          <label className="rp-field">
            <span>Impression</span>
            <textarea
              className="rp-input"
              rows={4}
              value={draftBuf.impression}
              onChange={(e) => setDraftBuf({ ...draftBuf, impression: e.target.value })}
            />
          </label>
          <label className="rp-field">
            <span>Recommendations</span>
            <textarea
              className="rp-input"
              rows={3}
              value={draftBuf.recommendations}
              onChange={(e) => setDraftBuf({ ...draftBuf, recommendations: e.target.value })}
            />
          </label>
          <div style={{ display: 'flex', gap: 8 }}>
            <button className="primary" onClick={saveEdit}>Save offline</button>
            <button className="ghost" onClick={() => { setEditingId(null); setDraftBuf(null); }}>Cancel</button>
          </div>
        </div>
      )}
    </Container>
  );
}
