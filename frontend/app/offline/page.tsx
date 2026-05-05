'use client';

import { useEffect, useState } from 'react';
import {
  listOfflineDrafts,
  syncOfflineDrafts,
  deleteOfflineDraft,
  saveOfflineDraft,
  type OfflineDraft,
} from '@/lib/offlineDrafts';

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
    <div>
      <h1 className="rp-page-title">Offline drafts</h1>
      <p className="rp-page-sub">
        Drafts saved while the device is offline are buffered locally and
        replayed against the server once connectivity returns. You can also
        sync manually below.
      </p>

      <div className="rp-panel">
        <div className="rp-panel-title">
          {drafts.length} draft{drafts.length === 1 ? '' : 's'} in buffer
        </div>
        <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
          <button className="primary" disabled={busy} onClick={onSync}>
            {busy ? 'Syncing…' : 'Sync now'}
          </button>
          <button className="ghost" onClick={refresh}>Refresh</button>
        </div>
        {msg && <p className="rp-page-sub">{msg}</p>}

        {drafts.length === 0 && (
          <p className="rp-page-sub">No offline drafts. New drafts created while offline will appear here.</p>
        )}

        <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {drafts.map((d) => (
            <li key={d.localId} className="rp-panel" style={{ marginTop: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
                <div>
                  <div className="rp-panel-title">
                    {d.modality || 'Modality?'} · {d.bodyPart || 'Body part?'}
                  </div>
                  <div className="rp-page-sub">
                    Accession <code>{d.accessionNumber || '—'}</code> · rev {d.rev} ·{' '}
                    {new Date(d.updatedAt).toLocaleString()}{' '}
                    {d.dirty
                      ? <span className="badge warn">dirty</span>
                      : <span className="badge ok">synced</span>}{' '}
                    {d.serverId
                      ? <span className="badge info">server id known</span>
                      : <span className="badge warn">never synced</span>}
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
      </div>

      {editingId && draftBuf && (
        <div className="rp-panel">
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
    </div>
  );
}
