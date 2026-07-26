'use client';

import { Download, Redo2, Undo2, ZoomIn, ZoomOut } from 'lucide-react';
import { useDesignerZoom } from '@/lib/reportLayouts/prefs';
import Banner from '@/components/ui/Banner';

export interface DesignerToolbarProps {
  name: string;
  onNameChange: (name: string) => void;
  description: string;
  onDescriptionChange: (description: string) => void;
  onSave: () => void;
  saving: boolean;
  dirty: boolean;
  canUndo: boolean;
  canRedo: boolean;
  onUndo: () => void;
  onRedo: () => void;
  downloading: 'pdf' | 'docx' | null;
  onDownloadPdf: () => void;
  onDownloadDocx: () => void;
  warning?: string | null;
}

export default function DesignerToolbar({
  name, onNameChange, description, onDescriptionChange,
  onSave, saving, dirty, canUndo, canRedo, onUndo, onRedo,
  downloading, onDownloadPdf, onDownloadDocx, warning,
}: DesignerToolbarProps) {
  const [zoom, setZoom] = useDesignerZoom();

  return (
    <div className="rp-toolbar sticky rp-rl-toolbar">
      {warning && (
        <div className="rp-rl-toolbar-warning">
          <Banner tone="warn">{warning}</Banner>
        </div>
      )}
      <div className="rp-row rp-gap-sm rp-rl-toolbar-row">
        <div className="rp-rl-toolbar-name">
          <input
            type="text"
            aria-label="Design name"
            value={name}
            onChange={(e) => onNameChange(e.target.value)}
            placeholder="Untitled design"
            className="rp-rl-name-input"
          />
          <input
            type="text"
            aria-label="Description"
            value={description}
            onChange={(e) => onDescriptionChange(e.target.value)}
            placeholder="Add a short description (optional)"
            className="rp-rl-desc-input"
          />
        </div>

        <div className="rp-row rp-gap-sm">
          <button type="button" className="icon-btn ghost" aria-label="Undo" disabled={!canUndo} onClick={onUndo}>
            <Undo2 size={15} strokeWidth={1.8} aria-hidden />
          </button>
          <button type="button" className="icon-btn ghost" aria-label="Redo" disabled={!canRedo} onClick={onRedo}>
            <Redo2 size={15} strokeWidth={1.8} aria-hidden />
          </button>

          <div className="rp-row rp-rl-zoom" aria-label="Canvas zoom">
            <button type="button" className="icon-btn ghost" aria-label="Zoom out" onClick={() => setZoom(zoom - 10)}>
              <ZoomOut size={14} strokeWidth={1.8} aria-hidden />
            </button>
            <span className="rp-rl-zoom-value">{zoom}%</span>
            <button type="button" className="icon-btn ghost" aria-label="Zoom in" onClick={() => setZoom(zoom + 10)}>
              <ZoomIn size={14} strokeWidth={1.8} aria-hidden />
            </button>
          </div>

          <button type="button" className="ghost" disabled={downloading === 'pdf'} onClick={onDownloadPdf}>
            <Download size={13} strokeWidth={1.8} aria-hidden /> Sample PDF
          </button>
          <button type="button" className="ghost" disabled={downloading === 'docx'} onClick={onDownloadDocx}>
            <Download size={13} strokeWidth={1.8} aria-hidden /> Sample DOCX
          </button>

          <button type="button" className="primary" disabled={saving || !dirty} onClick={onSave} aria-busy={saving}>
            {saving ? 'Saving…' : dirty ? 'Save' : 'Saved'}
          </button>
        </div>
      </div>
    </div>
  );
}
