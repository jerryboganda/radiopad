'use client';

import { useState, type ChangeEvent } from 'react';
import type { RulebookEditorState } from '@/lib/rulebookYaml';

const MODALITY_OPTIONS = ['CT', 'MR', 'US', 'XR', 'NM', 'PET', 'MG', 'FL'];
const BODY_PART_OPTIONS = ['Head', 'Neck', 'Chest', 'Abdomen', 'Pelvis', 'Spine', 'Extremity', 'Whole Body'];

type Props = {
  data: RulebookEditorState;
  onChange: (next: RulebookEditorState) => void;
};

export default function MetadataPanel({ data, onChange }: Props) {
  const [newModality, setNewModality] = useState('');
  const [newBodyPart, setNewBodyPart] = useState('');
  const [newReportType, setNewReportType] = useState('');

  function set<K extends keyof RulebookEditorState>(key: K, val: RulebookEditorState[K]) {
    onChange({ ...data, [key]: val });
  }

  function setAppliesTo(field: keyof RulebookEditorState['applies_to'], items: string[]) {
    onChange({ ...data, applies_to: { ...data.applies_to, [field]: items } });
  }

  function addToList(field: keyof RulebookEditorState['applies_to'], value: string, clear: () => void) {
    const v = value.trim();
    if (!v || data.applies_to[field].includes(v)) return;
    setAppliesTo(field, [...data.applies_to[field], v]);
    clear();
  }

  function removeFromList(field: keyof RulebookEditorState['applies_to'], value: string) {
    setAppliesTo(field, data.applies_to[field].filter((i) => i !== value));
  }

  return (
    <div className="rp-editor-block">
      <div className="rp-panel-title">Metadata</div>

      <div className="section-block">
        <label>Rulebook ID</label>
        <input
          className="rp-input"
          value={data.rulebook_id}
          onChange={(e: ChangeEvent<HTMLInputElement>) => set('rulebook_id', e.target.value)}
          placeholder="chest_ct_v1"
        />
      </div>

      <div className="section-block">
        <label>Name</label>
        <input
          className="rp-input"
          value={data.name}
          onChange={(e: ChangeEvent<HTMLInputElement>) => set('name', e.target.value)}
          placeholder="Chest CT"
        />
      </div>

      <div className="rp-row rp-gap-sm">
        <div className="section-block" style={{ flex: 1 }}>
          <label>Version</label>
          <input
            className="rp-input"
            value={data.version}
            onChange={(e: ChangeEvent<HTMLInputElement>) => set('version', e.target.value)}
            placeholder="1.0.0"
          />
        </div>
        <div className="section-block" style={{ flex: 1 }}>
          <label>Owner</label>
          <input
            className="rp-input"
            value={data.owner}
            onChange={(e: ChangeEvent<HTMLInputElement>) => set('owner', e.target.value)}
            placeholder="radiology-dept"
          />
        </div>
        <div className="section-block" style={{ flex: 1 }}>
          <label>Status</label>
          <select
            className="rp-input"
            value={data.status}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => set('status', e.target.value)}
          >
            <option value="draft">Draft</option>
            <option value="in_review">In Review</option>
            <option value="approved">Approved</option>
            <option value="deprecated">Deprecated</option>
          </select>
        </div>
      </div>

      <div className="section-block">
        <label>Modalities</label>
        <div className="rp-row rp-row-wrap rp-gap-sm">
          {data.applies_to.modalities.map((m) => (
            <span key={m} className="badge">
              {m}
              <button
                className="ghost"
                style={{ padding: '0 4px', fontSize: 11, lineHeight: 1 }}
                onClick={() => removeFromList('modalities', m)}
                aria-label={`Remove ${m}`}
              >×</button>
            </span>
          ))}
          <select
            className="rp-input"
            value={newModality}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => {
              const v = e.target.value;
              setNewModality('');
              if (v) addToList('modalities', v, () => {});
            }}
            style={{ width: 'auto', minWidth: 80 }}
          >
            <option value="">+ Add</option>
            {MODALITY_OPTIONS.filter((o) => !data.applies_to.modalities.includes(o)).map((o) => (
              <option key={o} value={o}>{o}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="section-block">
        <label>Body Parts</label>
        <div className="rp-row rp-row-wrap rp-gap-sm">
          {data.applies_to.body_parts.map((bp) => (
            <span key={bp} className="badge">
              {bp}
              <button
                className="ghost"
                style={{ padding: '0 4px', fontSize: 11, lineHeight: 1 }}
                onClick={() => removeFromList('body_parts', bp)}
                aria-label={`Remove ${bp}`}
              >×</button>
            </span>
          ))}
          <select
            className="rp-input"
            value={newBodyPart}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => {
              const v = e.target.value;
              setNewBodyPart('');
              if (v) addToList('body_parts', v, () => {});
            }}
            style={{ width: 'auto', minWidth: 80 }}
          >
            <option value="">+ Add</option>
            {BODY_PART_OPTIONS.filter((o) => !data.applies_to.body_parts.includes(o)).map((o) => (
              <option key={o} value={o}>{o}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="section-block">
        <label>Report Types</label>
        <div className="rp-row rp-row-wrap rp-gap-sm">
          {data.applies_to.report_types.map((rt) => (
            <span key={rt} className="badge">
              {rt}
              <button
                className="ghost"
                style={{ padding: '0 4px', fontSize: 11, lineHeight: 1 }}
                onClick={() => removeFromList('report_types', rt)}
                aria-label={`Remove ${rt}`}
              >×</button>
            </span>
          ))}
          <input
            className="rp-input"
            value={newReportType}
            onChange={(e: ChangeEvent<HTMLInputElement>) => setNewReportType(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                addToList('report_types', newReportType, () => setNewReportType(''));
              }
            }}
            placeholder="Type + Enter"
            style={{ width: 'auto', minWidth: 120 }}
          />
        </div>
      </div>
    </div>
  );
}
