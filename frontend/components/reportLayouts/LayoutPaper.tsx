'use client';

import type { CSSProperties } from 'react';
import type { LayoutBlock, ReportLayoutJson } from '@/lib/reportLayouts/schema';
import {
  ACCENT_HEX, BRANDING_FOOTER_TEXT, FONT_STACKS, PAGE_SIZE_PT,
  SECTION_LABEL_FALLBACK, STUDY_FIELD_LABEL_FALLBACK,
} from '@/lib/reportLayouts/accents';
import { SAMPLE_REPORT, resolveSampleStudyField } from '@/lib/reportLayouts/sampleReport';

export interface LayoutPaperProps {
  layout: ReportLayoutJson;
  /** 1 = true size (points as CSS px); pass e.g. 0.16 for a gallery-card thumbnail. */
  scale?: number;
  selectedBlockId?: string | null;
  onSelectBlock?: (id: string) => void;
  /** Enables hover/selection affordances — the designer canvas only. */
  interactive?: boolean;
}

/**
 * Report Templates (RPT-030) — the single pure-React renderer for a
 * `ReportLayoutJson` against the fictional `SAMPLE_REPORT`. Used by the designer
 * canvas, the gallery's mini card previews, and the preset picker, so all three
 * surfaces agree on what a layout looks like. This is a *preview*, not the export
 * itself — true fidelity comes from `POST /api/report-layouts/preview/{pdf,docx}`
 * (server-rendered via the same block model). The branding line below is always
 * rendered here too, exactly as the server renderers emit it unconditionally.
 */
export default function LayoutPaper({ layout, scale = 1, selectedBlockId, onSelectBlock, interactive }: LayoutPaperProps) {
  const size = PAGE_SIZE_PT[layout.page.size];
  const accentHex = ACCENT_HEX[layout.page.accent];
  const fontFamily = FONT_STACKS[layout.page.font];

  const wrapStyle: CSSProperties = {
    width: size.width * scale,
    height: size.height * scale,
    overflow: 'hidden',
    flex: '0 0 auto',
  };

  const paperStyle: CSSProperties = {
    width: size.width,
    height: size.height,
    transform: `scale(${scale})`,
    transformOrigin: 'top left',
    background: '#ffffff',
    color: '#111827',
    fontFamily,
    fontSize: layout.page.baseFontSizePt,
    lineHeight: 1.4,
    padding: layout.page.marginPt,
    boxSizing: 'border-box',
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
  };

  return (
    <div className="rp-rl-paper-wrap" style={wrapStyle}>
      <div className="rp-rl-paper" style={paperStyle}>
        {layout.blocks.map((block) => (
          <LayoutPaperBlock
            key={block.id}
            block={block}
            accentHex={accentHex}
            selected={Boolean(interactive) && selectedBlockId === block.id}
            onClick={interactive && onSelectBlock ? () => onSelectBlock(block.id) : undefined}
          />
        ))}
        <div style={{ flex: 1 }} />
        <div className="rp-rl-paper-footer">
          {(layout.footer.showStatusLine || layout.footer.customText) && (
            <div style={{ fontSize: 8, color: '#595959', textAlign: 'center', marginBottom: 2 }}>
              {[layout.footer.customText, layout.footer.showStatusLine
                ? `${SAMPLE_REPORT.status} — ${new Date(SAMPLE_REPORT.updatedAt).toLocaleString()}`
                : null].filter(Boolean).join('   •   ')}
            </div>
          )}
          <div style={{ fontSize: 7.5, color: '#808080', textAlign: 'center' }}>
            {BRANDING_FOOTER_TEXT}
          </div>
          {layout.page.showPageNumbers && (
            <div style={{ fontSize: 7.5, color: '#808080', textAlign: 'center', marginTop: 2 }}>Page 1 of 1</div>
          )}
        </div>
      </div>
    </div>
  );
}

function LayoutPaperBlock({
  block, accentHex, selected, onClick,
}: { block: LayoutBlock; accentHex: string; selected: boolean; onClick?: () => void }) {
  const cls = ['rp-rl-block', selected ? 'selected' : '', onClick ? 'interactive' : ''].filter(Boolean).join(' ');
  const props = onClick
    ? { className: cls, onClick, role: 'button' as const, tabIndex: 0 }
    : { className: cls };

  switch (block.type) {
    case 'letterhead': {
      const clinicName = block.clinicName || 'Your Clinic Name';
      const textCol = (
        <div style={{ textAlign: block.align }}>
          <div style={{ fontSize: 16, fontWeight: 600 }}>{clinicName}</div>
          {block.lines.map((line, i) => (
            <div key={i} style={{ fontSize: 9, color: '#595959' }}>{line}</div>
          ))}
        </div>
      );
      const logoImg = block.logo ? (
        <img src={block.logo.dataUrl} alt="" style={{ width: block.logo.widthPt, height: 'auto', display: 'block' }} />
      ) : null;
      return (
        <div {...props}>
          {block.logo && block.logoPosition === 'above' ? (
            <div style={{ textAlign: 'center' }}>
              {logoImg}
              {textCol}
            </div>
          ) : block.logo && block.logoPosition === 'right' ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
              {textCol}
              {logoImg}
            </div>
          ) : block.logo ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              {logoImg}
              {textCol}
            </div>
          ) : textCol}
          {block.showAccentRule && <div style={{ borderTop: `1.5px solid ${accentHex}`, marginTop: 6 }} />}
        </div>
      );
    }

    case 'studyInfo': {
      if (block.fields.length === 0) return null;
      const gridStyle: CSSProperties = {
        display: 'grid',
        gridTemplateColumns: `repeat(${block.columns}, 1fr)`,
        gap: '4px 16px',
        ...(block.showBox ? { border: '1px solid #d9d9d9', padding: 10, borderRadius: 4 } : {}),
      };
      return (
        <div {...props}>
          <div style={gridStyle}>
            {block.fields.map((f, i) => (
              <div key={i}>
                <div style={{ fontSize: 8, color: '#595959', textTransform: 'uppercase' }}>
                  {f.label || STUDY_FIELD_LABEL_FALLBACK[f.key]}
                </div>
                <div style={{ fontSize: 10 }}>{resolveSampleStudyField(f.key)}</div>
              </div>
            ))}
          </div>
        </div>
      );
    }

    case 'section': {
      const body = SAMPLE_REPORT.sections[block.section];
      if (!body && block.hideIfEmpty) return null;
      const label = block.label || SECTION_LABEL_FALLBACK[block.section];
      return (
        <div {...props}>
          {block.headingStyle === 'accent-bar' && (
            <div style={{ display: 'flex', alignItems: 'stretch', gap: 6 }}>
              <div style={{ width: 3, background: accentHex, borderRadius: 1 }} />
              <div style={{ fontWeight: 600, fontSize: 11 }}>{label}</div>
            </div>
          )}
          {block.headingStyle === 'underline' && (
            <div>
              <div style={{ fontWeight: 600, fontSize: 11 }}>{label}</div>
              <div style={{ borderTop: `1px solid ${accentHex}`, marginTop: 2 }} />
            </div>
          )}
          {block.headingStyle === 'plain' && <div style={{ fontWeight: 600, fontSize: 11 }}>{label}</div>}
          {block.headingStyle === 'uppercase' && (
            <div style={{ fontWeight: 600, fontSize: 10, textTransform: 'uppercase' }}>{label}</div>
          )}
          <div style={{ fontSize: 10.5, whiteSpace: 'pre-wrap', marginTop: 4 }}>{body || '—'}</div>
        </div>
      );
    }

    case 'signatures': {
      if (SAMPLE_REPORT.signatures.length === 0) return null;
      return (
        <div {...props}>
          <div style={{ fontWeight: 600, fontSize: 9, color: '#595959', marginBottom: 6 }}>SIGNATURES</div>
          {SAMPLE_REPORT.signatures.map((sig, i) => (
            <div key={i} style={{ marginTop: 8 }}>
              {block.showSignatureLine && <div style={{ width: 180, borderTop: '0.75px solid #595959', marginBottom: 4 }} />}
              <div style={{ fontWeight: 600, fontSize: 10 }}>{sig.role}</div>
              {block.showDate && <div style={{ fontSize: 8.5, color: '#595959' }}>Signed {new Date(sig.signedAt).toLocaleString()}</div>}
              {block.showNote && sig.note && <div style={{ fontSize: 9, fontStyle: 'italic' }}>{sig.note}</div>}
              {block.showHash && <div style={{ fontSize: 7.5, color: '#808080' }}>Verification: {sig.hash.slice(0, 12)}…</div>}
            </div>
          ))}
        </div>
      );
    }

    case 'text':
      return (
        <div {...props} style={{ textAlign: block.align, fontStyle: block.italic ? 'italic' : undefined, fontSize: 10.5 + block.fontSizeDelta, whiteSpace: 'pre-wrap' }}>
          {block.content}
        </div>
      );

    case 'divider':
      return (
        <div {...props}>
          {block.style === 'space' && <div style={{ height: block.spacePt }} />}
          {block.style === 'line' && <div style={{ borderTop: '0.75px solid #d9d9d9', margin: `${block.spacePt / 2}px 0` }} />}
          {block.style === 'accent' && <div style={{ borderTop: `1.5px solid ${accentHex}`, margin: `${block.spacePt / 2}px 0` }} />}
        </div>
      );
  }
}
