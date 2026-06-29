'use client';

import { useState, type ChangeEvent } from 'react';
import type { RulebookPromptBlock } from '@/lib/rulebookYaml';

type Props = {
  blocks: RulebookPromptBlock[];
  onChange: (next: RulebookPromptBlock[]) => void;
};

const DEFAULT_BLOCK_KEYS = ['system', 'findings_to_impression', 'cleanup', 'dictation_cleanup'];

export default function PromptBlocksPanel({ blocks, onChange }: Props) {
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [newKey, setNewKey] = useState('');

  function toggleCollapsed(key: string) {
    setCollapsed((prev) => ({ ...prev, [key]: !prev[key] }));
  }

  function updateBlock(idx: number, text: string) {
    onChange(blocks.map((b, i) => (i === idx ? { ...b, text } : b)));
  }

  function removeBlock(idx: number) {
    onChange(blocks.filter((_, i) => i !== idx));
  }

  function addBlock(key?: string) {
    const k = (key || newKey).trim().replace(/\s+/g, '_').toLowerCase();
    if (!k || blocks.some((b) => b.key === k)) return;
    onChange([...blocks, { key: k, text: '' }]);
    setNewKey('');
  }

  const usedKeys = new Set(blocks.map((b) => b.key));
  const availableDefaults = DEFAULT_BLOCK_KEYS.filter((k) => !usedKeys.has(k));

  return (
    <div className="rp-editor-block">
      <div className="rp-panel-title">Prompt Blocks</div>

      <div className="rp-stagger">
        {blocks.map((b, idx) => (
          <div
            key={b.key}
            className={`rp-editor-block${collapsed[b.key] ? ' collapsed' : ''}`}
            style={{ marginBottom: 8 }}
          >
            <div
              className="rp-row between"
              style={{ cursor: 'pointer' }}
              onClick={() => toggleCollapsed(b.key)}
            >
              <div className="rp-row rp-gap-sm">
                <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                  {collapsed[b.key] ? '▸' : '▾'}
                </span>
                <code style={{ fontSize: 12 }}>{b.key}</code>
              </div>
              <button
                className="ghost"
                style={{ padding: '2px 6px', fontSize: 11 }}
                onClick={(e) => {
                  e.stopPropagation();
                  removeBlock(idx);
                }}
                aria-label={`Remove block ${b.key}`}
              >×</button>
            </div>
            {!collapsed[b.key] && (
              <textarea
                className="rp-mt-sm"
                value={b.text}
                onChange={(e: ChangeEvent<HTMLTextAreaElement>) => updateBlock(idx, e.target.value)}
                placeholder={`Prompt text for ${b.key}…`}
                style={{
                  width: '100%',
                  minHeight: 96,
                  fontFamily: 'var(--mono)',
                  fontSize: 12,
                }}
              />
            )}
          </div>
        ))}
      </div>

      <div className="rp-row rp-row-wrap rp-gap-sm rp-mt-sm">
        {availableDefaults.map((k) => (
          <button key={k} className="subtle" onClick={() => addBlock(k)}>
            + {k}
          </button>
        ))}
      </div>

      <div className="rp-row rp-gap-sm rp-mt-sm">
        <input
          className="rp-input"
          value={newKey}
          onChange={(e: ChangeEvent<HTMLInputElement>) => setNewKey(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') addBlock(); }}
          placeholder="custom_block_key"
          style={{ flex: 1 }}
        />
        <button className="ghost" onClick={() => addBlock()}>+ Add Block</button>
      </div>
    </div>
  );
}
