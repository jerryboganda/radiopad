'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type Summary = Awaited<ReturnType<typeof api.analytics.summary>>;

/**
 * PRD §18 — Analytics dashboard. Surfaces the product KPIs (validation pass
 * rate, exported reports, AI requests) and the governance KPIs (PHI policy
 * blocks, policy violations, rulebook approvals, active users) for the last
 * 30 days. All numbers come from `/api/usage/analytics` which scopes by
 * tenant via `TenantedController.ResolveContextAsync`.
 */
export default function AnalyticsPage() {
  const [s, setS] = useState<Summary | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    api.analytics.summary().then(setS).catch((e: Error) => setErr(e.message));
  }, []);

  if (err) return <div className="banner warn">{err}</div>;
  if (!s) return <p className="rp-page-sub">Loading…</p>;

  const pass = (s.reports.validationPassRate * 100).toFixed(1);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Analytics</h1>
      <p className="rp-page-sub">
        Window: <code>{new Date(s.window.from).toLocaleDateString()}</code> →{' '}
        <code>{new Date(s.window.to).toLocaleDateString()}</code>. Tenant-scoped.
      </p>

      <div className="rp-panel">
        <div className="rp-panel-title">Reporting KPIs</div>
        <div className="rp-grid-3">
          <Kpi label="Total reports" value={s.reports.total} />
          <Kpi label="Validated" value={s.reports.validated} />
          <Kpi label="Exported" value={s.reports.exported} />
          <Kpi label="Validation pass rate" value={`${pass}%`} />
          <Kpi label="Active users" value={s.governance.activeUsers} />
          <Kpi label="Rulebook approvals" value={s.governance.rulebookApprovals} />
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">AI usage</div>
        <div className="rp-grid-3">
          <Kpi label="AI requests" value={s.ai.totalRequests} />
          <Kpi label="OK" value={s.ai.okCount} />
          <Kpi label="Blocked" value={s.ai.blockedCount} />
          <Kpi label="Errors" value={s.ai.errorCount} />
          <Kpi label="Input tokens" value={s.ai.inputTokens.toLocaleString()} />
          <Kpi label="Output tokens" value={s.ai.outputTokens.toLocaleString()} />
        </div>
        <table className="rp-table" style={{ marginTop: 16 }}>
          <thead>
            <tr><th>Provider</th><th>Adapter</th><th>Requests</th><th>Input</th><th>Output</th></tr>
          </thead>
          <tbody>
            {s.ai.byProvider.length === 0 && (
              <tr><td colSpan={5} style={{ color: 'var(--text-muted)' }}>No AI activity in window.</td></tr>
            )}
            {s.ai.byProvider.map((p) => (
              <tr key={p.provider}>
                <td><code>{p.provider}</code></td>
                <td>{p.adapter}</td>
                <td>{p.requests}</td>
                <td>{p.inputTokens.toLocaleString()}</td>
                <td>{p.outputTokens.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Governance</div>
        <div className="rp-grid-3">
          <Kpi label="PHI policy blocks" value={s.governance.phiPolicyBlocks} severity={s.governance.phiPolicyBlocks > 0 ? 'info' : 'ok'} />
          <Kpi label="Policy violations" value={s.governance.policyViolations} severity={s.governance.policyViolations > 0 ? 'warn' : 'ok'} />
          <Kpi label="Avg latency (ms)" value={s.ai.avgLatencyMs} />
        </div>
      </div>
    </div>
  );
}

function Kpi({ label, value, severity }: { label: string; value: string | number; severity?: 'ok' | 'warn' | 'info' }) {
  return (
    <div className="rp-panel" style={{ padding: 16 }}>
      <div className="rp-page-sub" style={{ marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 28, fontFamily: 'var(--serif)' }}>
        {value}{' '}
        {severity && <span className={`badge ${severity}`}>{severity}</span>}
      </div>
    </div>
  );
}
