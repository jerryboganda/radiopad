'use client';

import type { ValidationResult } from '@/lib/api';

type ValidationFindingSeverity = ValidationResult['findings'][number]['severity'];

export interface TestRunnerTabProps {
  value: string;
  onChange: (value: string) => void;
  onRun: () => void;
  running: boolean;
  result: ValidationResult | null;
  error: string | null;
  disabled: boolean;
}

/** qualityScore may arrive as 0..1 or 0..100 depending on the path; normalise to %. */
function toPercent(score: number): number {
  const pct = score <= 1 ? score * 100 : score;
  return Math.max(0, Math.min(100, Math.round(pct)));
}

function scoreTone(pct: number): string {
  if (pct >= 80) return 'ok';
  if (pct >= 50) return 'warn';
  return 'danger';
}

function severityClass(sev: ValidationFindingSeverity): string {
  if (sev === 'Blocker') return 'blocker';
  if (sev === 'Warning') return 'warning';
  return 'info';
}

export default function TestRunnerTab({
  value,
  onChange,
  onRun,
  running,
  result,
  error,
  disabled,
}: TestRunnerTabProps) {
  const pct = result ? toPercent(result.qualityScore) : 0;

  return (
    <div data-testid="tab-test-runner" className="rp-tab-body">
      <p className="rp-tab-intro">
        Paste sample findings and run them through this rulebook&apos;s validation rules. Nothing is
        saved — your reports are never touched.
      </p>

      <div className="section-block">
        <label htmlFor="ps-test-input">Findings input</label>
        <textarea
          id="ps-test-input"
          className="rp-prompt-textarea findings"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="Paste findings text here…"
          rows={7}
          spellCheck={false}
        />
      </div>

      <div className="rp-actions rp-mb-md">
        <button
          type="button"
          className="primary"
          disabled={running || disabled || !value.trim()}
          onClick={onRun}
        >
          {running ? 'Running…' : 'Run test'}
        </button>
      </div>

      {error ? <div className="rp-banner danger">{error}</div> : null}

      {result ? (
        <>
          <div className="rp-stat-strip">
            <div className="rp-stat-tile">
              <div className="rp-stat-label">Quality score</div>
              <div className="rp-stat-value">
                {pct}%<span className={`badge ${scoreTone(pct)}`}>{scoreTone(pct) === 'ok' ? 'Good' : scoreTone(pct) === 'warn' ? 'Fair' : 'Low'}</span>
              </div>
            </div>
            <div className="rp-stat-tile">
              <div className="rp-stat-label">Blockers</div>
              <div className="rp-stat-value">
                <span className={`badge ${result.blockerPresent ? 'danger' : 'ok'}`}>
                  {result.blockerPresent ? 'Present' : 'None'}
                </span>
              </div>
            </div>
            <div className="rp-stat-tile">
              <div className="rp-stat-label">Findings</div>
              <div className="rp-stat-value">{result.findings.length}</div>
            </div>
          </div>

          {result.findings.length > 0 ? (
            <ul className="rp-list rp-mt-sm">
              {result.findings.map((f, i) => (
                <li key={`${f.ruleId}-${i}`} className={`finding ${severityClass(f.severity)}`}>
                  {f.message}
                  <div className="rule">{f.ruleId}</div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="rp-tab-intro rp-mt-sm">No rule findings — these findings pass the rulebook clean.</p>
          )}
        </>
      ) : null}
    </div>
  );
}
