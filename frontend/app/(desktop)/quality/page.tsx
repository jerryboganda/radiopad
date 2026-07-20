'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { ArrowRight, Users } from 'lucide-react';
import { api } from '@/lib/api';
import type { PeerReviewStats, QualityTrendsResponse, Report, ValidationResult } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import { usePermissions } from '@/lib/permissions';
import { formatRate, isOpen } from '@/lib/peerReview';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

type Summary = Awaited<ReturnType<typeof api.analytics.summary>>;

type OutcomeRow = {
  report: Report;
  result: ValidationResult | null;
  err?: string;
};

function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function statusName(status: Report['status']): string {
  return typeof status === 'string'
    ? status
    : ['Draft', 'Validated', 'Acknowledged', 'Exported'][status as number] ?? 'Draft';
}

function severityName(sev: string | number): string {
  return typeof sev === 'string'
    ? sev
    : ['Info', 'Warning', 'Blocker'][sev as number] ?? 'Info';
}

/**
 * Quality & Peer Review — umbrella page for report quality.
 * Pulls the same analytics data as the full quality dashboard
 * (/analytics/quality) and shows a compact overview plus recent
 * validation outcomes. The peer-review panel (PRD §14.13) summarises the
 * live double-read programme and links to /peer-review for the full queue,
 * scoring form, and concordance dashboard.
 */
export default function QualityHubPage() {
  const [summary, setSummary] = useState<Summary | null>(null);
  const [trends, setTrends] = useState<QualityTrendsResponse | null>(null);
  const [finalizedCount, setFinalizedCount] = useState<number | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const [outcomes, setOutcomes] = useState<OutcomeRow[]>([]);
  const [outcomesBusy, setOutcomesBusy] = useState(true);
  const [outcomesErr, setOutcomesErr] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    setOutcomesBusy(true);
    setOutcomesErr(null);

    const window = { from: daysAgo(30), to: today() };

    Promise.allSettled([
      api.analytics.summary({ period: '30d' }),
      api.analytics.qualityTrends({ ...window, groupBy: 'day' }),
      api.reports.list(),
    ]).then(async ([sRes, tRes, rRes]) => {
      if (sRes.status === 'fulfilled') setSummary(sRes.value);
      if (tRes.status === 'fulfilled') setTrends(tRes.value);

      if (sRes.status === 'rejected' && tRes.status === 'rejected') {
        setErr((sRes.reason as Error).message);
      } else if (sRes.status === 'rejected' || tRes.status === 'rejected') {
        const bad = sRes.status === 'rejected' ? sRes.reason : (tRes as PromiseRejectedResult).reason;
        setErr((bad as Error).message);
      }
      setLoading(false);

      // Recent validation outcomes: re-check the latest few unfinished drafts.
      if (rRes.status === 'fulfilled') {
        const reports = rRes.value;
        setFinalizedCount(reports.filter((r) => statusName(r.status) === 'Exported').length);
        const recent = reports
          .filter((r) => statusName(r.status) !== 'Exported')
          .sort((a, b) => (b.updatedAt ?? '').localeCompare(a.updatedAt ?? ''))
          .slice(0, 5);
        const rows: OutcomeRow[] = [];
        for (const r of recent) {
          try {
            const result = await api.reports.validate(r.id);
            rows.push({ report: r, result });
          } catch (e) {
            rows.push({ report: r, result: null, err: (e as Error).message });
          }
        }
        setOutcomes(rows);
      } else {
        setOutcomesErr((rRes.reason as Error).message);
      }
      setOutcomesBusy(false);
    });
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  /* ── Derived numbers (same math as the full quality dashboard) ──── */
  const totalReports = trends?.trends.reduce((s, t) => s + t.reportCount, 0) ?? 0;
  const totalBlockers = trends?.trends.reduce((s, t) => s + t.blockerCount, 0) ?? 0;
  const avgScore =
    trends && totalReports > 0
      ? Math.round(
          trends.trends.reduce((s, t) => s + t.avgScore * t.reportCount, 0) / totalReports,
        )
      : null;
  const reportsAbove80 =
    trends?.trends.reduce((s, t) => s + (t.avgScore >= 80 ? t.reportCount : 0), 0) ?? 0;
  const pctAbove80 = totalReports > 0 ? ((reportsAbove80 / totalReports) * 100).toFixed(1) : null;
  const passRate = summary ? summary.product.validationPassRate : null;

  const coreLoaded = summary !== null || trends !== null;

  return (
    <Container>
      <PageHeader
        title="Quality & Peer Review"
        description="How your team's reports are holding up — quality scores, validation outcomes, and peer-reviewed second reads."
        secondaryActions={
          <Link
            href="/analytics/quality"
            className="ghost"
            style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}
          >
            Open full quality analytics
            <ArrowRight size={15} strokeWidth={1.8} aria-hidden />
          </Link>
        }
      />

      {err && !coreLoaded && <ErrorState message={err} onRetry={load} />}
      {err && coreLoaded && (
        <Banner tone="warn" title="Some quality data couldn't be loaded">
          {err}
        </Banner>
      )}

      <div aria-live="polite" aria-busy={loading}>
        {loading && !coreLoaded && (
          <div className="rp-panel">
            <TableSkeleton rows={4} cols={3} />
          </div>
        )}
      </div>

      {coreLoaded && (
        <>
          {/* ── KPI row ─────────────────────────────────────────────── */}
          <div className="rp-panel rp-anim-fade-in-up">
            <div className="rp-panel-title">Last 30 days</div>
            <div className="metric-grid rp-stagger">
              <div
                className="metric-card"
                data-tone={
                  passRate === null ? 'info' : passRate >= 0.9 ? 'ready' : passRate >= 0.7 ? 'review' : 'blocked'
                }
              >
                <div className="metric-card-value">
                  {passRate === null ? '—' : (
                    <>
                      <AnimatedNumber value={Number((passRate * 100).toFixed(1))} decimals={1} />%
                    </>
                  )}
                </div>
                <div className="metric-card-label">Validation pass rate</div>
              </div>
              <div className="metric-card" data-tone={totalBlockers > 0 ? 'blocked' : 'ready'}>
                <div className="metric-card-value">
                  <AnimatedNumber value={totalBlockers} />{' '}
                  <span className={`badge ${totalBlockers > 0 ? 'danger' : 'ok'}`}>
                    {totalBlockers > 0 ? 'caught' : 'all clear'}
                  </span>
                </div>
                <div className="metric-card-label">Blockers caught</div>
              </div>
              <div className="metric-card" data-tone="info">
                <div className="metric-card-value">
                  {finalizedCount === null ? '—' : <AnimatedNumber value={finalizedCount} />}
                </div>
                <div className="metric-card-label">Reports finalized</div>
              </div>
            </div>
          </div>

          {/* ── Quality trends at a glance ──────────────────────────── */}
          <div className="rp-panel rp-anim-fade-in-up">
            <div className="rp-panel-title">Quality trends</div>
            {!trends || trends.trends.length === 0 ? (
              <EmptyState
                title="No quality data yet"
                description="Once reports are validated, their quality scores will show up here."
              />
            ) : (
              <>
                <div className="rp-grid-3 rp-stagger">
                  <div className="rp-stat-tile">
                    <div className="rp-stat-label">Average quality score</div>
                    <div className="rp-stat-value">
                      {avgScore !== null ? <AnimatedNumber value={avgScore} /> : '—'}
                      {avgScore !== null && (
                        <span
                          className={`badge ${avgScore >= 80 ? 'ok' : avgScore >= 50 ? 'warn' : 'danger'}`}
                          style={{ marginLeft: 8 }}
                        >
                          {avgScore >= 80 ? 'good' : avgScore >= 50 ? 'fair' : 'poor'}
                        </span>
                      )}
                    </div>
                  </div>
                  <div className="rp-stat-tile">
                    <div className="rp-stat-label">Reports checked</div>
                    <div className="rp-stat-value">
                      <AnimatedNumber value={totalReports} />
                    </div>
                  </div>
                  <div className="rp-stat-tile">
                    <div className="rp-stat-label">Scoring 80 or higher</div>
                    <div className="rp-stat-value">
                      {pctAbove80 !== null ? (
                        <>
                          <AnimatedNumber value={Number(pctAbove80)} decimals={1} />%
                        </>
                      ) : (
                        '—'
                      )}
                    </div>
                  </div>
                </div>
                <p className="rp-page-sub" style={{ marginTop: 12 }}>
                  Daily and weekly trends, plus per-radiologist and per-rulebook breakdowns, live in
                  the full dashboard.{' '}
                  <Link href="/analytics/quality">Open full quality analytics →</Link>
                </p>
              </>
            )}
          </div>
        </>
      )}

      {/* ── Validation outcomes ─────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={outcomesBusy}>
        <div className="rp-panel-title">Validation outcomes</div>
        <p className="rp-page-sub" style={{ marginBottom: 12 }}>
          A fresh check of your team&apos;s most recent unfinished drafts. For the whole worklist,
          use the <Link href="/validation">quality check</Link> page.
        </p>
        {outcomesBusy ? (
          <TableSkeleton rows={5} cols={5} />
        ) : outcomesErr ? (
          <ErrorState
            title="Couldn't check recent drafts"
            message={outcomesErr}
            onRetry={load}
          />
        ) : outcomes.length === 0 ? (
          <EmptyState
            title="No drafts to check right now"
            description="When your team has draft reports, their latest validation results will appear here."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Accession</th>
                <th>Modality</th>
                <th>Body part</th>
                <th>Score</th>
                <th>Issues found</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {outcomes.map(({ report, result, err: rowErr }) => {
                const findings = result?.findings ?? [];
                const blockers = findings.filter((f) => severityName(f.severity) === 'Blocker').length;
                const warnings = findings.filter((f) => severityName(f.severity) === 'Warning').length;
                return (
                  <tr key={report.id}>
                    <td>{report.study.accessionNumber || '—'}</td>
                    <td>{report.study.modality}</td>
                    <td>{report.study.bodyPart}</td>
                    <td>
                      {result ? (
                        <span
                          className={`badge ${
                            result.qualityScore >= 80 ? 'ok' : result.qualityScore >= 50 ? 'warn' : 'danger'
                          }`}
                        >
                          {result.qualityScore}
                        </span>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>
                      {rowErr ? (
                        <span className="badge warn">{rowErr}</span>
                      ) : (
                        <>
                          {blockers > 0 && <span className="badge danger">{blockers} must-fix</span>}{' '}
                          {warnings > 0 && <span className="badge warn">{warnings} warning</span>}{' '}
                          {findings.length === 0 && <span className="badge ok">all clear</span>}
                        </>
                      )}
                    </td>
                    <td>
                      <Link href={reportHref(report.id)}>Open →</Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* ── Peer review (PRD §14.13, PR-001..010) ───────────────────── */}
      <PeerReviewSummaryPanel />
    </Container>
  );
}

/**
 * Compact peer-review summary for the quality hub: how many second reads are
 * waiting on ME, and the tenant's concordance rate for anyone who runs the
 * programme. The full queue, scoring form, and per-reader dashboard live on
 * /peer-review — this panel is the doorway, not a second implementation.
 */
function PeerReviewSummaryPanel() {
  const { can, loading: permsLoading } = usePermissions();
  const isProgrammeAdmin = can('peer_review.manage');

  const [open, setOpen] = useState<number | null>(null);
  const [stats, setStats] = useState<PeerReviewStats | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);

  const load = useCallback(() => {
    setBusy(true);
    setErr(null);
    Promise.allSettled([
      api.peerReview.mine(),
      isProgrammeAdmin ? api.peerReview.stats() : Promise.resolve(null),
    ]).then(([mineRes, statsRes]) => {
      if (mineRes.status === 'fulfilled') {
        setOpen(mineRes.value.filter(isOpen).length);
      } else {
        setErr((mineRes.reason as Error).message);
      }
      if (statsRes.status === 'fulfilled' && statsRes.value) setStats(statsRes.value);
      setBusy(false);
    });
  }, [isProgrammeAdmin]);

  useEffect(() => {
    if (!permsLoading) load();
  }, [permsLoading, load]);

  return (
    <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={busy}>
      <div className="rp-panel-title">
        <Users size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
        Peer review
      </div>
      <p className="rp-page-sub" style={{ marginBottom: 12, maxWidth: 680 }}>
        Second reads of signed reports, scored on the RADPEER scale. A quality benchmark for
        inter-reader agreement — it never changes or re-signs a report.
      </p>

      {busy ? (
        <TableSkeleton rows={2} cols={3} />
      ) : err ? (
        <ErrorState title="Couldn't load peer review" message={err} onRetry={load} />
      ) : (
        <>
          <div className="rp-grid-3 rp-stagger">
            <div className="rp-stat-tile">
              <div className="rp-stat-label">Waiting on you</div>
              <div className="rp-stat-value">
                {open === null ? '—' : <AnimatedNumber value={open} />}
                {open !== null && open > 0 && (
                  <span className="badge info" style={{ marginLeft: 8 }}>
                    to review
                  </span>
                )}
              </div>
            </div>
            {isProgrammeAdmin && (
              <>
                <div className="rp-stat-tile">
                  <div className="rp-stat-label">Concordance rate</div>
                  <div className="rp-stat-value">{formatRate(stats?.totals.concordanceRate)}</div>
                </div>
                <div className="rp-stat-tile">
                  <div className="rp-stat-label">Discrepancies recorded</div>
                  <div className="rp-stat-value">
                    {stats ? <AnimatedNumber value={stats.totals.discrepancies} /> : '—'}
                  </div>
                </div>
              </>
            )}
          </div>
          <p className="rp-page-sub" style={{ marginTop: 12 }}>
            <Link href="/peer-review">Open peer review →</Link>
          </p>
        </>
      )}
    </div>
  );
}
