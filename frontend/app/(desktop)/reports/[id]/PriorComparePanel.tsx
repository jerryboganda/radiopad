'use client';

/**
 * RPT-009 / RC-05 — Priors comparison tray.
 *
 * Side-by-side diff of the current report against the most-recent prior:
 * current column marks additive changes with blue "Changed" / "New" chips,
 * the prior column marks superseded text with amber "Different" chips, and a
 * sync-scroll toggle keeps the two columns aligned. Data flow is unchanged —
 * `/compare-prior` first, `/prior` + client diff as fallback.
 */

import { useEffect, useRef, useState } from 'react';
import { api, type ComparePriorResult, type Report } from '@/lib/api';
import Banner from '@/components/ui/Banner';
import StatusBadge from '@/components/ui/StatusBadge';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import { GitCompareArrows, Link as LinkIcon, Plus, Check } from 'lucide-react';
import { getSectionEditor } from '@/lib/editor/sectionEditorRegistry';
import { buildComparisonStatement } from '@/lib/comparisonStatement';

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
  const [syncScroll, setSyncScroll] = useState(true);
  const [cmpState, setCmpState] = useState<'idle' | 'inserted' | 'copied'>('idle');

  const leftRef = useRef<HTMLDivElement | null>(null);
  const rightRef = useRef<HTMLDivElement | null>(null);
  const syncingRef = useRef(false);

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

  function mirrorScroll(from: HTMLDivElement | null, to: HTMLDivElement | null) {
    if (!syncScroll || !from || !to || syncingRef.current) return;
    syncingRef.current = true;
    to.scrollTop = from.scrollTop;
    // Release on the next frame so the mirrored scroll event doesn't bounce back.
    requestAnimationFrame(() => { syncingRef.current = false; });
  }

  if (loading) {
    return (
      <div className="rp-panel rp-priorcmp rp-anim-scale-in" role="status" aria-live="polite" aria-busy="true">
        <div className="rp-panel-title">Priors comparison</div>
        <div className="rp-priorcmp-grid">
          <div><Skeleton variant="text" width="55%" /><Skeleton variant="block" height={120} /></div>
          <div><Skeleton variant="text" width="55%" /><Skeleton variant="block" height={120} /></div>
        </div>
        <span className="rp-sr-only">Loading prior report…</span>
      </div>
    );
  }
  if (error) return <Banner tone="warn" className="rp-anim-scale-in">{error}</Banner>;
  if (!data) return null;

  if (!data.prior) {
    return (
      <div className="rp-panel rp-priorcmp rp-anim-scale-in">
        <div className="rp-panel-title">Priors comparison</div>
        <EmptyState
          title="No prior studies found"
          description={
            <>Nothing to compare for body part <code>{data.current.bodyPart}</code>. You can continue reviewing the current study.</>
          }
        />
      </div>
    );
  }

  const changedCount = data.sections.filter((s) => s.changed).length;

  // F5 — a conventional "Compared to …" sentence built from the prior's body part + date. It is
  // deterministic (no invented interval change); the radiologist inserts it into Comparison and
  // writes the actual comparison of findings themselves.
  const comparison = buildComparisonStatement(data.prior);
  function insertComparison() {
    const editor = getSectionEditor('comparison');
    if (editor) {
      editor.focus();
      editor.insertAtCursor(comparison);
      setCmpState('inserted');
    } else if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      void navigator.clipboard.writeText(comparison).then(() => setCmpState('copied')).catch(() => undefined);
    }
    window.setTimeout(() => setCmpState('idle'), 2500);
  }

  return (
    <div className="rp-panel rp-priorcmp rp-anim-scale-in">
      <div className="rp-priorcmp-head">
        <div className="rp-panel-title" style={{ marginBottom: 0 }}>
          Priors comparison
          {changedCount > 0 ? (
            <StatusBadge tone="info">{changedCount} changed</StatusBadge>
          ) : (
            <StatusBadge tone="success">Identical sections</StatusBadge>
          )}
        </div>
        <button
          type="button"
          className={`ghost rp-priorcmp-sync${syncScroll ? ' is-on' : ''}`}
          aria-pressed={syncScroll}
          onClick={() => setSyncScroll((v) => !v)}
        >
          <GitCompareArrows size={13} aria-hidden /> Sync scroll {syncScroll ? 'on' : 'off'}
        </button>
      </div>
      <p className="rp-page-sub">
        Compared against report <code>{data.prior.id.slice(0, 8)}</code> from{' '}
        <code>{fmtDate(data.prior.createdAt)}</code>.
      </p>

      {comparison && (
        <div
          className="rp-card"
          style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 14px', marginBottom: 12 }}
        >
          <span style={{ flex: 1 }}>
            <span className="rp-page-sub" style={{ display: 'block' }}>Comparison statement</span>
            <span style={{ fontWeight: 500 }}>{comparison}</span>
          </span>
          <button
            type="button"
            className="primary-ghost"
            onClick={insertComparison}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap' }}
          >
            {cmpState === 'idle' ? (
              <><Plus size={14} strokeWidth={2} aria-hidden /> Insert into Comparison</>
            ) : (
              <><Check size={14} strokeWidth={2} aria-hidden /> {cmpState === 'copied' ? 'Copied' : 'Inserted'}</>
            )}
          </button>
        </div>
      )}

      <div className="rp-priorcmp-grid">
        <div className="rp-priorcmp-col">
          <div className="rp-priorcmp-col-head">Current report</div>
          <div
            className="rp-priorcmp-scroll"
            ref={leftRef}
            onScroll={() => mirrorScroll(leftRef.current, rightRef.current)}
          >
            {data.sections.map((s) => {
              const label = sectionLabel(s.section);
              const isNew = s.changed && !s.prior.trim() && Boolean(s.current.trim());
              return (
                <div key={s.section} className="section-block">
                  <label>
                    {label}
                    {s.changed && (
                      <span className={`badge ${isNew ? 'ai' : 'info'} rp-priorcmp-chip`}>
                        {isNew ? 'New' : 'Changed'}
                      </span>
                    )}
                  </label>
                  <pre className={`rp-rewrite-pre ${s.changed ? 'rp-priorcmp-changed' : ''}`}>
                    {s.current || '(empty)'}
                  </pre>
                </div>
              );
            })}
          </div>
        </div>

        <div className="rp-priorcmp-col">
          <div className="rp-priorcmp-col-head">
            Prior report — {fmtDate(data.prior.createdAt)}
          </div>
          <div
            className="rp-priorcmp-scroll"
            ref={rightRef}
            onScroll={() => mirrorScroll(rightRef.current, leftRef.current)}
          >
            {data.sections.map((s) => {
              const label = sectionLabel(s.section);
              return (
                <div key={s.section} className="section-block">
                  <label>
                    {label}
                    {s.changed && (
                      <span className="badge warn rp-priorcmp-chip">
                        <LinkIcon size={10} aria-hidden /> Different
                      </span>
                    )}
                  </label>
                  <pre className={`rp-rewrite-pre ${s.changed ? 'rp-priorcmp-different' : ''}`}>
                    {s.prior || '(empty)'}
                  </pre>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}

function sectionLabel(section: string): string {
  return SECTIONS.find((x) => x.key === (section as keyof Report))?.label ?? section;
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
