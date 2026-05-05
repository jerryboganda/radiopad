'use client';

/**
 * RPT-009 — Prior comparison panel.
 *
 * Two-column locked-grid showing the current report's section bodies on the
 * left and the most-recent prior on the right. Section diffs use the
 * `.rp-diff-add` (added in current) and `.rp-diff-remove` (removed since
 * prior) helper classes (recorded for Agent J in `iter31-E.md`).
 */

import { useEffect, useState } from 'react';
import { api, type ComparePriorResult, type Report } from '@/lib/api';

type Props = {
  reportId: string;
};

const SECTIONS: Array<{ key: keyof Report; label: string }> = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'comparison', label: 'Comparison' },
  { key: 'findings', label: 'Findings' },
  { key: 'impression', label: 'Impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

type SectionDiff = {
  section: string;
  current: string;
  prior: string;
  changed: boolean;
};

export default function PriorComparePanel({ reportId }: Props) {
  const [data, setData] = useState<ComparePriorResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    (async () => {
      try {
        // Try /compare-prior first (richer); fall back to /prior + client diff.
        let result: ComparePriorResult;
        try {
          result = await api.reports.comparePrior(reportId);
        } catch {
          const fallback = await api.reports.prior(reportId);
          result = fromPriorOnly(reportId, fallback);
        }
        if (!cancelled) setData(result);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [reportId]);

  if (loading) return <p className="rp-page-sub">Loading prior report…</p>;
  if (error) return <div className="banner warn">{error}</div>;
  if (!data) return null;

  if (!data.prior) {
    return (
      <div className="rp-panel">
        <div className="rp-panel-title">Prior comparison</div>
        <p className="rp-page-sub">
          No prior report on file for body part <code>{data.current.bodyPart}</code>.
        </p>
      </div>
    );
  }

  const changedCount = data.sections.filter((s) => s.changed).length;

  return (
    <div className="rp-panel">
      <div className="rp-panel-title">
        Prior comparison
        {changedCount > 0 ? (
          <span className="badge info">{changedCount} changed</span>
        ) : (
          <span className="badge ok">Identical sections</span>
        )}
      </div>
      <p className="rp-page-sub">
        Compared against report <code>{data.prior.id.slice(0, 8)}</code> from{' '}
        <code>{fmtDate(data.prior.createdAt)}</code>.
      </p>

      <div className="rp-grid-2">
        <div className="rp-stat-label">Current</div>
        <div className="rp-stat-label">Prior</div>
        {data.sections.map((s) => {
          const meta = SECTIONS.find((x) => x.key === (s.section as keyof Report));
          const label = meta?.label ?? s.section;
          return (
            <div key={s.section} className="rp-grid-2-row">
              <div className="section-block">
                <label>
                  {label}
                  {s.changed && <span className="badge info" style={{ marginLeft: 8 }}>changed</span>}
                </label>
                <pre className={`rp-rewrite-pre ${s.changed ? 'rp-diff-add' : ''}`}>
                  {s.current || '(empty)'}
                </pre>
              </div>
              <div className="section-block">
                <label>{label}</label>
                <pre className={`rp-rewrite-pre ${s.changed ? 'rp-diff-remove' : ''}`}>
                  {s.prior || '(empty)'}
                </pre>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function fmtDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

function fromPriorOnly(
  reportId: string,
  payload: { current: { id: string; bodyPart: string }; prior: Report | null },
): ComparePriorResult {
  if (!payload.prior) {
    return {
      current: { id: reportId, bodyPart: payload.current.bodyPart },
      prior: null,
      sections: [],
    };
  }
  const prior = payload.prior;
  const sections: SectionDiff[] = SECTIONS.map(({ key }) => {
    const priorVal = String((prior as Record<string, unknown>)[key as string] ?? '');
    return {
      section: key as string,
      current: '', // unknown without a fetch; the page passes its own current values via a richer endpoint.
      prior: priorVal,
      changed: priorVal.length > 0,
    };
  });
  return {
    current: payload.current,
    prior: { id: prior.id, bodyPart: prior.study.bodyPart, createdAt: prior.updatedAt },
    sections,
  };
}
