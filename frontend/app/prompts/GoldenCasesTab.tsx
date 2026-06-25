'use client';

import { useMemo } from 'react';
import type { GoldenCaseResult } from '@/lib/api';

export interface GoldenCasesTabProps {
  results: GoldenCaseResult[] | null;
  onRun: () => void;
  running: boolean;
  disabled: boolean;
}

export default function GoldenCasesTab({ results, onRun, running, disabled }: GoldenCasesTabProps) {
  const summary = useMemo(() => {
    if (!results) return null;
    const passed = results.filter((r) => r.passed).length;
    return { passed, failed: results.length - passed, total: results.length };
  }, [results]);

  return (
    <div data-testid="tab-golden-cases" className="rp-tab-body">
      <p className="rp-tab-intro">
        Run this rulebook&apos;s golden cases — curated findings with known expected rule hits — to
        catch regressions. Results are indicative while the scoring engine is being finalised.
      </p>

      <div className="rp-actions rp-mb-md">
        <button type="button" className="primary" disabled={running || disabled} onClick={onRun}>
          {running ? 'Running…' : 'Run all cases'}
        </button>
      </div>

      {summary ? (
        <div className="rp-stat-strip">
          <div className="rp-stat-tile">
            <div className="rp-stat-label">Passed</div>
            <div className="rp-stat-value">
              {summary.passed}/{summary.total}
              <span className={`badge ${summary.failed === 0 ? 'ok' : 'warn'}`}>
                {summary.total > 0 ? Math.round((summary.passed / summary.total) * 100) : 0}%
              </span>
            </div>
          </div>
          <div className="rp-stat-tile">
            <div className="rp-stat-label">Failed</div>
            <div className="rp-stat-value">
              <span className={`badge ${summary.failed > 0 ? 'danger' : 'ok'}`}>{summary.failed}</span>
            </div>
          </div>
        </div>
      ) : null}

      {results && results.length > 0 ? (
        <table className="rp-table rp-mt-sm">
          <thead>
            <tr>
              <th>Case</th>
              <th>Result</th>
              <th>Quality</th>
              <th>Expected rules</th>
              <th>Actual rules</th>
            </tr>
          </thead>
          <tbody>
            {results.map((r) => (
              <tr key={r.caseName}>
                <td>{r.caseName}</td>
                <td>
                  <span className={`badge ${r.passed ? 'ok' : 'danger'}`}>{r.passed ? 'Pass' : 'Fail'}</span>
                </td>
                <td>{Math.round(r.qualityScore * 100)}%</td>
                <td>
                  <code>{r.expectedRules.join(', ') || '—'}</code>
                </td>
                <td>
                  <code>{r.actualRules.join(', ') || '—'}</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : results && results.length === 0 ? (
        <p className="rp-tab-intro">No golden cases are defined for this rulebook yet.</p>
      ) : null}
    </div>
  );
}
