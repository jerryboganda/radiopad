'use client';

import { getBlockMeta } from './blockMeta';

export type BlockStatus = 'Default' | 'Draft' | 'Approved';

export interface BlockCardProps {
  blockKey: string;
  /** Current editor value (may differ from the saved base when dirty). */
  body: string;
  /** True when the value differs from the saved override / rulebook default. */
  dirty: boolean;
  /** Default = no override yet; Draft / Approved = saved override status. */
  status: BlockStatus;
  saving: boolean;
  onEdit: (value: string) => void;
  onSave: () => void;
  onReset: () => void;
}

const STATUS_BADGE: Record<BlockStatus, string> = {
  Default: 'badge',
  Draft: 'badge warn',
  Approved: 'badge ok',
};

const SAVED_HINT: Record<BlockStatus, string> = {
  Default: 'Rulebook default',
  Draft: 'Saved draft',
  Approved: 'Approved override',
};

export default function BlockCard({
  blockKey,
  body,
  dirty,
  status,
  saving,
  onEdit,
  onSave,
  onReset,
}: BlockCardProps) {
  const meta = getBlockMeta(blockKey);
  const fieldId = `prompt-block-${blockKey}`;

  return (
    <div className="rp-block-card" data-testid={`prompt-block-${blockKey}`}>
      <div className="rp-block-head">
        <div className="rp-block-headings">
          <label className="rp-block-title" htmlFor={fieldId}>
            {meta.title}
          </label>
          <code className="rp-block-key">{blockKey}</code>
        </div>
        <span className={STATUS_BADGE[status]}>{status}</span>
      </div>

      <p className="rp-block-desc">{meta.description}</p>

      <textarea
        id={fieldId}
        className="rp-prompt-textarea"
        value={body}
        onChange={(e) => onEdit(e.target.value)}
        rows={6}
        spellCheck={false}
        placeholder={`Prompt text for ${meta.title}…`}
      />

      <div className="rp-block-footer">
        <span className="rp-block-count">{body.length.toLocaleString()} chars</span>
        {dirty ? (
          <div className="rp-block-actions rp-anim-slide-left">
            <button type="button" className="subtle" onClick={onReset} disabled={saving}>
              Reset
            </button>
            <button type="button" className="primary" onClick={onSave} disabled={saving} aria-busy={saving}>
              {saving && <span className="rp-spinner sm" aria-hidden />}
              {saving ? 'Saving…' : 'Save draft'}
            </button>
          </div>
        ) : (
          <span className="rp-block-saved">{SAVED_HINT[status]}</span>
        )}
      </div>
    </div>
  );
}
