'use client';

import { useState } from 'react';
import BlockCard, { type BlockStatus } from './BlockCard';
import EmptyState from '@/components/ui/EmptyState';

export interface EditorBlock {
  key: string;
  body: string;
  dirty: boolean;
  status: BlockStatus;
}

export interface PromptBlockEditorProps {
  blocks: EditorBlock[];
  savingKey: string | null;
  onEdit: (key: string, value: string) => void;
  onSave: (key: string) => void;
  onReset: (key: string) => void;
  onAdd: (key: string) => void;
}

/** Default blocks every rulebook is expected to define — offered as one-click adds. */
const DEFAULT_BLOCK_KEYS = ['system', 'findings_to_impression', 'cleanup', 'dictation_cleanup'];

function normalizeKey(raw: string): string {
  return raw.trim().replace(/\s+/g, '_').toLowerCase();
}

export default function PromptBlockEditor({
  blocks,
  savingKey,
  onEdit,
  onSave,
  onReset,
  onAdd,
}: PromptBlockEditorProps) {
  const [adding, setAdding] = useState(false);
  const [newKey, setNewKey] = useState('');

  const usedKeys = new Set(blocks.map((b) => b.key));
  const availableDefaults = DEFAULT_BLOCK_KEYS.filter((k) => !usedKeys.has(k));

  function commitAdd(raw?: string) {
    const key = normalizeKey(raw ?? newKey);
    if (!key || usedKeys.has(key)) return;
    onAdd(key);
    setNewKey('');
    setAdding(false);
  }

  return (
    <section className="rp-panel rp-block-panel" aria-label="Prompt blocks">
      <div className="rp-panel-title rp-row between">
        <span>
          Prompt blocks
          {blocks.length > 0 ? <span className="rp-panel-count">{blocks.length}</span> : null}
        </span>
        <button
          type="button"
          className="primary-ghost"
          onClick={() => setAdding((v) => !v)}
          aria-expanded={adding}
        >
          + Add block
        </button>
      </div>

      {adding ? (
        <div className="rp-add-block">
          <input
            type="text"
            className="rp-add-block-input"
            value={newKey}
            autoFocus
            placeholder="custom_block_key"
            aria-label="New block key"
            onChange={(e) => setNewKey(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') commitAdd();
              if (e.key === 'Escape') {
                setNewKey('');
                setAdding(false);
              }
            }}
          />
          <button type="button" className="primary" onClick={() => commitAdd()} disabled={!normalizeKey(newKey)}>
            Add
          </button>
          {availableDefaults.length > 0 ? (
            <div className="rp-add-block-suggest">
              {availableDefaults.map((k) => (
                <button key={k} type="button" className="subtle" onClick={() => commitAdd(k)}>
                  + {k}
                </button>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}

      {blocks.length === 0 ? (
        <EmptyState
          title="No prompt blocks"
          description="This rulebook has no prompt blocks yet. Add one to start authoring AI instructions."
        />
      ) : (
        <div className="rp-block-list" data-testid="prompt-block-list">
          {blocks.map((block) => (
            <BlockCard
              key={block.key}
              blockKey={block.key}
              body={block.body}
              dirty={block.dirty}
              status={block.status}
              saving={savingKey === block.key}
              onEdit={(value) => onEdit(block.key, value)}
              onSave={() => onSave(block.key)}
              onReset={() => onReset(block.key)}
            />
          ))}
        </div>
      )}
    </section>
  );
}
