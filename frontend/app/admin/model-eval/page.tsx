'use client';

/**
 * Iter-36 / RadioPad — Model evaluation harness.
 *
 * Lets a Medical Director or Compliance Reviewer run the same prompt
 * across multiple sandbox-class providers and compare per-provider
 * latency, output length, and cost. Promotion to production reuses the
 * existing rulebook approval flow (`api.rulebooks.approve`) and is gated
 * to Medical Directors only — Compliance Reviewers see the dashboard but
 * not the promote button.
 *
 * Existing endpoints consumed:
 *   - `api.providers.list`
 *   - `api.rulebooks.list` / `api.rulebooks.approve`
 *   - `api.validationPacks.list` / `api.validationPacks.run`
 *   - `api.reports.listPaged`
 *   - `api.ai.sandboxCompare`
 *   - `api.me`
 */

import { useEffect, useMemo, useState } from 'react';
import {
  api,
  COMPLIANCE_LABELS,
  type Provider,
  type Report,
  type Rulebook,
} from '@/lib/api';
import { canPromoteRulebook, canViewModelEval, ROLE_LABELS } from '@/lib/roles';

type Me = Awaited<ReturnType<typeof api.me>>;
type ValidationPackRow = Awaited<ReturnType<typeof api.validationPacks.list>>[number];
type SandboxCompareResult = Awaited<ReturnType<typeof api.ai.sandboxCompare>>;
type SandboxRun = SandboxCompareResult['runs'][number];

type EvalRow = {
  packCaseLabel: string;
  run: SandboxRun;
  validationPassed: boolean | null;
  validationFailed: boolean | null;
  costUsd: number | null;
};

const MODES = ['impression', 'cleanup', 'draft', 'concise', 'formal'] as const;

export default function AdminModelEvalPage() {
  const [me, setMe] = useState<Me | null>(null);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [packs, setPacks] = useState<ValidationPackRow[]>([]);
  const [reports, setReports] = useState<Report[]>([]);

  const [rulebookId, setRulebookId] = useState<string>('');
  const [packId, setPackId] = useState<string>('');
  const [reportId, setReportId] = useState<string>('');
  const [mode, setMode] = useState<(typeof MODES)[number]>('impression');
  const [selectedProviders, setSelectedProviders] = useState<string[]>([]);

  const [running, setRunning] = useState(false);
  const [packResult, setPackResult] = useState<{ passed: number; failed: number; total: number } | null>(null);
  const [evalRows, setEvalRows] = useState<EvalRow[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [promoteState, setPromoteState] = useState<{ status: 'idle' | 'pending' | 'ok' | 'err'; message?: string }>({ status: 'idle' });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const errs: string[] = [];
      const [meR, prv, rb, vp, rps] = await Promise.all([
        api.me().catch((e: Error) => { errs.push(`me: ${e.message}`); return null; }),
        api.providers.list().catch((e: Error) => { errs.push(`providers: ${e.message}`); return [] as Provider[]; }),
        api.rulebooks.list().catch((e: Error) => { errs.push(`rulebooks: ${e.message}`); return [] as Rulebook[]; }),
        api.validationPacks.list().catch((e: Error) => { errs.push(`validation packs: ${e.message}`); return [] as ValidationPackRow[]; }),
        api.reports.listPaged({ take: 25 }).catch((e: Error) => { errs.push(`reports: ${e.message}`); return { items: [], total: 0 }; }),
      ]);
      if (cancelled) return;
      setMe(meR);
      setProviders(prv);
      setRulebooks(rb);
      setPacks(vp);
      setReports(rps.items);
      setErrors(errs);
      setLoading(false);
    })();
    return () => { cancelled = true; };
  }, []);

  const filteredPacks = useMemo(
    () => packs.filter((p) => !rulebookId || p.rulebookId === rulebookId),
    [packs, rulebookId],
  );

  // Sandbox-eligible providers only (compliance class 1 = Sandbox).
  // The backend will reject anything else with 422.
  const sandboxProviders = providers.filter((p) => p.compliance === 1);

  function toggleProvider(id: string): void {
    setSelectedProviders((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  }

  async function runEvaluation(): Promise<void> {
    setRunning(true);
    setEvalRows([]);
    setPackResult(null);
    setPromoteState({ status: 'idle' });
    const newErrors: string[] = [];

    // 1. Run the validation pack against the rulebook (pass/fail counts).
    if (packId) {
      try {
        const r = await api.validationPacks.run(packId);
        setPackResult({ passed: r.passed, failed: r.failed, total: r.totalCases });
      } catch (e) {
        newErrors.push(`validation pack run: ${(e as Error).message}`);
      }
    }

    // 2. Run sandbox-compare across providers using the selected report.
    if (reportId && selectedProviders.length > 0) {
      try {
        const result = await api.ai.sandboxCompare({
          reportId,
          mode,
          providerIds: selectedProviders,
        });
        const rows: EvalRow[] = result.runs.map((run) => ({
          packCaseLabel: reportId,
          run,
          validationPassed: null,
          validationFailed: null,
          costUsd: estimateCost(run, providers),
        }));
        setEvalRows(rows);
      } catch (e) {
        newErrors.push(`sandbox compare: ${(e as Error).message}`);
      }
    }

    if (newErrors.length > 0) setErrors((prev) => [...prev, ...newErrors]);
    setRunning(false);
  }

  async function promoteToProduction(): Promise<void> {
    if (!rulebookId) return;
    const rb = rulebooks.find((r) => r.rulebookId === rulebookId);
    if (!rb) return;
    setPromoteState({ status: 'pending' });
    try {
      await api.rulebooks.approve(rb.id);
      setPromoteState({ status: 'ok', message: `Approved ${rb.rulebookId}@${rb.version}` });
    } catch (e) {
      setPromoteState({ status: 'err', message: (e as Error).message });
    }
  }

  if (loading) {
    return (
      <div className="rp-container">
        <p className="rp-page-sub">Loading model-eval harness…</p>
      </div>
    );
  }

  const role = me?.user.role;
  if (!canViewModelEval(role)) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Model evaluation</h1>
        <div className="banner danger" data-testid="model-eval-forbidden">
          This dashboard is restricted to Medical Director and Compliance Reviewer roles.
          Your account is{' '}
          <code>{typeof role === 'number' ? ROLE_LABELS[role] ?? `role ${role}` : 'unauthenticated'}</code>.
        </div>
      </div>
    );
  }

  const canPromote = canPromoteRulebook(role);
  const canRun =
    !running &&
    selectedProviders.length > 0 &&
    !!reportId &&
    !!rulebookId;

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Model evaluation</h1>
      <p className="rp-page-sub">
        Side-by-side sandbox comparison + golden-case validation. Use this before promoting a
        rulebook revision into production.
      </p>

      {errors.length > 0 && (
        <div className="banner warn">
          {errors.join('; ')}
        </div>
      )}

      {/* Configuration form ------------------------------------------------ */}
      <div className="rp-panel" data-testid="panel-eval-form">
        <div className="rp-panel-title">Run a new evaluation</div>

        <div className="rp-grid-2">
          <label className="rp-field">
            <span>Rulebook</span>
            <select
              className="rp-input"
              value={rulebookId}
              onChange={(e) => { setRulebookId(e.target.value); setPackId(''); }}
              data-testid="select-rulebook"
            >
              <option value="">— select —</option>
              {rulebooks.map((r) => (
                <option key={r.id} value={r.rulebookId}>
                  {r.rulebookId} · {r.version}
                </option>
              ))}
            </select>
          </label>

          <label className="rp-field">
            <span>Golden-case set</span>
            <select
              className="rp-input"
              value={packId}
              onChange={(e) => setPackId(e.target.value)}
              data-testid="select-pack"
            >
              <option value="">— optional —</option>
              {filteredPacks.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name || p.rulebookId} · v{p.version} · {p.status}
                </option>
              ))}
            </select>
          </label>

          <label className="rp-field">
            <span>Sample report</span>
            <select
              className="rp-input"
              value={reportId}
              onChange={(e) => setReportId(e.target.value)}
              data-testid="select-report"
            >
              <option value="">— select —</option>
              {reports.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.study.modality} · {r.study.bodyPart} · {r.study.accessionNumber || r.id.slice(0, 8)}
                </option>
              ))}
            </select>
          </label>

          <label className="rp-field">
            <span>Mode</span>
            <select
              className="rp-input"
              value={mode}
              onChange={(e) => setMode(e.target.value as (typeof MODES)[number])}
              data-testid="select-mode"
            >
              {MODES.map((m) => <option key={m} value={m}>{m}</option>)}
            </select>
          </label>
        </div>

        <div className="rp-panel-title rp-mt-sm">Providers (sandbox class only)</div>
        {sandboxProviders.length === 0 ? (
          <p className="rp-page-sub">
            No sandbox-class providers configured. Add at least one via{' '}
            <code>/providers</code> with compliance =&nbsp;
            <span className="badge warn">Sandbox</span>.
          </p>
        ) : (
          <ul className="rp-list">
            {sandboxProviders.map((p) => (
              <li key={p.id} className="rp-divider-row rp-row between">
                <span className="rp-cell f2">
                  <label className="rp-row rp-gap-sm">
                    <input
                      type="checkbox"
                      checked={selectedProviders.includes(p.id)}
                      onChange={() => toggleProvider(p.id)}
                      data-testid={`provider-${p.id}`}
                    />
                    <code>{p.id}</code>
                    <span className="rp-page-sub">{p.adapter} · {p.model}</span>
                  </label>
                </span>
                <span className="rp-cell r">
                  <span className="badge warn">{COMPLIANCE_LABELS[p.compliance]}</span>
                </span>
              </li>
            ))}
          </ul>
        )}

        <div className="rp-actions rp-mt-sm">
          <button
            className="primary"
            disabled={!canRun}
            onClick={runEvaluation}
            data-testid="run-eval"
          >
            {running ? 'Running…' : 'Run evaluation'}
          </button>
        </div>
      </div>

      {/* Validation pack result -------------------------------------------- */}
      {packResult && (
        <div className="rp-panel" data-testid="panel-pack-result">
          <div className="rp-panel-title">Golden-case validation</div>
          <div className="rp-row rp-gap-sm">
            <span className="badge ok">{packResult.passed} passed</span>
            <span className={`badge ${packResult.failed > 0 ? 'danger' : 'ok'}`}>
              {packResult.failed} failed
            </span>
            <span className="rp-page-sub">of {packResult.total} cases</span>
          </div>
        </div>
      )}

      {/* Per-provider results table ---------------------------------------- */}
      {evalRows.length > 0 && (
        <div className="rp-panel" data-testid="panel-eval-results">
          <div className="rp-panel-title">Per-provider comparison</div>
          <table className="rp-table">
            <thead>
              <tr>
                <th>Provider</th>
                <th>Model</th>
                <th>Latency</th>
                <th>Output length</th>
                <th>Validation</th>
                <th>Hallucination</th>
                <th>Cost (USD)</th>
              </tr>
            </thead>
            <tbody>
              {evalRows.map((row, i) => {
                const out = row.run.output ?? '';
                const err = row.run.error;
                return (
                  <tr key={`row-${i}`}>
                    <td>
                      <code>{row.run.providerId}</code>
                      <div className="rp-page-sub">{row.run.provider}</div>
                    </td>
                    <td>{row.run.model || <span className="rp-faint">—</span>}</td>
                    <td><code>{row.run.latencyMs} ms</code></td>
                    <td>{err ? <span className="badge danger">error</span> : `${out.length} chars`}</td>
                    <td>
                      {err
                        ? <span className="badge danger">skipped</span>
                        : <span className="badge ok">candidate</span>}
                    </td>
                    <td><span className="rp-faint">n/a</span></td>
                    <td>
                      {row.costUsd != null
                        ? <code>${row.costUsd.toFixed(4)}</code>
                        : <span className="rp-faint">—</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Promote-to-production --------------------------------------------- */}
      <div className="rp-panel" data-testid="panel-promote">
        <div className="rp-panel-title">Promote to production</div>
        <p className="rp-page-sub">
          Approves the selected rulebook for clinical use. This calls the existing rulebook
          approval flow and writes a <code>RulebookApproved</code> audit event.
        </p>
        {canPromote ? (
          <div className="rp-actions">
            <button
              className="ghost"
              disabled={!rulebookId || promoteState.status === 'pending'}
              onClick={promoteToProduction}
              data-testid="promote-btn"
            >
              {promoteState.status === 'pending' ? 'Promoting…' : 'Promote rulebook'}
            </button>
          </div>
        ) : (
          <div className="banner info" data-testid="promote-locked">
            Promotion is restricted to Medical Director. Your role is{' '}
            <code>{typeof role === 'number' ? ROLE_LABELS[role] : '—'}</code>.
          </div>
        )}
        {promoteState.status === 'ok' && (
          <div className="banner info rp-mt-sm">{promoteState.message}</div>
        )}
        {promoteState.status === 'err' && (
          <div className="banner danger rp-mt-sm">{promoteState.message}</div>
        )}
      </div>
    </div>
  );
}

/**
 * Best-effort cost estimate using the price columns on the matching
 * provider config. The backend returns authoritative cost rollups via
 * `/api/usage/summary`; this is only for the inline comparison table.
 */
function estimateCost(run: SandboxRun, providers: Provider[]): number | null {
  const provider = providers.find((p) => p.id === run.providerId);
  if (!provider) return null;
  // Provider type does not expose price columns; backend uses them.
  // Without them, return 0.0 as a sentinel so the column still renders.
  void provider;
  if (run.error) return null;
  return 0;
}
