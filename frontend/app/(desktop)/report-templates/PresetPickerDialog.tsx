'use client';

import { X } from 'lucide-react';
import { REPORT_LAYOUT_PRESETS } from '@/lib/reportLayouts/presets';
import LayoutPaper from '@/components/reportLayouts/LayoutPaper';

export interface PresetPickerDialogProps {
  onClose: () => void;
  onChoose: (presetKey: string) => void;
  busy: boolean;
}

export default function PresetPickerDialog({ onClose, onChoose, busy }: PresetPickerDialogProps) {
  return (
    <div className="rp-rl-dialog-backdrop" role="presentation" onClick={onClose}>
      <div
        className="rp-panel rp-rl-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="New from preset"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="rp-row between rp-rl-dialog-head">
          <div className="rp-panel-title">New from preset</div>
          <button type="button" className="icon-btn ghost" aria-label="Close" onClick={onClose}>
            <X size={16} strokeWidth={1.8} aria-hidden />
          </button>
        </div>
        <p className="rp-page-sub">Start from a built-in design, then customize every detail in the designer.</p>

        <div className="rp-rl-preset-grid">
          {REPORT_LAYOUT_PRESETS.map((preset) => (
            <div key={preset.key} className="rp-rl-preset-card">
              <div className="rp-rl-preset-preview">
                <LayoutPaper layout={preset.layout} scale={0.24} />
              </div>
              <div className="rp-rl-preset-name">{preset.name}</div>
              <div className="rp-rl-preset-desc">{preset.description}</div>
              <button
                type="button"
                className="primary"
                disabled={busy}
                onClick={() => onChoose(preset.key)}
              >
                Use this preset
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
