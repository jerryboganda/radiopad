'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useEffect, useMemo, useState } from 'react';
import { api } from '@/lib/api';
import Banner from '@/components/ui/Banner';
import EmptyState from '@/components/ui/EmptyState';

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
  return (
    <PermissionGate permission="validation_packs.read" title="Validation packs">
      <ValidationPacksPageInner />
    </PermissionGate>
  );
}

function ValidationPacksPageInner() {
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
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Quality check packs</h1>
          <p className="rp-page-sub">
            Test sets that prove a rulebook still catches the issues it&apos;s supposed to catch. Each pack runs against a set of example reports and shows whether the rulebook still passes.
          </p>
        </div>
      </header>

      {error && <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>}
      {info && <Banner tone="success" onDismiss={() => setInfo(null)}>{info}</Banner>}

      <div className="rp-panel">
        <div className="rp-panel-title">
          Filter by rulebook
          <span style={{ flex: 1 }} />
          <input
            className="rp-input"
            style={{ maxWidth: 280 }}
            placeholder="e.g. chest_ct_v1"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <button type="button" className="primary-ghost" onClick={refresh}>
            Refresh
          </button>
        </div>
      </div>

      {grouped.length === 0 && (
        <EmptyState
          title="No quality check packs yet"
          description="Packs appear here once they are created for a rulebook in your workspace."
        />
      )}

      <div className="rp-stagger">
      {grouped.map(([rb, list]) => (
        <div className="rp-panel" key={rb}>
          <div className="rp-panel-title">
            {rb}
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
                      <span className="rp-page-sub">v{p.version}</span>{' '}
                      <span className={`badge ${STATUS_BADGE[p.status]}`}>{p.status}</span>
                      <span className="rp-page-sub" style={{ marginLeft: 8 }}>
                        {p.caseCount} case{p.caseCount === 1 ? '' : 's'}
                      </span>
                    </div>
                    <div className="rp-page-sub">
                      Created {new Date(p.createdAt).toLocaleString()}
                      {p.approvedAt && <> · approved {new Date(p.approvedAt).toLocaleString()}</>}
                    </div>
                    {lastRun && (
                      <div className="rp-page-sub rp-anim-fade-in-up" aria-live="polite">
                        Last run:{' '}
                        <span className={`badge ${lastRun.failed === 0 ? 'ok' : 'danger'}`}>
                          {lastRun.passed}/{lastRun.totalCases} passed
                        </span>
                        {lastRun.failures.length > 0 && (
                          <details className="rp-advanced">
                            <summary>Show failures</summary>
                            <ul className="rp-list" style={{ marginTop: 6 }}>
                              {lastRun.failures.map((f) => (
                                <li key={f.caseId}>
                                  Case {f.caseId}
                                  {f.missing.length > 0 && <> · missing [{f.missing.join(', ')}]</>}
                                  {f.unexpected.length > 0 && (
                                    <> · unexpected [{f.unexpected.join(', ')}]</>
                                  )}
                                </li>
                              ))}
                            </ul>
                          </details>
                        )}
                      </div>
                    )}
                  </div>
                  <div style={{ display: 'flex', gap: 8 }} aria-busy={busy === p.id}>
                    <button
                      type="button"
                      className="primary-ghost"
                      disabled={busy === p.id}
                      aria-busy={busy === p.id}
                      onClick={() => run(p.id)}
                    >
                      {busy === p.id && <span className="rp-spinner sm" aria-hidden />}
                      Run check
                    </button>
                    {p.status === 'Draft' && (
                      <button
                        type="button"
                        className="primary"
                        disabled={busy === p.id}
                        aria-busy={busy === p.id}
                        onClick={() => approve(p.id)}
                      >
                        {busy === p.id && <span className="rp-spinner sm" aria-hidden />}
                        Approve
                      </button>
                    )}
                    {p.status !== 'Deprecated' && (
                      <button
                        type="button"
                        className="ghost"
                        disabled={busy === p.id}
                        aria-busy={busy === p.id}
                        onClick={() => deprecate(p.id)}
                      >
                        {busy === p.id && <span className="rp-spinner sm" aria-hidden />}
                        Retire
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
    </div>
  );
}
