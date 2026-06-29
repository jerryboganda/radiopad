'use client';

import { useState, useRef, type ChangeEvent, type DragEvent } from 'react';
import type { RulebookRule } from '@/lib/rulebookYaml';

type Props = {
  rules: RulebookRule[];
  onChange: (next: RulebookRule[]) => void;
};

/** Predefined catalog of rule IDs matching ReportValidator's known IDs. */
const RULE_CATALOG = [
  'required_sections',
  'impression_bullet_count',
  'avoid_terms',
  'approved_followups_only',
  'findings_not_empty',
  'impression_not_empty',
  'no_duplicate_sections',
  'laterality_consistency',
  'measurement_units',
  'comparison_required',
  'clinical_indication_present',
  'recommendation_present',
  'spelling_check',
  'grammar_check',
  'template_compliance',
];

export default function RulesPanel({ rules, onChange }: Props) {
  const [showPicker, setShowPicker] = useState(false);
  const dragIdx = useRef<number | null>(null);
  const [dragOverIdx, setDragOverIdx] = useState<number | null>(null);

  function addRule(id: string) {
    if (rules.some((r) => r.id === id)) return;
    onChange([...rules, { id, severity: 'warning', description: '' }]);
    setShowPicker(false);
  }

  function removeRule(idx: number) {
    onChange(rules.filter((_, i) => i !== idx));
  }

  function updateRule(idx: number, patch: Partial<RulebookRule>) {
    onChange(rules.map((r, i) => (i === idx ? { ...r, ...patch } : r)));
  }

  // Drag-and-drop handlers
  function onDragStart(e: DragEvent, idx: number) {
    dragIdx.current = idx;
    e.dataTransfer.effectAllowed = 'move';
    (e.currentTarget as HTMLElement).classList.add('rp-drag-active');
  }

  function onDragEnd(e: DragEvent) {
    (e.currentTarget as HTMLElement).classList.remove('rp-drag-active');
    setDragOverIdx(null);
  }

  function onDragOver(e: DragEvent, idx: number) {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    setDragOverIdx(idx);
  }

  function onDrop(e: DragEvent, toIdx: number) {
    e.preventDefault();
    const fromIdx = dragIdx.current;
    if (fromIdx === null || fromIdx === toIdx) {
      setDragOverIdx(null);
      return;
    }
    const next = [...rules];
    const [moved] = next.splice(fromIdx, 1);
    next.splice(toIdx, 0, moved);
    onChange(next);
    dragIdx.current = null;
    setDragOverIdx(null);
  }

  const usedIds = new Set(rules.map((r) => r.id));
  const availableRules = RULE_CATALOG.filter((id) => !usedIds.has(id));

  return (
    <div className="rp-editor-block">
      <div className="rp-panel-title">Validation Rules</div>

      <div className="rp-stagger">
        {rules.map((r, idx) => (
          <div
            key={r.id}
            draggable
            onDragStart={(e) => onDragStart(e, idx)}
            onDragEnd={onDragEnd}
            onDragOver={(e) => onDragOver(e, idx)}
            onDrop={(e) => onDrop(e, idx)}
            className={`rp-editor-block${dragOverIdx === idx ? ' rp-drag-active rp-drop-zone' : ''}`}
            style={{ marginBottom: 8, cursor: 'grab' }}
          >
            <div className="rp-row between">
              <div className="rp-row rp-gap-sm">
                <span className="rp-drag-handle" aria-hidden="true">⠿</span>
                <code style={{ fontSize: 12 }}>{r.id}</code>
              </div>
              <div className="rp-row rp-gap-sm">
                <select
                  className="rp-input"
                  value={r.severity}
                  onChange={(e: ChangeEvent<HTMLSelectElement>) =>
                    updateRule(idx, { severity: e.target.value as RulebookRule['severity'] })
                  }
                  style={{ width: 'auto', minWidth: 90 }}
                >
                  <option value="blocker">Blocker</option>
                  <option value="warning">Warning</option>
                  <option value="info">Info</option>
                </select>
                <button
                  className="ghost"
                  style={{ padding: '2px 6px', fontSize: 11 }}
                  onClick={() => removeRule(idx)}
                  aria-label={`Remove rule ${r.id}`}
                >×</button>
              </div>
            </div>
            <input
              className="rp-input rp-mt-sm"
              value={r.description}
              onChange={(e: ChangeEvent<HTMLInputElement>) =>
                updateRule(idx, { description: e.target.value })
              }
              placeholder="Description"
              style={{ width: '100%' }}
            />
          </div>
        ))}
      </div>

      {showPicker ? (
        <div className="rp-editor-block rp-mt-sm">
          <div className="rp-panel-title" style={{ fontSize: 12 }}>Select a rule</div>
          <div className="rp-row rp-row-wrap rp-gap-sm">
            {availableRules.length === 0 && (
              <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>All rules added.</span>
            )}
            {availableRules.map((id) => (
              <button key={id} className="subtle" onClick={() => addRule(id)}>
                <code style={{ fontSize: 11 }}>{id}</code>
              </button>
            ))}
          </div>
          <button className="ghost rp-mt-sm" onClick={() => setShowPicker(false)}>Cancel</button>
        </div>
      ) : (
        <button className="ghost rp-mt-sm" onClick={() => setShowPicker(true)}>+ Add Rule</button>
      )}
    </div>
  );
}
