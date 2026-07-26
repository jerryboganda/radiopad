'use client';

import { useRef, useState, type DragEvent } from 'react';
import { AlignLeft, FileSignature, Heading, Minus, Plus, Rows3, Type } from 'lucide-react';
import type { LayoutBlock } from '@/lib/reportLayouts/schema';
import { newBlockOfType, SECTION_KEYS, DEFAULT_SECTION_LABEL } from '@/lib/reportLayouts/schema';

export interface BlockPalettePanelProps {
  blocks: LayoutBlock[];
  selectedBlockId: string | null;
  onSelect: (id: string) => void;
  onAdd: (block: LayoutBlock) => void;
  onRemove: (id: string) => void;
  onMove: (id: string, toIndex: number) => void;
}

const BLOCK_ICON: Record<LayoutBlock['type'], typeof Heading> = {
  letterhead: Heading,
  studyInfo: Rows3,
  section: AlignLeft,
  signatures: FileSignature,
  text: Type,
  divider: Minus,
};

function blockLabel(block: LayoutBlock): string {
  switch (block.type) {
    case 'letterhead': return 'Letterhead';
    case 'studyInfo': return 'Patient / study info';
    case 'section': return block.label || DEFAULT_SECTION_LABEL[block.section];
    case 'signatures': return 'Signatures';
    case 'text': return block.content ? `Text: ${block.content.slice(0, 24)}` : 'Text block';
    case 'divider': return 'Divider';
  }
}

export default function BlockPalettePanel({ blocks, selectedBlockId, onSelect, onAdd, onRemove, onMove }: BlockPalettePanelProps) {
  const dragIdx = useRef<number | null>(null);
  const [dragOverIdx, setDragOverIdx] = useState<number | null>(null);

  const hasLetterhead = blocks.some((b) => b.type === 'letterhead');
  const hasStudyInfo = blocks.some((b) => b.type === 'studyInfo');
  const hasSignatures = blocks.some((b) => b.type === 'signatures');
  const usedSections = new Set(blocks.filter((b) => b.type === 'section').map((b) => (b as { section: string }).section));

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
    setDragOverIdx(null);
    dragIdx.current = null;
    if (fromIdx === null || fromIdx === toIdx) return;
    onMove(blocks[fromIdx].id, toIdx);
  }

  return (
    <div className="rp-editor-block rp-rl-palette">
      <div className="rp-panel-title">Blocks</div>

      <div className="rp-rl-palette-list">
        {blocks.map((block, idx) => {
          const Icon = BLOCK_ICON[block.type];
          const active = dragOverIdx === idx;
          return (
            <div
              key={block.id}
              draggable
              onDragStart={(e) => onDragStart(e, idx)}
              onDragEnd={onDragEnd}
              onDragOver={(e) => onDragOver(e, idx)}
              onDrop={(e) => onDrop(e, idx)}
              className={`rp-row rp-gap-sm rp-rl-palette-row${active ? ' rp-drag-active rp-drop-zone' : ''}${selectedBlockId === block.id ? ' selected' : ''}`}
              onClick={() => onSelect(block.id)}
            >
              <span className="rp-drag-handle" aria-hidden="true">⠿</span>
              <Icon size={14} strokeWidth={1.8} aria-hidden />
              <span className="rp-rl-palette-row-label">{blockLabel(block)}</span>
              <button
                type="button"
                className="ghost icon-btn"
                aria-label={`Remove ${blockLabel(block)}`}
                onClick={(e) => { e.stopPropagation(); onRemove(block.id); }}
              >×</button>
            </div>
          );
        })}
        {blocks.length === 0 && <p className="rp-page-sub">Add a block below to start designing.</p>}
      </div>

      <div className="rp-panel-title rp-mt-sm">Add a block</div>
      <div className="rp-rl-palette-add">
        <button type="button" className="ghost" disabled={hasLetterhead} onClick={() => onAdd(newBlockOfType('letterhead'))}>
          <Plus size={13} strokeWidth={1.8} aria-hidden /> Letterhead
        </button>
        <button type="button" className="ghost" disabled={hasStudyInfo} onClick={() => onAdd(newBlockOfType('studyInfo'))}>
          <Plus size={13} strokeWidth={1.8} aria-hidden /> Patient / study info
        </button>
        {SECTION_KEYS.filter((s) => !usedSections.has(s)).map((section) => (
          <button
            key={section}
            type="button"
            className="ghost"
            onClick={() => onAdd({ ...newBlockOfType('section'), section } as LayoutBlock)}
          >
            <Plus size={13} strokeWidth={1.8} aria-hidden /> {DEFAULT_SECTION_LABEL[section]} section
          </button>
        ))}
        <button type="button" className="ghost" disabled={hasSignatures} onClick={() => onAdd(newBlockOfType('signatures'))}>
          <Plus size={13} strokeWidth={1.8} aria-hidden /> Signatures
        </button>
        <button type="button" className="ghost" onClick={() => onAdd(newBlockOfType('text'))}>
          <Plus size={13} strokeWidth={1.8} aria-hidden /> Text
        </button>
        <button type="button" className="ghost" onClick={() => onAdd(newBlockOfType('divider'))}>
          <Plus size={13} strokeWidth={1.8} aria-hidden /> Divider
        </button>
      </div>
    </div>
  );
}
