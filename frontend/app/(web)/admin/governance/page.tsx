'use client';

/**
 * Iter-36 / RadioPad — Governance dashboard.
 *
 * Single Enterprise-GA surface aggregating the six governance signals the
 * Medical Director, Compliance Reviewer, and IT Admin roles need
 * day-to-day. Every panel reads from an *existing* tenant-scoped endpoint
 * via `frontend/lib/api.ts`; no new backend endpoints were introduced.
 *
 * Panels:
 *   1. Model inventory       — `api.providers.list()` + on-demand health probe
 *   2. Prompt + rulebook     — `api.rulebooks.list()` + `api.promptOverrides.list()`
 *   3. AI usage              — `api.usage.summary()` (30-day window)
 *   4. PHI routing           — `api.analytics.summary()` + audit search
 *   5. Validation results    — `api.audit.query()` filtered by ValidationPackRun
 *   6. Drift alerts          — `api.audit.query()` filtered by SystemAlert / AnomalyDetected
 *
 * RBAC — Medical Director / Compliance Reviewer / IT Admin (read-only for
 * the latter two; the page itself is read-only — every mutation lives on
 * the originating page).
 */

import Link from 'next/link';
import { useEffect, useState } from 'react';
import {
  api,
  COMPLIANCE_LABELS,
  type Provider,
  type Rulebook,
  type UsageSummary,
} from '@/lib/api';
import { ROLE_LABELS } from '@/lib/roles';
import { can } from '@/lib/permissions';
import Banner from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';
import { TableSkeleton } from '@/components/ui/Skeleton';

type Me = Awaited<ReturnType<typeof api.me>>;
type AnalyticsSummary = Awaited<ReturnType<typeof api.analytics.summary>>;
type PromptOverrideRow = Awaited<ReturnType<typeof api.promptOverrides.list>>[number];

type AuditEvent = {
  id?: string;
  action: number | string;
  createdAt: string;
  detailsJson?: string | null;
  userEmail?: string | null;
};

// AuditAction enum values used by this dashboard. Keep in sync with
// `RadioPad.Domain.Enums.AuditAction`.
const ACTION_PROVIDER_BLOCKED = 5;
const ACTION_ANOMALY_DETECTED = 25;
const ACTION_SYSTEM_ALERT = 40;
const ACTION_VALIDATION_PACK_RUN = 44;

export default function AdminGovernancePage() {
  const [me, setMe] = useState<Me | null>(null);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [overrides, setOverrides] = useState<PromptOverrideRow[]>([]);
  const [usage, setUsage] = useState<UsageSummary | null>(null);
  const [analytics, setAnalytics] = useState<AnalyticsSummary | null>(null);
  const [auditEvents, setAuditEvents] = useState<AuditEvent[]>([]);
  const [healthByProvider, setHealthByProvider] = useState<
    Record<string, { ok: boolean; message: string; checkedAt: string } | undefined>
  >({});
  const [errors, setErrors] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const errs: string[] = [];
      const [meR, prv, rb, ov, us, an, au] = await Promise.all([
        api.me().catch((e: Error) => { errs.push(`me: ${e.message}`); return null; }),
        api.providers.list().catch((e: Error) => { errs.push(`providers: ${e.message}`); return [] as Provider[]; }),
        api.rulebooks.list().catch((e: Error) => { errs.push(`rulebooks: ${e.message}`); return [] as Rulebook[]; }),
        api.promptOverrides.list().catch((e: Error) => { errs.push(`prompts: ${e.message}`); return [] as PromptOverrideRow[]; }),
        api.usage.summary().catch((e: Error) => { errs.push(`usage: ${e.message}`); return null; }),
        api.analytics.summary().catch((e: Error) => { errs.push(`analytics: ${e.message}`); return null; }),
        api.audit.query({ take: 500 }).catch((e: Error) => { errs.push(`audit: ${e.message}`); return [] as unknown[]; }),
      ]);
      if (cancelled) return;
      setMe(meR);
      setProviders(prv);
      setRulebooks(rb);
      setOverrides(ov);
      setUsage(us);
      setAnalytics(an);
      setAuditEvents(au as AuditEvent[]);
      setErrors(errs);
      setLoading(false);
    })();
    return () => { cancelled = true; };
  }, []);

  async function probeHealth(p: Provider) {
    setHealthByProvider((m) => ({
      ...m,
      [p.id]: { ok: false, message: 'probing…', checkedAt: new Date().toISOString() },
    }));
    try {
      const r = await api.providers.health(p.id);
      setHealthByProvider((m) => ({
        ...m,
        [p.id]: {
          ok: r.ok,
          message: r.ok ? 'OK' : (r.error ?? 'unreachable'),
          checkedAt: new Date().toISOString(),
        },
      }));
    } catch (e) {
      setHealthByProvider((m) => ({
        ...m,
        [p.id]: { ok: false, message: (e as Error).message, checkedAt: new Date().toISOString() },
      }));
    }
  }

  if (loading) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Oversight dashboard</h1>
        <div className="rp-panel"><TableSkeleton rows={6} cols={6} /></div>
      </div>
    );
  }

  const role = me?.user.role;
  // Gate on the audit-oversight permission (Medical Director / Compliance
  // Reviewer / IT Admin / Auditor) rather than hard-coded role numbers, so new
  // roles inherit access from the same RolePermissionMap the backend enforces.
  if (!can(me?.user.permissions, 'audit.verify')) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Governance</h1>
        <div className="banner danger" data-testid="governance-forbidden">
          This dashboard is restricted to Medical Director, Compliance Reviewer, and IT Admin roles.
          Your account is{' '}
          <code>{typeof role === 'number' ? ROLE_LABELS[role] ?? `role ${role}` : 'unauthenticated'}</code>.
        </div>
      </div>
    );
  }

  // -- Panel 4: PHI routing --------------------------------------------------
  const phiBlocks = analytics?.governance.phiViolationsBlocked ?? 0;
  const aiRequests = analytics?.ai.totalRequests ?? usage?.totalRequests ?? 0;
  const providerBlockedAuditCount = auditEvents.filter(
    (e) => normaliseAction(e.action) === ACTION_PROVIDER_BLOCKED,
  ).length;

  // -- Panel 5: Validation results -------------------------------------------
  const validationRuns = auditEvents
    .filter((e) => normaliseAction(e.action) === ACTION_VALIDATION_PACK_RUN)
    .slice(0, 100)
    .map((e) => parseValidationRun(e));
  const validationTotals = validationRuns.reduce(
    (acc, r) => {
      acc.passed += r.passed;
      acc.failed += r.failed;
      return acc;
    },
    { passed: 0, failed: 0 },
  );

  // -- Panel 6: Drift alerts -------------------------------------------------
  const driftEvents = auditEvents
    .filter((e) => {
      const a = normaliseAction(e.action);
      return a === ACTION_SYSTEM_ALERT || a === ACTION_ANOMALY_DETECTED;
    })
    .slice(0, 50);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Oversight dashboard</h1>
      <p className="rp-page-sub">
        A single view of AI activity, rulebook approvals, and patient-data routing for your workspace. Signed in as{' '}
        <strong>{me?.user.email}</strong> ·{' '}
        <em>{typeof role === 'number' ? ROLE_LABELS[role] ?? role : 'unknown'}</em>.
      </p>

      {errors.length > 0 && (
        <Banner tone="warn">Some signals could not be loaded: {errors.join('; ')}.</Banner>
      )}

      {/* Summary metrics --------------------------------------------------- */}
      <div className="metric-grid rp-stagger rp-mb-md" data-testid="governance-summary">
        <div className="metric-card" data-tone="info">
          <div className="metric-card-value"><AnimatedNumber value={aiRequests} /></div>
          <div className="metric-card-label">AI requests · window</div>
        </div>
        <div className="metric-card" data-tone={phiBlocks > 0 ? 'review' : 'ready'}>
          <div className="metric-card-value"><AnimatedNumber value={phiBlocks} /></div>
          <div className="metric-card-label">PHI requests blocked</div>
        </div>
        <div className="metric-card" data-tone="ready">
          <div className="metric-card-value"><AnimatedNumber value={validationTotals.passed} /></div>
          <div className="metric-card-label">Validation passed</div>
        </div>
        <div className="metric-card" data-tone={validationTotals.failed > 0 ? 'blocked' : 'ready'}>
          <div className="metric-card-value"><AnimatedNumber value={validationTotals.failed} /></div>
          <div className="metric-card-label">Validation failed</div>
        </div>
      </div>

      {/* 1 — Model inventory ------------------------------------------------ */}
      <div className="rp-panel" data-testid="panel-model-inventory">
        <div className="rp-panel-title">AI models</div>
        {providers.length === 0 ? (
          <p className="rp-page-sub">No AI models set up yet.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Provider</th>
                <th>Adapter</th>
                <th>Compliance</th>
                <th>Endpoint host</th>
                <th>Retention</th>
                <th>Last health check</th>
              </tr>
            </thead>
            <tbody>
              {providers.map((p) => {
                const health = healthByProvider[p.id];
                return (
                  <tr key={p.id}>
                    <td>
                      <code>{p.id}</code>
                      <div className="rp-page-sub">{p.name}</div>
                    </td>
                    <td>{p.adapter}</td>
                    <td>
                      <span className={`badge ${complianceTone(p.compliance)}`}>
                        {COMPLIANCE_LABELS[p.compliance] ?? `class ${p.compliance}`}
                      </span>
                    </td>
                    <td><code>{hostOf(p.endpointUrl)}</code></td>
                    <td>{p.retentionLabel ? <code>{p.retentionLabel}</code> : <span className="rp-faint">—</span>}</td>
                    <td>
                      <div className="rp-row rp-gap-sm">
                        {health ? (
                          <span className={`badge ${health.ok ? 'ok' : 'danger'}`}>{health.message}</span>
                        ) : (
                          <span className="rp-faint">not yet probed</span>
                        )}
                        {(() => {
                          const probing = health?.message === 'probing…';
                          return (
                            <button className="subtle" onClick={() => probeHealth(p)} disabled={probing} aria-busy={probing}>
                              {probing && <span className="rp-spinner sm" aria-hidden />}
                              Probe
                            </button>
                          );
                        })()}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* 2 — Prompt + rulebook versions ------------------------------------ */}
      <div className="rp-panel" data-testid="panel-versions">
        <div className="rp-panel-title">Prompt + rulebook versions</div>
        <table className="rp-table">
          <thead>
            <tr>
              <th>Kind</th>
              <th>Id</th>
              <th>Version</th>
              <th>Status</th>
              <th>Last approved</th>
              <th>Approved by</th>
            </tr>
          </thead>
          <tbody>
            {rulebooks.map((r) => (
              <tr key={`rb-${r.id}`}>
                <td><span className="badge info">rulebook</span></td>
                <td><code>{r.rulebookId}</code></td>
                <td><code>{r.version}</code></td>
                <td>
                  <span className={`badge ${rulebookStatusTone(r.status)}`}>
                    {String(r.status)}
                  </span>
                </td>
                <td>{r.updatedAt ? new Date(r.updatedAt).toLocaleDateString() : '—'}</td>
                <td><span className="rp-faint">—</span></td>
              </tr>
            ))}
            {overrides.map((o) => (
              <tr key={`po-${o.id}`}>
                <td><span className="badge ai">prompt</span></td>
                <td><code>{o.rulebookId} · {o.blockKey}</code></td>
                <td><span className="rp-faint">—</span></td>
                <td>
                  <span className={`badge ${o.status === 'Approved' ? 'ok' : 'warn'}`}>
                    {o.status}
                  </span>
                </td>
                <td>{o.approvedAt ? new Date(o.approvedAt).toLocaleDateString() : '—'}</td>
                <td>{o.approvedByUserId ? <code>{o.approvedByUserId.slice(0, 8)}…</code> : <span className="rp-faint">—</span>}</td>
              </tr>
            ))}
            {rulebooks.length === 0 && overrides.length === 0 && (
              <tr><td colSpan={6} className="rp-page-sub">No rulebooks or prompt overrides.</td></tr>
            )}
          </tbody>
        </table>
        {/* Rulebooks and Prompt Studio are (desktop) routes; this page is (web), and
            build-surface.mjs stages the desktop group out of the web bundle — so these were dead
            links for the only users who can reach this screen. The governance table above already
            summarises both; authoring happens in the desktop app. */}
        <p className="rp-page-sub rp-mt-sm">
          Rulebooks and prompt overrides are authored in the RadioPad desktop app.
        </p>
      </div>

      {/* 3 — AI usage ------------------------------------------------------- */}
      <div className="rp-panel" data-testid="panel-ai-usage">
        <div className="rp-panel-title">AI usage · last 30 days</div>
        {usage ? (
          <>
            <div className="rp-stat-tile rp-mb-md">
              <div className="rp-stat-tile-row">
                <div>
                  <div className="rp-stat-label">Total cost</div>
                  <div className="rp-stat-value">${fmtUsd(usage.costTotalUsd)}</div>
                </div>
                <span className="badge info">
                  {usage.totalRequests.toLocaleString()} requests
                </span>
              </div>
              <div className="rp-stat-sub">
                {usage.inputTokens.toLocaleString()} in · {usage.outputTokens.toLocaleString()} out ·
                {' '}avg <code>{usage.avgLatencyMs} ms</code>
              </div>
            </div>
            <table className="rp-table">
              <thead>
                <tr>
                  <th>Provider</th>
                  <th>Adapter</th>
                  <th>Requests</th>
                  <th>Input</th>
                  <th>Output</th>
                  <th>Cost (USD)</th>
                </tr>
              </thead>
              <tbody>
                {usage.byProvider.length === 0 && (
                  <tr><td colSpan={6} className="rp-page-sub">No AI activity in window.</td></tr>
                )}
                {usage.byProvider.map((p) => (
                  <tr key={`${p.provider}-${p.adapter}`}>
                    <td><code>{p.provider}</code></td>
                    <td>{p.adapter}</td>
                    <td>{p.requests.toLocaleString()}</td>
                    <td>{p.inputTokens.toLocaleString()}</td>
                    <td>{p.outputTokens.toLocaleString()}</td>
                    <td>
                      <code>${fmtUsd(p.costTotalUsd)}</code>
                      {p.unpriced && <span className="rp-page-sub"> · unpriced</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ) : (
          <p className="rp-page-sub">Usage rollup unavailable.</p>
        )}
      </div>

      {/* 4 — PHI routing ---------------------------------------------------- */}
      <div className="rp-panel" data-testid="panel-phi-routing">
        <div className="rp-panel-title">PHI routing</div>
        <div className="rp-grid-2">
          <div className="rp-stat-tile">
            <div className="rp-stat-tile-row">
              <div>
                <div className="rp-stat-label">AI requests (window)</div>
                <div className="rp-stat-value">{aiRequests.toLocaleString()}</div>
              </div>
              <span className="badge info">routed</span>
            </div>
            <div className="rp-stat-sub">Total inference traffic via the AI gateway.</div>
          </div>
          <div className="rp-stat-tile">
            <div className="rp-stat-tile-row">
              <div>
                <div className="rp-stat-label">PHI requests blocked</div>
                <div className="rp-stat-value">{phiBlocks.toLocaleString()}</div>
              </div>
              <span className={`badge ${phiBlocks > 0 ? 'warn' : 'ok'}`}>
                ProviderBlocked
              </span>
            </div>
            <div className="rp-stat-sub">
              Audit hits in window: <code>{providerBlockedAuditCount}</code>.
            </div>
          </div>
        </div>
        <p className="rp-page-sub rp-mt-sm">
          PHI requests are blocked in <code>AiGateway.EnforcePhiPolicy</code> unless the provider
          carries <span className="badge ok">PHI-approved</span> or{' '}
          <span className="badge ok">Local-only</span>; every block is recorded as{' '}
          <code>ProviderBlocked</code> in the append-only audit chain.
        </p>
      </div>

      {/* 5 — Validation results -------------------------------------------- */}
      <div className="rp-panel" data-testid="panel-validation-results">
        <div className="rp-panel-title">Validation results</div>
        <div className="rp-row rp-row-wrap rp-gap-sm rp-mb-md">
          <span className="badge ok">{validationTotals.passed} passed</span>
          <span className={`badge ${validationTotals.failed > 0 ? 'danger' : 'ok'}`}>
            {validationTotals.failed} failed
          </span>
          <span className="rp-page-sub">
            across {validationRuns.length} run{validationRuns.length === 1 ? '' : 's'}
          </span>
        </div>
        {validationRuns.length === 0 ? (
          <p className="rp-page-sub">No validation pack runs in the audit window.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Pack</th>
                <th>Pass</th>
                <th>Fail</th>
              </tr>
            </thead>
            <tbody>
              {validationRuns.slice(0, 20).map((r, i) => (
                <tr key={`vr-${i}`}>
                  <td><code>{new Date(r.when).toLocaleString()}</code></td>
                  <td><code>{r.packId ?? '—'}</code></td>
                  <td><span className="badge ok">{r.passed}</span></td>
                  <td>
                    <span className={`badge ${r.failed > 0 ? 'danger' : 'ok'}`}>{r.failed}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* 6 — Drift alerts --------------------------------------------------- */}
      <div className="rp-panel" data-testid="panel-drift-alerts">
        <div className="rp-panel-title">Drift alerts</div>
        {driftEvents.length === 0 ? (
          <p className="rp-page-sub">No drift or anomaly alerts in window.</p>
        ) : (
          <ul className="rp-list">
            {driftEvents.map((e, i) => {
              const a = normaliseAction(e.action);
              const kind = a === ACTION_SYSTEM_ALERT ? 'SystemAlert' : 'AnomalyDetected';
              const tone = a === ACTION_SYSTEM_ALERT ? 'danger' : 'warn';
              return (
                <li key={`drift-${e.id ?? i}`} className="rp-divider-row rp-row between">
                  <span className="rp-cell f1">
                    <code>{new Date(e.createdAt).toLocaleString()}</code>
                  </span>
                  <span className="rp-cell f1">
                    <span className={`badge ${tone}`}>{kind}</span>
                  </span>
                  <span className="rp-cell f2 rp-faint">
                    {summariseDetails(e.detailsJson)}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
        {/* /audit is a (desktop) route and does not ship in the web bundle — see the note above. */}
        <p className="rp-page-sub">The full append-only audit log is available in the desktop app.</p>
      </div>
    </div>
  );
}

function complianceTone(c: number): 'ok' | 'warn' | 'danger' | 'info' {
  // 0 Blocked / 1 Sandbox / 2 De-identified / 3 PhiApproved / 4 LocalOnly
  if (c === 3 || c === 4) return 'ok';
  if (c === 1) return 'warn';
  if (c === 0) return 'danger';
  return 'info';
}

function rulebookStatusTone(s: number | string): 'ok' | 'warn' | 'info' {
  if (s === 2 || s === 'Approved' || s === 'approved') return 'ok';
  if (s === 1 || s === 'Deprecated') return 'warn';
  return 'info';
}

function hostOf(url: string): string {
  if (!url) return '—';
  try {
    return new URL(url).host;
  } catch {
    return url.replace(/^https?:\/\//, '').split('/')[0];
  }
}

function normaliseAction(a: number | string): number {
  return typeof a === 'number' ? a : Number.NaN;
}

function parseValidationRun(e: AuditEvent): {
  when: string;
  packId: string | null;
  passed: number;
  failed: number;
} {
  let passed = 0;
  let failed = 0;
  let packId: string | null = null;
  if (e.detailsJson) {
    try {
      const d = JSON.parse(e.detailsJson) as Record<string, unknown>;
      if (typeof d.passed === 'number') passed = d.passed;
      if (typeof d.failed === 'number') failed = d.failed;
      if (typeof d.packId === 'string') packId = d.packId;
    } catch {
      /* ignore — leave zeros */
    }
  }
  return { when: e.createdAt, packId, passed, failed };
}

function summariseDetails(json: string | null | undefined): string {
  if (!json) return '—';
  try {
    const d = JSON.parse(json) as Record<string, unknown>;
    const keys = ['kind', 'reason', 'severity', 'message', 'rulebookId'];
    for (const k of keys) {
      const v = d[k];
      if (typeof v === 'string' && v.length > 0) return v;
    }
    return JSON.stringify(d).slice(0, 120);
  } catch {
    return json.slice(0, 120);
  }
}

function fmtUsd(value: number): string {
  return (value ?? 0).toFixed(4);
}
