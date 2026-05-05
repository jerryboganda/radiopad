'use client';

/**
 * BILL-004 — per-month usage dashboard. Surfaces seat / AI-request /
 * token / report-generation totals from `GET /api/usage/analytics` (windowed)
 * + `GET /api/usage/summary` (lifetime). Tenant-scoped server-side.
 *
 * Locked design tokens only.
 */

import { useEffect, useMemo, useState } from 'react';
import { api, type UsageSummary } from '@/lib/api';

type AnalyticsSummary = Awaited<ReturnType<typeof api.analytics.summary>>;

type Month = { from: string; to: string; label: string };

function lastNMonths(n: number): Month[] {
  const months: Month[] = [];
  const now = new Date();
  for (let i = n - 1; i >= 0; i--) {
    const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i, 1));
    const end = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i + 1, 1));
    const label = start.toLocaleDateString(undefined, { year: 'numeric', month: 'short' });
    months.push({ from: start.toISOString(), to: end.toISOString(), label });
  }
  return months;
}

type MonthRow = {
  month: Month;
  ai: UsageSummary | null;
  reportsTotal: number;
  reportsExported: number;
  activeUsers: number;
  error: string | null;
};

export default function UsageDashboardPage() {
  const months = useMemo(() => lastNMonths(6), []);
  const [rows, setRows] = useState<MonthRow[]>([]);
  const [lifetime, setLifetime] = useState<UsageSummary | null>(null);
  const [windowed, setWindowed] = useState<AnalyticsSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    (async () => {
      try {
        const [life, win] = await Promise.all([
          api.usage.summary({}),
          api.analytics.summary(),
        ]);
        if (cancelled) return;
        setLifetime(life);
        setWindowed(win);

        const perMonth = await Promise.all(
          months.map(async (m): Promise<MonthRow> => {
            try {
              const [ai, analytics] = await Promise.all([
                api.usage.summary({ from: m.from, to: m.to }),
                api.analytics.summary({ from: m.from, to: m.to }),
              ]);
              return {
                month: m,
                ai,
                reportsTotal: analytics.reports.total,
                reportsExported: analytics.reports.exported,
                activeUsers: analytics.governance.activeUsers,
                error: null,
              };
            } catch (e) {
              return {
                month: m,
                ai: null,
                reportsTotal: 0,
                reportsExported: 0,
                activeUsers: 0,
                error: (e as Error).message,
              };
            }
          }),
        );
        if (!cancelled) setRows(perMonth);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [months]);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Usage</h1>
      <p className="rp-page-sub">
        Per-month seats, AI requests, tokens, and report generations. Tenant-scoped.
        Numbers come from <code>/api/usage/summary</code> and <code>/api/usage/analytics</code>.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {loading && <p className="rp-page-sub">Loading usage…</p>}

      {windowed && lifetime && (
        <div className="rp-panel">
          <div className="rp-panel-title">Last 30 days · summary</div>
          <div className="rp-grid-3">
            <Stat label="Active users" value={windowed.governance.activeUsers} />
            <Stat label="Reports created" value={windowed.reports.total} />
            <Stat label="Reports exported" value={windowed.reports.exported} />
            <Stat label="AI requests" value={windowed.ai.totalRequests} />
            <Stat label="AI tokens (in/out)" value={fmtTokens(windowed.ai.inputTokens, windowed.ai.outputTokens)} />
            <Stat label="Avg latency" value={`${windowed.ai.avgLatencyMs} ms`} />
            <Stat label="AI cost (USD)" value={`$${fmtUsd(windowed.ai.costTotalUsd)}`} />
          </div>
          <p className="rp-page-sub rp-mt-sm">
            Lifetime AI requests: <code>{lifetime.totalRequests.toLocaleString()}</code>{' '}
            · ok <code>{lifetime.okCount}</code> · blocked{' '}
            <code>{lifetime.blockedCount}</code> · error{' '}
            <code>{lifetime.errorCount}</code> · cost{' '}
            <code>${fmtUsd(lifetime.costTotalUsd)}</code>.
          </p>
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-panel-title">Monthly breakdown</div>
        <table className="rp-table">
          <thead>
            <tr>
              <th>Month</th>
              <th>Seats</th>
              <th>Reports</th>
              <th>Exported</th>
              <th>AI requests</th>
              <th>Input tokens</th>
              <th>Output tokens</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && !loading && (
              <tr>
                <td colSpan={7} className="rp-page-sub">No data.</td>
              </tr>
            )}
            {rows.map((r) => (
              <tr key={r.month.label}>
                <td><code>{r.month.label}</code></td>
                <td>{r.activeUsers}</td>
                <td>{r.reportsTotal}</td>
                <td>{r.reportsExported}</td>
                <td>{r.ai?.totalRequests ?? '—'}</td>
                <td>{r.ai?.inputTokens.toLocaleString() ?? '—'}</td>
                <td>{r.ai?.outputTokens.toLocaleString() ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {rows.some((r) => r.ai && r.ai.byProvider.length > 0) && (
        <div className="rp-panel">
          <div className="rp-panel-title">By provider · last full month</div>
          {(() => {
            const last = rows[rows.length - 1];
            if (!last?.ai) return <p className="rp-page-sub">No provider activity.</p>;
            return (
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
                  {last.ai.byProvider.length === 0 && (
                    <tr>
                      <td colSpan={6} className="rp-page-sub">No AI activity.</td>
                    </tr>
                  )}
                  {last.ai.byProvider.map((p) => (
                    <tr key={`${p.provider}-${p.adapter}`}>
                      <td><code>{p.provider}</code></td>
                      <td>{p.adapter}</td>
                      <td>{p.requests}</td>
                      <td>{p.inputTokens.toLocaleString()}</td>
                      <td>{p.outputTokens.toLocaleString()}</td>
                      <td>
                        <code>${fmtUsd(p.costTotalUsd)}</code>
                        {p.unpriced && (
                          <span className="rp-page-sub"> · unpriced</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            );
          })()}
        </div>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div>
      <div className="rp-stat-label">{label}</div>
      <div className="rp-stat-value">{typeof value === 'number' ? value.toLocaleString() : value}</div>
    </div>
  );
}

function fmtTokens(input: number, output: number): string {
  return `${input.toLocaleString()} / ${output.toLocaleString()}`;
}

function fmtUsd(value: number): string {
  // Iter-34 BILL-005 — 4 decimal places to keep sub-cent provider deltas legible.
  return (value ?? 0).toFixed(4);
}
