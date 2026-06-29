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
import { isAuthError, useAuthSession } from '@/lib/useAuthSession';
import SignInRequired from '@/components/ui/SignInRequired';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';
import { TableSkeleton } from '@/components/ui/Skeleton';

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
  const session = useAuthSession();
  const months = useMemo(() => lastNMonths(6), []);
  const [rows, setRows] = useState<MonthRow[]>([]);
  const [lifetime, setLifetime] = useState<UsageSummary | null>(null);
  const [windowed, setWindowed] = useState<AnalyticsSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authBlocked, setAuthBlocked] = useState(false);

  useEffect(() => {
    if (session.loading || session.signedOut) return;
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
                reportsTotal: 0, // full KPIs available on main analytics page
                reportsExported: 0,
                activeUsers: analytics.product.activeRadiologists,
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
        if (cancelled) return;
        if (isAuthError(e)) setAuthBlocked(true);
        else setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [months, session.loading, session.signedOut]);

  if (session.signedOut) {
    return (
      <Container>
        <h1 className="rp-page-title">Usage</h1>
        <SignInRequired surface="Please sign in to view usage for your workspace." />
      </Container>
    );
  }

  if (authBlocked) {
    return (
      <Container>
        <h1 className="rp-page-title">Usage</h1>
        <SignInRequired
          surface="You don't have access to usage analytics."
          detail="Ask your Medical Director, IT Admin, or Billing Admin for access."
        />
      </Container>
    );
  }

  return (
    <Container>
      <PageHeader
        title="Usage"
        description="How much your workspace has used RadioPad each month — active radiologists, AI requests, and reports."
      />

      <div aria-live="polite" aria-busy={loading}>
        {error && <Banner tone="warn" title="Couldn't load usage">{error}</Banner>}
        {loading && !error && <TableSkeleton rows={6} cols={4} />}
      </div>

      {windowed && lifetime && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Last 30 days</div>
          <div className="metric-grid rp-stagger">
            <Stat label="Active radiologists" value={windowed.product.activeRadiologists} numeric tone="info" />
            <Stat label="Reports passing review" value={`${(windowed.product.validationPassRate * 100).toFixed(1)}%`} percent={windowed.product.validationPassRate * 100} />
            <Stat label="Rulebook usage" value={`${(windowed.product.rulebookAdoption * 100).toFixed(1)}%`} percent={windowed.product.rulebookAdoption * 100} />
            <Stat label="AI requests" value={windowed.ai.totalRequests} numeric />
            <Stat label="AI words (in / out)" value={fmtTokens(windowed.ai.inputTokens, windowed.ai.outputTokens)} />
            <Stat label="Average AI speed" value={`${windowed.ai.avgLatencyMs} ms`} />
            <Stat label="AI cost" value={`$${fmtUsd(windowed.ai.costTotalUsd)}`} />
          </div>
          <details className="rp-advanced">
            <summary>Show all-time totals</summary>
            <p className="rp-page-sub">
              Lifetime AI requests: <strong>{lifetime.totalRequests.toLocaleString()}</strong>{' '}
              · succeeded {lifetime.okCount.toLocaleString()}{' '}
              · blocked for safety {lifetime.blockedCount.toLocaleString()}{' '}
              · errors {lifetime.errorCount.toLocaleString()}{' '}
              · total cost ${fmtUsd(lifetime.costTotalUsd)}.
            </p>
          </details>
        </div>
      )}

      <div className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Month by month</div>
        <table className="rp-table">
          <thead>
            <tr>
              <th>Month</th>
              <th>Active users</th>
              <th>Reports</th>
              <th>Exported</th>
              <th>AI requests</th>
              <th>Words in</th>
              <th>Words out</th>
            </tr>
          </thead>
          <tbody className="rp-stagger">
            {rows.length === 0 && !loading && (
              <tr>
                <td colSpan={7} className="rp-page-sub">No data yet.</td>
              </tr>
            )}
            {rows.map((r) => (
              <tr key={r.month.label}>
                <td>{r.month.label}</td>
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
        <details className="rp-panel rp-advanced rp-anim-fade-in-up">
          <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>Last month broken down by AI model</summary>
          {(() => {
            const last = rows[rows.length - 1];
            if (!last?.ai) return <p className="rp-page-sub">No AI activity.</p>;
            return (
              <table className="rp-table">
                <thead>
                  <tr>
                    <th>Provider</th>
                    <th>Model</th>
                    <th>Requests</th>
                    <th>Words in</th>
                    <th>Words out</th>
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
                      <td>{p.provider}</td>
                      <td>{p.adapter}</td>
                      <td>{p.requests}</td>
                      <td>{p.inputTokens.toLocaleString()}</td>
                      <td>{p.outputTokens.toLocaleString()}</td>
                      <td>
                        ${fmtUsd(p.costTotalUsd)}
                        {p.unpriced && (
                          <span className="rp-page-sub"> · price unknown</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            );
          })()}
        </details>
      )}
    </Container>
  );
}

function Stat({
  label,
  value,
  numeric,
  percent,
  tone,
}: {
  label: string;
  value: string | number;
  /** Render the value with a count-up animation. */
  numeric?: boolean;
  /** When set (0–100), show a fill bar beneath the value. */
  percent?: number;
  tone?: 'ready' | 'review' | 'blocked' | 'info';
}) {
  const pct = percent != null ? Math.max(0, Math.min(100, percent)) : null;
  return (
    <div className="metric-card" {...(tone ? { 'data-tone': tone } : {})}>
      <div className="metric-card-label">{label}</div>
      <div className="metric-card-value">
        {numeric && typeof value === 'number' ? <AnimatedNumber value={value} /> : value}
      </div>
      {pct != null && (
        <div
          style={{ height: 6, borderRadius: 3, overflow: 'hidden', background: 'var(--color-rule)' }}
          role="progressbar"
          aria-valuenow={Math.round(pct)}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={label}
        >
          <div
            className={`rp-bar-fill badge ${pct >= 80 ? 'ok' : pct >= 50 ? 'warn' : 'danger'}`}
            style={{ width: `${pct}%`, height: '100%', borderRadius: 0 }}
          />
        </div>
      )}
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
