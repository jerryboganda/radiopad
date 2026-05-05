'use client';

import { useEffect, useMemo, useState } from 'react';
import { api } from '@/lib/api';

type Pack = Awaited<ReturnType<typeof api.validationPacks.list>>[number];
type RunSummary = Awaited<ReturnType<typeof api.validationPacks.run>>;

const STATUS_BADGE: Record<Pack['status'], string> = {
  Draft: 'info',
  Approved: 'ok',
  Deprecated: 'danger',
};

/**
 * Iter-35 — versioned clinical validation packs (rulebook golden suites).
 * Lists packs by rulebook with Approve / Deprecate / Run actions. Last-run
 * summary is held client-side per pack (audit row carries authoritative
 * history server-side via AuditAction.ValidationPackRun).
 */
export default function ValidationPacksPage() {
  const [packs, setPacks] = useState<Pack[]>([]);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [runs, setRuns] = useState<Record<string, RunSummary>>({});
  const [busy, setBusy] = useState<string | null>(null);

  async function refresh() {
    try {
      setPacks(await api.validationPacks.list(filter || undefined));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const grouped = useMemo(() => {
    const out = new Map<string, Pack[]>();
    for (const p of packs) {
      if (!out.has(p.rulebookId)) out.set(p.rulebookId, []);
      out.get(p.rulebookId)!.push(p);
    }
    return Array.from(out.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [packs]);

  async function approve(id: string) {
    setBusy(id);
    setError(null);
    setInfo(null);
    try {
      await api.validationPacks.approve(id);
      setInfo('Pack approved.');
      await refresh();
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error ?? err.message);
    } finally {
      setBusy(null);
    }
  }

  async function deprecate(id: string) {
    if (!confirm('Mark this pack as Deprecated? It cannot be re-approved.')) return;
    setBusy(id);
    setError(null);
    setInfo(null);
    try {
      await api.validationPacks.deprecate(id);
      setInfo('Pack deprecated.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function run(id: string) {
    setBusy(id);
    setError(null);
    try {
      const r = await api.validationPacks.run(id);
      setRuns((prev) => ({ ...prev, [id]: r }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Validation packs</h1>
      <p className="rp-page-sub">
        Versioned clinical validation packs (rulebook golden suites). Each pack is a tenant-scoped
        bundle of <code>{'{report, expectFlagged}'}</code> golden cases that a rulebook must pass
        before promotion. Lifecycle: <span className="badge info">Draft</span> →{' '}
        <span className="badge ok">Approved</span> → <span className="badge danger">Deprecated</span>.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">
          Filter
          <span style={{ flex: 1 }} />
          <input
            className="rp-input"
            style={{ maxWidth: 280 }}
            placeholder="rulebook id (e.g. chest_ct_v1)"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <button type="button" className="primary-ghost" onClick={refresh}>
            Refresh
          </button>
        </div>
      </div>

      {grouped.length === 0 && (
        <div className="rp-panel">
          <p className="rp-page-sub">No validation packs yet for this tenant.</p>
        </div>
      )}

      {grouped.map(([rb, list]) => (
        <div className="rp-panel" key={rb}>
          <div className="rp-panel-title">
            <code>{rb}</code>
            <span className="rp-page-sub" style={{ marginLeft: 8 }}>
              {list.length} pack{list.length === 1 ? '' : 's'}
            </span>
          </div>
          <ul className="rp-list">
            {list.map((p) => {
              const lastRun = runs[p.id];
              return (
                <li key={p.id} className="rp-list-row">
                  <div style={{ flex: 1 }}>
                    <div>
                      <strong>{p.name}</strong>{' '}
                      <code>v{p.version}</code>{' '}
                      <span className={`badge ${STATUS_BADGE[p.status]}`}>{p.status}</span>
                      <span className="rp-page-sub" style={{ marginLeft: 8 }}>
                        {p.caseCount} case{p.caseCount === 1 ? '' : 's'}
                      </span>
                    </div>
                    <div className="rp-page-sub">
                      created <code>{new Date(p.createdAt).toLocaleString()}</code>
                      {p.approvedAt && (
                        <>
                          {' '}
                          · approved <code>{new Date(p.approvedAt).toLocaleString()}</code>
                        </>
                      )}
                    </div>
                    {lastRun && (
                      <div className="rp-page-sub">
                        Last run:{' '}
                        <span
                          className={`badge ${lastRun.failed === 0 ? 'ok' : 'danger'}`}
                        >
                          {lastRun.passed}/{lastRun.totalCases} passed
                        </span>
                        {lastRun.failures.length > 0 && (
                          <ul className="rp-list" style={{ marginTop: 6 }}>
                            {lastRun.failures.map((f) => (
                              <li key={f.caseId}>
                                <code>{f.caseId}</code>
                                {f.missing.length > 0 && <> · missing [{f.missing.join(', ')}]</>}
                                {f.unexpected.length > 0 && (
                                  <> · unexpected [{f.unexpected.join(', ')}]</>
                                )}
                              </li>
                            ))}
                          </ul>
                        )}
                      </div>
                    )}
                  </div>
                  <div style={{ display: 'flex', gap: 8 }}>
                    <button
                      type="button"
                      className="primary-ghost"
                      disabled={busy === p.id}
                      onClick={() => run(p.id)}
                    >
                      Run
                    </button>
                    {p.status === 'Draft' && (
                      <button
                        type="button"
                        className="primary"
                        disabled={busy === p.id}
                        onClick={() => approve(p.id)}
                      >
                        Approve
                      </button>
                    )}
                    {p.status !== 'Deprecated' && (
                      <button
                        type="button"
                        className="ghost"
                        disabled={busy === p.id}
                        onClick={() => deprecate(p.id)}
                      >
                        Deprecate
                      </button>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </div>
  );
}
