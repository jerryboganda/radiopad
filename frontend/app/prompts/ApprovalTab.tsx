'use client';

import { useMemo } from 'react';
import { relativeTime } from '@/lib/rulebookStatus';
import { getBlockMeta } from './blockMeta';
import type { PromptOverride } from './promptStudioTypes';

export interface ApprovalTabProps {
  overrides: PromptOverride[];
  canApprove: boolean;
  approvingId: string | null;
  onApprove: (id: string) => void;
}

export default function ApprovalTab({ overrides, canApprove, approvingId, onApprove }: ApprovalTabProps) {
  // Drafts first (they need action), then approved — each group newest-first.
  const sorted = useMemo(() => {
    const rank = (o: PromptOverride) => (o.status === 'Draft' ? 0 : 1);
    return [...overrides].sort((a, b) => {
      if (rank(a) !== rank(b)) return rank(a) - rank(b);
      return (b.updatedAt ?? '').localeCompare(a.updatedAt ?? '');
    });
  }, [overrides]);

  if (overrides.length === 0) {
    return (
      <div data-testid="tab-approval" className="rp-tab-body">
        <p className="rp-tab-intro">
          No overrides yet. Edit a block and save a draft to start the medical-director approval
          workflow.
        </p>
      </div>
    );
  }

  return (
    <div data-testid="tab-approval" className="rp-tab-body">
      <p className="rp-tab-intro">
        Drafts require a medical director&apos;s sign-off before they take effect in production
        drafting.
      </p>

      <ul className="rp-approval-list">
        {sorted.map((ov) => {
          const meta = getBlockMeta(ov.blockKey);
          const isDraft = ov.status === 'Draft';
          return (
            <li key={ov.id} className="rp-approval-row">
              <div className="rp-approval-main">
                <div className="rp-approval-headings">
                  <span className="rp-approval-title">{meta.title}</span>
                  <code className="rp-block-key">{ov.blockKey}</code>
                </div>
                <div className="rp-approval-meta">
                  {ov.approvedAt
                    ? `Approved ${relativeTime(ov.approvedAt)}`
                    : `Updated ${relativeTime(ov.updatedAt)}`}
                </div>
              </div>
              <div className="rp-approval-actions">
                <span className={`badge ${ov.status === 'Approved' ? 'ok' : 'warn'}`}>{ov.status}</span>
                {isDraft && canApprove ? (
                  <button
                    type="button"
                    className="primary"
                    disabled={approvingId === ov.id}
                    onClick={() => onApprove(ov.id)}
                  >
                    {approvingId === ov.id ? 'Approving…' : 'Approve'}
                  </button>
                ) : isDraft ? (
                  <span className="rp-approval-note">Awaiting medical-director approval</span>
                ) : null}
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
