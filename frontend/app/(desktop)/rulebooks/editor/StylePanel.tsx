'use client';

import { useState, type ChangeEvent, type KeyboardEvent } from 'react';
import type { RulebookStyle } from '@/lib/rulebookYaml';

type Props = {
  style: RulebookStyle;
  onChange: (next: RulebookStyle) => void;
};

const TONE_OPTIONS = [
  { value: 'concise_clinical', label: 'Concise Clinical' },
  { value: 'verbose_clinical', label: 'Verbose Clinical' },
  { value: 'educational', label: 'Educational' },
];

export default function StylePanel({ style, onChange }: Props) {
  const [newAvoid, setNewAvoid] = useState('');
  const [newFollowup, setNewFollowup] = useState('');

  function update(patch: Partial<RulebookStyle>) {
    onChange({ ...style, ...patch });
  }

  function addAvoidTerm() {
    const v = newAvoid.trim();
    if (!v || style.avoid_terms.includes(v)) return;
    update({ avoid_terms: [...style.avoid_terms, v] });
    setNewAvoid('');
  }

  function removeAvoidTerm(term: string) {
    update({ avoid_terms: style.avoid_terms.filter((t) => t !== term) });
  }

  function addFollowup() {
    const v = newFollowup.trim();
    if (!v || style.approved_followups.includes(v)) return;
    update({ approved_followups: [...style.approved_followups, v] });
    setNewFollowup('');
  }

  function removeFollowup(term: string) {
    update({ approved_followups: style.approved_followups.filter((t) => t !== term) });
  }

  return (
    <div className="rp-editor-block">
      <div className="rp-panel-title">Style</div>

      <div className="rp-row rp-gap-sm">
        <div className="section-block" style={{ flex: 2 }}>
          <label>Tone</label>
          <select
            className="rp-input"
            value={style.tone}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => update({ tone: e.target.value })}
          >
            {TONE_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </div>
        <div className="section-block" style={{ flex: 1 }}>
          <label>Max Impression Bullets</label>
          <input
            className="rp-input"
            type="number"
            min={1}
            max={20}
            value={style.impression_max_bullets}
            onChange={(e: ChangeEvent<HTMLInputElement>) =>
              update({ impression_max_bullets: parseInt(e.target.value, 10) || 1 })
            }
          />
        </div>
      </div>

      <div className="section-block">
        <label>Avoid Terms</label>
        <div className="rp-row rp-row-wrap rp-gap-sm">
          {style.avoid_terms.map((t) => (
            <span key={t} className="badge danger">
              {t}
              <button
                className="ghost"
                style={{ padding: '0 4px', fontSize: 11, lineHeight: 1 }}
                onClick={() => removeAvoidTerm(t)}
                aria-label={`Remove ${t}`}
              >×</button>
            </span>
          ))}
          <input
            className="rp-input"
            value={newAvoid}
            onChange={(e: ChangeEvent<HTMLInputElement>) => setNewAvoid(e.target.value)}
            onKeyDown={(e: KeyboardEvent) => { if (e.key === 'Enter') addAvoidTerm(); }}
            placeholder="Term + Enter"
            style={{ width: 'auto', minWidth: 120 }}
          />
        </div>
      </div>

      <div className="section-block">
        <label>Approved Follow-ups</label>
        <div className="rp-row rp-row-wrap rp-gap-sm">
          {style.approved_followups.map((f) => (
            <span key={f} className="badge info">
              {f}
              <button
                className="ghost"
                style={{ padding: '0 4px', fontSize: 11, lineHeight: 1 }}
                onClick={() => removeFollowup(f)}
                aria-label={`Remove ${f}`}
              >×</button>
            </span>
          ))}
          <input
            className="rp-input"
            value={newFollowup}
            onChange={(e: ChangeEvent<HTMLInputElement>) => setNewFollowup(e.target.value)}
            onKeyDown={(e: KeyboardEvent) => { if (e.key === 'Enter') addFollowup(); }}
            placeholder="Follow-up + Enter"
            style={{ width: 'auto', minWidth: 140 }}
          />
        </div>
      </div>
    </div>
  );
}
