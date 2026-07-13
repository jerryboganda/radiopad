'use client';

// RC-01/02 review checklist — progress ring + item list derived entirely from
// real report state (section presence, unreviewed AI text, validation result,
// acknowledgement / signature). Nothing here is hand-maintained state.
import type { Report } from '@/lib/api';
import { CheckCircle2, AlertTriangle, Circle } from 'lucide-react';

export interface ChecklistItem {
  label: string;
  state: 'done' | 'pending' | 'todo';
}

export interface ChecklistPanelProps {
  report: Report;
  /** True while any section holds unreviewed AI text. */
  hasAiText: boolean;
  /** Validation has been run this session. */
  validated: boolean;
  blockers: number;
  primarySigned: boolean;
  canEdit: boolean;
  /** Existing acknowledge flow — marks all AI text reviewed. */
  onAcknowledge: () => void;
}

function statusName(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

export function buildChecklist(p: ChecklistPanelProps): ChecklistItem[] {
  const r = p.report;
  const has = (v: string | null | undefined) => Boolean((v ?? '').trim());
  const finalized = statusName(r.status) === 'Acknowledged' || statusName(r.status) === 'Exported' || p.primarySigned;
  return [
    { label: 'Clinical information present', state: has(r.indication) ? 'done' : 'todo' },
    { label: 'Technique described', state: has(r.technique) ? 'done' : 'todo' },
    { label: 'Findings present', state: has(r.findings) ? 'done' : 'todo' },
    { label: 'Impression present', state: has(r.impression) ? 'done' : 'todo' },
    {
      label: p.hasAiText ? 'Generated text requires review' : 'Generated text reviewed',
      state: p.hasAiText ? 'pending' : 'done',
    },
    {
      label: 'Comparison addressed',
      state: has(r.comparison) || has(r.study.comparison) ? 'done' : 'todo',
    },
    {
      label: p.validated && p.blockers > 0 ? 'Validation blockers open' : 'Validation passed',
      state: p.validated ? (p.blockers === 0 ? 'done' : 'pending') : 'todo',
    },
    { label: 'Report finalized', state: finalized ? 'done' : 'todo' },
  ];
}

export default function ChecklistPanel(p: ChecklistPanelProps) {
  const items = buildChecklist(p);
  const done = items.filter((i) => i.state === 'done').length;
  const pending = items.filter((i) => i.state === 'pending').length;
  const total = items.length;
  const pct = total === 0 ? 0 : Math.round((done / total) * 100);

  // Progress ring geometry (r=17 → circumference ≈ 106.8).
  const R = 17;
  const C = 2 * Math.PI * R;

  return (
    <div className="rp-checklist">
      <div className="rp-checklist-summary">
        <svg className="rp-checklist-ring" width="44" height="44" viewBox="0 0 44 44" aria-hidden>
          <circle className="rp-checklist-ring-track" cx="22" cy="22" r={R} fill="none" strokeWidth="4" />
          <circle
            className="rp-checklist-ring-fill"
            cx="22"
            cy="22"
            r={R}
            fill="none"
            strokeWidth="4"
            strokeLinecap="round"
            strokeDasharray={C}
            strokeDashoffset={C - (C * pct) / 100}
            transform="rotate(-90 22 22)"
          />
          <text className="rp-checklist-ring-text" x="22" y="26" textAnchor="middle">
            {done}/{total}
          </text>
        </svg>
        <div className="rp-checklist-summary-text">
          <span className="rp-checklist-pct">{pct}% complete</span>
          {pending > 0 && (
            <span className="rp-checklist-pending">
              {pending} item{pending === 1 ? '' : 's'} require review
            </span>
          )}
        </div>
      </div>

      <ul className="rp-checklist-items">
        {items.map((item) => (
          <li key={item.label} className={`rp-checklist-item is-${item.state}`}>
            <span className="rp-checklist-mark" aria-hidden>
              {item.state === 'done' ? (
                <CheckCircle2 size={15} />
              ) : item.state === 'pending' ? (
                <AlertTriangle size={15} />
              ) : (
                <Circle size={15} />
              )}
            </span>
            <span className="rp-checklist-label">{item.label}</span>
          </li>
        ))}
      </ul>

      {p.hasAiText && p.canEdit && (
        <button className="primary-ghost rp-checklist-ack" type="button" onClick={p.onAcknowledge}>
          Acknowledge AI text
        </button>
      )}
    </div>
  );
}
