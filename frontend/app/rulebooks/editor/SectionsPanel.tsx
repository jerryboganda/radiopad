'use client';

import { useState, useRef, type ChangeEvent, type DragEvent } from 'react';
import type { RulebookSection } from '@/lib/rulebookYaml';

type Props = {
  sections: RulebookSection[];
  onChange: (next: RulebookSection[]) => void;
};

export default function SectionsPanel({ sections, onChange }: Props) {
  const [newName, setNewName] = useState('');
  const dragIdx = useRef<number | null>(null);
  const [dragOverIdx, setDragOverIdx] = useState<number | null>(null);

  function toggleRequired(idx: number) {
    const next = sections.map((s, i) =>
      i === idx ? { ...s, required: !s.required } : s,
    );
    onChange(next);
  }

  function removeSection(idx: number) {
    onChange(sections.filter((_, i) => i !== idx));
  }

  function addSection() {
    const v = newName.trim();
    if (!v || sections.some((s) => s.name === v)) return;
    onChange([...sections, { name: v, required: false }]);
    setNewName('');
  }

  function onDragStart(e: DragEvent, idx: number) {
    dragIdx.current = idx;
    e.dataTransfer.effectAllowed = 'move';
    const target = e.currentTarget as HTMLElement;
    target.classList.add('rp-drag-active');
  }

  function onDragEnd(e: DragEvent) {
    const target = e.currentTarget as HTMLElement;
    target.classList.remove('rp-drag-active');
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
    const next = [...sections];
    const [moved] = next.splice(fromIdx, 1);
    next.splice(toIdx, 0, moved);
    onChange(next);
    dragIdx.current = null;
    setDragOverIdx(null);
  }

  return (
    <div className="rp-editor-block">
      <div className="rp-panel-title">Required Sections</div>

      <div>
        {sections.map((s, idx) => (
          <div
            key={s.name}
            draggable
            onDragStart={(e) => onDragStart(e, idx)}
            onDragEnd={onDragEnd}
            onDragOver={(e) => onDragOver(e, idx)}
            onDrop={(e) => onDrop(e, idx)}
            className={`rp-row rp-gap-sm${dragOverIdx === idx ? ' rp-drag-active' : ''}`}
            style={{
              padding: '8px 6px',
              borderBottom: '1px solid var(--border-soft)',
              cursor: 'grab',
            }}
          >
            <span className="rp-drag-handle" aria-hidden="true">⠿</span>
            <input
              type="checkbox"
              checked={s.required}
              onChange={() => toggleRequired(idx)}
              style={{ width: 16, height: 16 }}
            />
            <span style={{ flex: 1, fontSize: 13 }}>{s.name}</span>
            <button
              className="ghost"
              style={{ padding: '2px 6px', fontSize: 11 }}
              onClick={() => removeSection(idx)}
              aria-label={`Remove ${s.name}`}
            >×</button>
          </div>
        ))}
      </div>

      <div className="rp-row rp-gap-sm rp-mt-sm">
        <input
          className="rp-input"
          value={newName}
          onChange={(e: ChangeEvent<HTMLInputElement>) => setNewName(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') addSection(); }}
          placeholder="Custom section name"
          style={{ flex: 1 }}
        />
        <button className="ghost" onClick={addSection}>+ Add</button>
      </div>
    </div>
  );
}
