'use client';

import { MoreHorizontal, Star, Users } from 'lucide-react';
import { useState } from 'react';
import type { ReportLayout } from '@/lib/api';
import { validateLayout } from '@/lib/reportLayouts/schema';
import LayoutPaper from '@/components/reportLayouts/LayoutPaper';
import StatusBadge from '@/components/ui/StatusBadge';

export interface LayoutCardProps {
  item: ReportLayout;
  isRecommended: boolean;
  isMyDefault: boolean;
  canRecommend: boolean;
  onEdit: () => void;
  onDuplicate: () => void;
  onSetDefault: () => void;
  onDelete: () => void;
  onRecommend: () => void;
  onClearRecommend: () => void;
}

export default function LayoutCard({
  item, isRecommended, isMyDefault, canRecommend,
  onEdit, onDuplicate, onSetDefault, onDelete, onRecommend, onClearRecommend,
}: LayoutCardProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const parsed = validateLayout(JSON.parse(item.layoutJson || '{}'));

  return (
    <div className="rp-panel rp-rl-card">
      <button type="button" className="rp-rl-card-preview" onClick={onEdit} aria-label={`Edit ${item.name}`}>
        {parsed.ok ? (
          <LayoutPaper layout={parsed.value} scale={0.22} />
        ) : (
          <div className="rp-rl-card-preview-broken">Preview unavailable</div>
        )}
      </button>

      <div className="rp-rl-card-body">
        <div className="rp-row rp-rl-card-badges">
          {isRecommended && <StatusBadge tone="ai"><Users size={11} strokeWidth={2} aria-hidden /> Recommended</StatusBadge>}
          {isMyDefault && <StatusBadge tone="success"><Star size={11} strokeWidth={2} aria-hidden /> My default</StatusBadge>}
        </div>
        <div className="rp-rl-card-name">{item.name}</div>
        <div className="rp-rl-card-meta">By {item.createdByName || 'Unknown'} · {new Date(item.updatedAt).toLocaleDateString()}</div>
        {item.description && <div className="rp-rl-card-desc">{item.description}</div>}

        <div className="rp-row rp-gap-sm rp-rl-card-actions">
          <button type="button" className="primary-ghost" onClick={onEdit}>Edit</button>
          <button type="button" className="ghost" onClick={onDuplicate}>Duplicate</button>
          <div className="rp-rl-card-menu">
            <button type="button" className="icon-btn ghost" aria-label="More actions" onClick={() => setMenuOpen((v) => !v)}>
              <MoreHorizontal size={15} strokeWidth={1.8} aria-hidden />
            </button>
            {menuOpen && (
              <div className="rp-rl-card-menu-list" role="menu">
                <button type="button" role="menuitem" onClick={() => { setMenuOpen(false); onSetDefault(); }} disabled={isMyDefault}>
                  {isMyDefault ? 'This is my default' : 'Set as my default'}
                </button>
                {canRecommend && (
                  isRecommended ? (
                    <button type="button" role="menuitem" onClick={() => { setMenuOpen(false); onClearRecommend(); }}>
                      Unrecommend
                    </button>
                  ) : (
                    <button type="button" role="menuitem" onClick={() => { setMenuOpen(false); onRecommend(); }}>
                      Recommend to team
                    </button>
                  )
                )}
                <button type="button" role="menuitem" className="rp-rl-card-menu-danger" onClick={() => { setMenuOpen(false); onDelete(); }}>
                  Delete
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
