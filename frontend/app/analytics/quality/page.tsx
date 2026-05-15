'use client';

import { useEffect, useState, useCallback } from 'react';
import { api } from '@/lib/api';
import type { QualityTrendsResponse } from '@/lib/api';

type GroupBy = 'day' | 'week';

function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * C7 — Quality Score Dashboard. Surfaces per-period quality trends,
 * per-radiologist and per-rulebook quality breakdowns using the
 * heuristic quality score from ValidationResult.QualityScore.
 */
export default function QualityDashboardPage() {
  const [data, setData] = useState<QualityTrendsResponse | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [fromDate, setFromDate] = useState(daysAgo(30));
  const [toDate, setToDate] = useState(today());
  const [groupBy, setGroupBy] = useState<GroupBy>('day');

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    api.analytics
      .qualityTrends({ from: fromDate, to: toDate, groupBy })
      .then(setData)
      .catch((e: Error) => setErr(e.message))
      .finally(() => setLoading(false));
  }, [fromDate, toDate, groupBy]);

  useEffect(() => {
    load();
  }, [load]);

  /* ── Derived summary stats ─────────────────────────────────────── */
  const avgScore =
    data && data.trends.length > 0
      ? Math.round(
          data.trends.reduce((s, t) => s + t.avgScore * t.reportCount, 0) /
            data.trends.reduce((s, t) => s + t.reportCount, 0),
        )
      : null;
  const totalReports = data?.trends.reduce((s, t) => s + t.reportCount, 0) ?? 0;
  const totalBlockers = data?.trends.reduce((s, t) => s + t.blockerCount, 0) ?? 0;
  const reportsAbove80 =
    data && data.trends.length > 0
      ? data.trends.reduce(
          (s, t) => s + (t.avgScore >= 80 ? t.reportCount : 0),
          0,
        )
      : 0;
  const pctAbove80 =
    totalReports > 0 ? ((reportsAbove80 / totalReports) * 100).toFixed(1) : '0.0';

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Quality Score Dashboard</h1>
      <p className="rp-page-sub">
        C7 — Heuristic quality trends computed from report validation data.
      </p>

      {/* ── Controls ─────────────────────────────────────────────── */}
      <div className="rp-panel" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">Time Window</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
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
          <button
            className={`ghost${groupBy === 'day' ? ' active' : ''}`}
            onClick={() => setGroupBy('day')}
          >
            Daily
          </button>
          <button
            className={`ghost${groupBy === 'week' ? ' active' : ''}`}
            onClick={() => setGroupBy('week')}
          >
            Weekly
          </button>
          <button className="ghost" onClick={load}>
            Apply
          </button>
        </div>
      </div>

      {err && <div className="banner warn">{err}</div>}
      {loading && !data && <p className="rp-page-sub">Loading…</p>}

      {data && (
        <>
          {/* ── Summary row ────────────────────────────────────────── */}
          <div className="rp-grid-3" style={{ marginBottom: 24 }}>
            <div className="panel" style={{ padding: 16 }}>
              <div className="rp-page-sub" style={{ marginBottom: 4 }}>
                Average Quality Score
              </div>
              <div style={{ fontSize: 36, fontFamily: 'var(--serif)' }}>
                {avgScore ?? '—'}
                {avgScore !== null && (
                  <span
                    className={`badge ${
                      avgScore >= 80 ? 'ok' : avgScore >= 50 ? 'warn' : 'danger'
                    }`}
                    style={{ marginLeft: 8, fontSize: 14 }}
                  >
                    {avgScore >= 80 ? 'good' : avgScore >= 50 ? 'fair' : 'poor'}
                  </span>
                )}
              </div>
            </div>
            <div className="panel" style={{ padding: 16 }}>
              <div className="rp-page-sub" style={{ marginBottom: 4 }}>
                Reports with Blockers
              </div>
              <div style={{ fontSize: 36, fontFamily: 'var(--serif)' }}>
                {totalBlockers}
                <span className="rp-page-sub" style={{ fontSize: 14, marginLeft: 4 }}>
                  / {totalReports}
                </span>
              </div>
            </div>
            <div className="panel" style={{ padding: 16 }}>
              <div className="rp-page-sub" style={{ marginBottom: 4 }}>
                Reports Scoring ≥80
              </div>
              <div style={{ fontSize: 36, fontFamily: 'var(--serif)' }}>
                {pctAbove80}%
              </div>
            </div>
          </div>

          {/* ── Trend chart (CSS bar chart) ────────────────────────── */}
          <div className="rp-panel" style={{ marginBottom: 24 }}>
            <div className="rp-panel-title">Quality Trend</div>
            {data.trends.length === 0 && (
              <p className="rp-page-sub">No data for the selected window.</p>
            )}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {data.trends.map((t) => (
                <div
                  key={t.period}
                  style={{ display: 'flex', alignItems: 'center', gap: 8 }}
                >
                  <code style={{ width: 100, flexShrink: 0, fontSize: 12 }}>
                    {t.period}
                  </code>
                  <div
                    style={{
                      flex: 1,
                      height: 22,
                      borderRadius: 4,
                      overflow: 'hidden',
                    }}
                    className="panel"
                  >
                    <div
                      className={`badge ${
                        t.avgScore >= 80
                          ? 'ok'
                          : t.avgScore >= 50
                            ? 'warn'
                            : 'danger'
                      }`}
                      style={{
                        width: `${t.avgScore}%`,
                        height: '100%',
                        display: 'flex',
                        alignItems: 'center',
                        paddingLeft: 6,
                        fontSize: 11,
                        borderRadius: 0,
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {t.avgScore}
                    </div>
                  </div>
                  <span className="rp-page-sub" style={{ fontSize: 11, width: 80 }}>
                    {t.reportCount} reports
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* ── By Radiologist table ───────────────────────────────── */}
          <div className="rp-panel" style={{ marginBottom: 24 }}>
            <div className="rp-panel-title">By Radiologist</div>
            {data.byRadiologist.length === 0 ? (
              <p className="rp-page-sub">No radiologist data.</p>
            ) : (
              <table className="rp-table">
                <thead>
                  <tr>
                    <th>Radiologist</th>
                    <th>Avg Score</th>
                    <th>Reports</th>
                    <th>Score</th>
                  </tr>
                </thead>
                <tbody>
                  {data.byRadiologist.map((r) => (
                    <tr key={r.userId}>
                      <td>
                        <code>{r.email}</code>
                      </td>
                      <td>{r.avgScore}</td>
                      <td>{r.reportCount}</td>
                      <td>
                        <span
                          className={`badge ${
                            r.avgScore >= 80
                              ? 'ok'
                              : r.avgScore >= 50
                                ? 'warn'
                                : 'danger'
                          }`}
                        >
                          {r.avgScore >= 80 ? 'good' : r.avgScore >= 50 ? 'fair' : 'poor'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {/* ── By Rulebook table ──────────────────────────────────── */}
          <div className="rp-panel">
            <div className="rp-panel-title">By Rulebook</div>
            {data.byRulebook.length === 0 ? (
              <p className="rp-page-sub">No rulebook data.</p>
            ) : (
              <table className="rp-table">
                <thead>
                  <tr>
                    <th>Rulebook</th>
                    <th>Avg Score</th>
                    <th>Reports</th>
                    <th>Score</th>
                  </tr>
                </thead>
                <tbody>
                  {data.byRulebook.map((rb) => (
                    <tr key={rb.rulebookId}>
                      <td>
                        <code>{rb.rulebookId}</code>
                      </td>
                      <td>{rb.avgScore}</td>
                      <td>{rb.reportCount}</td>
                      <td>
                        <span
                          className={`badge ${
                            rb.avgScore >= 80
                              ? 'ok'
                              : rb.avgScore >= 50
                                ? 'warn'
                                : 'danger'
                          }`}
                        >
                          {rb.avgScore >= 80 ? 'good' : rb.avgScore >= 50 ? 'fair' : 'poor'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </div>
  );
}
