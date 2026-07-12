'use client';

import { useMemo } from 'react';
import type { PromptOverrideVersion } from '@/lib/api';
import type { DiffLine } from '@/lib/textDiff';
import { getBlockMeta } from './blockMeta';

export interface DiffOverrideOption {
  id: string;
  blockKey: string;
}

export interface OutputDiffTabProps {
  overrides: DiffOverrideOption[];
  selectedOverrideId: string | null;
  onSelectOverride: (id: string | null) => void;
  versions: PromptOverrideVersion[];
  v1: number;
  v2: number;
  onSetV1: (n: number) => void;
  onSetV2: (n: number) => void;
  onCompare: () => void;
  diff: DiffLine[] | null;
  running: boolean;
}

export default function OutputDiffTab({
  overrides,
  selectedOverrideId,
  onSelectOverride,
  versions,
  v1,
  v2,
  onSetV1,
  onSetV2,
  onCompare,
  diff,
  running,
}: OutputDiffTabProps) {
  const counts = useMemo(() => {
    if (!diff) return { added: 0, removed: 0 };
    return {
      added: diff.filter((l) => l.type === 'added').length,
      removed: diff.filter((l) => l.type === 'removed').length,
    };
  }, [diff]);

  if (overrides.length === 0) {
    return (
      <div data-testid="tab-diff-viewer" className="rp-tab-body">
        <p className="rp-tab-intro">
          No saved overrides to compare yet. Edit a block and save a draft, then return here to see
          version history side by side.
        </p>
      </div>
    );
  }

  return (
    <div data-testid="tab-diff-viewer" className="rp-tab-body">
      <p className="rp-tab-intro">Compare two saved versions of an overridden prompt block.</p>

      <div className="section-block">
        <label htmlFor="ps-diff-override">Block</label>
        <select
          id="ps-diff-override"
          value={selectedOverrideId ?? ''}
          onChange={(e) => onSelectOverride(e.target.value || null)}
        >
          {overrides.map((o) => (
            <option key={o.id} value={o.id}>
              {getBlockMeta(o.blockKey).title} ({o.blockKey})
            </option>
          ))}
        </select>
      </div>

      {versions.length < 2 ? (
        <p className="rp-tab-intro">
          {running
            ? 'Loading versions…'
            : 'This block has only one saved version — nothing to compare yet.'}
        </p>
      ) : (
        <>
          <div className="rp-diff-controls">
            <div className="section-block">
              <label htmlFor="ps-diff-v1">Version A</label>
              <select id="ps-diff-v1" value={v1} onChange={(e) => onSetV1(Number(e.target.value))}>
                {versions.map((v, i) => (
                  <option key={i} value={i}>
                    v{v.version} — {v.status}
                  </option>
                ))}
              </select>
            </div>
            <div className="section-block">
              <label htmlFor="ps-diff-v2">Version B</label>
              <select id="ps-diff-v2" value={v2} onChange={(e) => onSetV2(Number(e.target.value))}>
                {versions.map((v, i) => (
                  <option key={i} value={i}>
                    v{v.version} — {v.status}
                  </option>
                ))}
              </select>
            </div>
            <button type="button" className="primary" onClick={onCompare} disabled={running} aria-busy={running}>
              {running && <span className="rp-spinner sm" aria-hidden />}
              Compare
            </button>
          </div>

          {diff ? (
            <>
              <div className="rp-diff-legend">
                <span className="rp-diff-legend-item added">+ {counts.added} added</span>
                <span className="rp-diff-legend-item removed">− {counts.removed} removed</span>
              </div>
              <div className="rp-diff-panel" data-testid="diff-output">
                {diff.map((line, i) => (
                  <div
                    key={i}
                    className={`rp-diff-line${line.type === 'added' ? ' rp-diff-add' : line.type === 'removed' ? ' rp-diff-remove' : ''}`}
                  >
                    <span className="rp-diff-gutter" aria-hidden>
                      {line.type === 'added' ? '+' : line.type === 'removed' ? '−' : ' '}
                    </span>
                    <code>{line.text || ' '}</code>
                  </div>
                ))}
              </div>
            </>
          ) : null}
        </>
      )}
    </div>
  );
}
