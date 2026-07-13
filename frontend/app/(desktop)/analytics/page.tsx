'use client';

import { useEffect, useState, useCallback } from 'react';
import { ArrowRight } from 'lucide-react';
import { api } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

type Summary = Awaited<ReturnType<typeof api.analytics.summary>>;

type Period = '7d' | '30d' | '90d' | 'custom';

function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * PRD §18 — Advanced Analytics Dashboard. Surfaces all product KPIs (§18.1)
 * and governance KPIs (§18.2) with date-range selection and status badges.
 */
export default function AnalyticsPage() {
  const [s, setS] = useState<Summary | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [period, setPeriod] = useState<Period>('30d');
  const [fromDate, setFromDate] = useState(daysAgo(30));
  const [toDate, setToDate] = useState(today());

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    const params =
      period === 'custom'
        ? { from: fromDate, to: toDate }
        : { period };
    api.analytics
      .summary(params)
      .then(setS)
      .catch((e: Error) => setErr(e.message))
      .finally(() => setLoading(false));
  }, [period, fromDate, toDate]);

  useEffect(() => {
    load();
  }, [load]);

  function selectPeriod(p: Period) {
    setPeriod(p);
    if (p === '7d') {
      setFromDate(daysAgo(7));
      setToDate(today());
    } else if (p === '30d') {
      setFromDate(daysAgo(30));
      setToDate(today());
    } else if (p === '90d') {
      setFromDate(daysAgo(90));
      setToDate(today());
    }
  }

  return (
    <Container>
      <PageHeader
        title="Analytics Dashboard"
        description="Productivity, quality, and governance metrics for your workspace."
        secondaryActions={
          <a
            href="/analytics/quality"
            className="ghost"
            style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}
          >
            Quality Trends
            <ArrowRight size={15} strokeWidth={1.8} aria-hidden />
          </a>
        }
      />

      {/* ── Date range picker ───────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">Time Window</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          {(['7d', '30d', '90d'] as const).map((p) => (
            <button
              key={p}
              className={`ghost${period === p ? ' active' : ''}`}
              onClick={() => selectPeriod(p)}
            >
              {p === '7d' ? '7 days' : p === '30d' ? '30 days' : '90 days'}
            </button>
          ))}
          <button
            className={`ghost${period === 'custom' ? ' active' : ''}`}
            onClick={() => setPeriod('custom')}
          >
            Custom
          </button>
          {period === 'custom' && (
            <>
              <input
                type="date"
                className="rp-input"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
              />
              <span className="rp-page-sub">→</span>
              <input
                type="date"
                className="rp-input"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
              />
              <button className="ghost" onClick={load} disabled={loading} aria-busy={loading}>
                {loading && <span className="rp-spinner sm" aria-hidden />}
                Apply
              </button>
            </>
          )}
        </div>
        {s && (
          <p className="rp-page-sub" style={{ marginTop: 8 }}>
            Window: <code>{new Date(s.window.from).toLocaleDateString()}</code> →{' '}
            <code>{new Date(s.window.to).toLocaleDateString()}</code>
          </p>
        )}
      </div>

      {err && !s && <ErrorState message={err} onRetry={load} />}
      {err && s && (
        <Banner tone="warn" title="Couldn’t refresh analytics">
          {err}
        </Banner>
      )}

      <div aria-live="polite" aria-busy={loading}>
        {loading && !s && (
          <div className="rp-panel">
            <TableSkeleton rows={5} cols={3} />
          </div>
        )}
      </div>

      {s && (
        <>
          {/* ── Product KPIs (§18.1) ──────────────────────────────── */}
          <div className="rp-panel rp-anim-fade-in-up">
            <div className="rp-panel-title">Productivity &amp; quality</div>
            <div className="rp-grid-3 rp-stagger">
              <Kpi
                label="Draft acceptance rate"
                value={pct(s.product.draftAcceptanceRate)}
                severity={rateSeverity(s.product.draftAcceptanceRate, 0.8, 0.5)}
              />
              <Kpi
                label="Impression acceptance rate"
                value={pct(s.product.impressionAcceptanceRate)}
                severity={rateSeverity(s.product.impressionAcceptanceRate, 0.8, 0.5)}
              />
              <Kpi
                label="Time saved / report"
                value={formatDuration(s.product.timeSavedPerReport)}
                severity="info"
              />
              <Kpi
                label="Validation pass rate"
                value={pct(s.product.validationPassRate)}
                severity={rateSeverity(s.product.validationPassRate, 0.9, 0.7)}
              />
              <Kpi
                label="Contradiction rate / 100"
                value={s.product.contradictionDetectionRate.toFixed(1)}
                severity={s.product.contradictionDetectionRate <= 2 ? 'ok' : s.product.contradictionDetectionRate <= 5 ? 'warn' : 'info'}
              />
              <Kpi
                label="Edit distance"
                value={pct(s.product.editDistance)}
                severity={s.product.editDistance <= 0.2 ? 'ok' : s.product.editDistance <= 0.4 ? 'warn' : 'info'}
              />
              <Kpi
                label="Active radiologists"
                value={s.product.activeRadiologists}
                severity="info"
              />
              <Kpi
                label="Rulebook adoption"
                value={pct(s.product.rulebookAdoption)}
                severity={rateSeverity(s.product.rulebookAdoption, 0.8, 0.5)}
              />
              <Kpi
                label="Provider cost / report"
                value={`$${Number(s.product.providerCostPerReport).toFixed(4)}`}
                severity="info"
              />
              <Kpi
                label="TAT impact (median)"
                value={formatDuration(s.product.turnaroundTimeImpact)}
                severity="info"
              />
              {s.product.avgQualityScore != null && (
                <Kpi
                  label="Avg quality score"
                  value={`${s.product.avgQualityScore.toFixed(1)} / 100`}
                  severity={
                    s.product.avgQualityScore >= 80
                      ? 'ok'
                      : s.product.avgQualityScore >= 50
                        ? 'warn'
                        : 'info'
                  }
                />
              )}
            </div>
          </div>

          {/* ── Governance KPIs (§18.2) ───────────────────────────── */}
          <div className="rp-panel rp-anim-fade-in-up">
            <div className="rp-panel-title">Governance KPIs (§18.2)</div>
            <div className="rp-grid-3 rp-stagger">
              <Kpi
                label="Unapproved prompt usage"
                value={s.governance.unapprovedPromptUsage}
                severity={s.governance.unapprovedPromptUsage === 0 ? 'ok' : 'warn'}
              />
              <Kpi
                label="PHI violations blocked"
                value={s.governance.phiViolationsBlocked}
                severity={s.governance.phiViolationsBlocked === 0 ? 'ok' : 'info'}
              />
              <Kpi
                label="Rulebook regression failures"
                value={s.governance.rulebookRegressionFailures}
                severity={s.governance.rulebookRegressionFailures === 0 ? 'ok' : 'warn'}
              />
              <Kpi
                label="Model drift alerts"
                value={s.governance.modelDriftAlerts}
                severity={s.governance.modelDriftAlerts === 0 ? 'ok' : 'warn'}
              />
              <Kpi
                label="Audit completeness"
                value={pct(s.governance.auditCompleteness)}
                severity={rateSeverity(s.governance.auditCompleteness, 0.95, 0.8)}
              />
            </div>
          </div>

          {/* ── AI Usage (from existing UsageSummary) ─────────────── */}
          <div className="rp-panel rp-anim-fade-in-up">
            <div className="rp-panel-title">AI Usage</div>
            <div className="rp-grid-3 rp-stagger">
              <Kpi label="AI requests" value={s.ai.totalRequests} />
              <Kpi label="OK" value={s.ai.okCount} />
              <Kpi label="Blocked" value={s.ai.blockedCount} />
              <Kpi label="Errors" value={s.ai.errorCount} />
              <Kpi label="Input tokens" value={s.ai.inputTokens.toLocaleString()} />
              <Kpi label="Output tokens" value={s.ai.outputTokens.toLocaleString()} />
              <Kpi label="Avg latency (ms)" value={s.ai.avgLatencyMs} />
              <Kpi
                label="Total AI cost"
                value={`$${Number(s.ai.costTotalUsd).toFixed(2)}`}
                severity="info"
              />
            </div>
            {s.ai.byProvider.length === 0 ? (
              <div style={{ marginTop: 16 }}>
                <EmptyState
                  title="No provider activity yet"
                  description="AI requests in this window will break down by provider here."
                />
              </div>
            ) : (
              <table className="rp-table" style={{ marginTop: 16 }}>
                <thead>
                  <tr>
                    <th>Provider</th>
                    <th>Adapter</th>
                    <th>Requests</th>
                    <th>Input</th>
                    <th>Output</th>
                    <th>Cost</th>
                  </tr>
                </thead>
                <tbody>
                  {s.ai.byProvider.map((p) => (
                    <tr key={p.provider}>
                      <td>
                        <code>{p.provider}</code>
                      </td>
                      <td>{p.adapter}</td>
                      <td>{p.requests}</td>
                      <td>{p.inputTokens.toLocaleString()}</td>
                      <td>{p.outputTokens.toLocaleString()}</td>
                      <td>
                        ${Number(p.costTotalUsd).toFixed(4)}
                        {p.unpriced && <span className="badge warn">unpriced</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </Container>
  );
}

/* ── Helpers ────────────────────────────────────────────────────────── */

function Kpi({
  label,
  value,
  severity,
}: {
  label: string;
  value: string | number;
  severity?: 'ok' | 'warn' | 'info';
}) {
  return (
    <div className="rp-panel" style={{ padding: 16 }}>
      <div className="rp-page-sub" style={{ marginBottom: 4 }}>
        {label}
      </div>
      <div className="rp-kpi-value">
        {typeof value === 'number' ? <AnimatedNumber value={value} /> : value}{' '}
        {severity && <span className={`badge ${severity}`}>{severity}</span>}
      </div>
    </div>
  );
}

function pct(ratio: number): string {
  return `${(ratio * 100).toFixed(1)}%`;
}

function rateSeverity(
  ratio: number,
  goodThreshold: number,
  warnThreshold: number,
): 'ok' | 'warn' | 'info' {
  if (ratio >= goodThreshold) return 'ok';
  if (ratio >= warnThreshold) return 'warn';
  return 'info';
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds.toFixed(0)}s`;
  if (seconds < 3600) return `${(seconds / 60).toFixed(1)}m`;
  return `${(seconds / 3600).toFixed(1)}h`;
}
