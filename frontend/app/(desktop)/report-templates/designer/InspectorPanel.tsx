'use client';

import { useState } from 'react';
import { Lock } from 'lucide-react';
import type {
  AccentColor, BlockAlign, DividerBlock, DividerStyle, HeadingStyle, LayoutBlock,
  LayoutFont, LayoutFooter, LetterheadBlock, LogoPosition, PageSetup, PageSize,
  SectionBlock, SignaturesBlock, StudyFieldKey, StudyInfoBlock, TextBlock,
} from '@/lib/reportLayouts/schema';
import { STUDY_FIELD_KEYS, MAX_LOGO_BYTES } from '@/lib/reportLayouts/schema';
import { ACCENT_LABEL, FONT_LABEL, BRANDING_FOOTER_TEXT, LEGAL_DISCLAIMER_TEXT, STUDY_FIELD_LABEL_FALLBACK } from '@/lib/reportLayouts/accents';

export interface InspectorPanelProps {
  page: PageSetup;
  footer: LayoutFooter;
  block: LayoutBlock | null;
  onPageChange: (page: Partial<PageSetup>) => void;
  onFooterChange: (footer: Partial<LayoutFooter>) => void;
  onBlockChange: (id: string, patch: Partial<LayoutBlock>) => void;
}

export default function InspectorPanel({ page, footer, block, onPageChange, onFooterChange, onBlockChange }: InspectorPanelProps) {
  return (
    <div className="rp-editor-block rp-rl-inspector">
      {block ? (
        <>
          <div className="rp-panel-title">{blockTitle(block)}</div>
          <BlockInspector block={block} onChange={(patch) => onBlockChange(block.id, patch as Partial<LayoutBlock>)} />
        </>
      ) : (
        <>
          <div className="rp-panel-title">Page setup</div>
          <PageSetupFields page={page} onChange={onPageChange} />
          <div className="rp-panel-title rp-mt-sm">Footer</div>
          <FooterFields footer={footer} onChange={onFooterChange} />
        </>
      )}
    </div>
  );
}

function blockTitle(block: LayoutBlock): string {
  switch (block.type) {
    case 'letterhead': return 'Letterhead';
    case 'studyInfo': return 'Patient / study info';
    case 'section': return `${block.section[0].toUpperCase()}${block.section.slice(1)} section`;
    case 'signatures': return 'Signatures';
    case 'text': return 'Text block';
    case 'divider': return 'Divider';
  }
}

// ------------------------------------------------------------------ page/footer

function PageSetupFields({ page, onChange }: { page: PageSetup; onChange: (page: Partial<PageSetup>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Page size</span>
        <select value={page.size} onChange={(e) => onChange({ size: e.target.value as PageSize })}>
          <option value="letter">Letter</option>
          <option value="a4">A4</option>
        </select>
      </label>
      <label className="rp-rl-field">
        <span>Font</span>
        <select value={page.font} onChange={(e) => onChange({ font: e.target.value as LayoutFont })}>
          <option value="sans">{FONT_LABEL.sans}</option>
          <option value="serif">{FONT_LABEL.serif}</option>
          <option value="mono">{FONT_LABEL.mono}</option>
        </select>
      </label>
      <label className="rp-rl-field">
        <span>Accent color</span>
        <select value={page.accent} onChange={(e) => onChange({ accent: e.target.value as AccentColor })}>
          {(Object.keys(ACCENT_LABEL) as AccentColor[]).map((a) => (
            <option key={a} value={a}>{ACCENT_LABEL[a]}</option>
          ))}
        </select>
      </label>
      <label className="rp-rl-field">
        <span>Margin (pt)</span>
        <input type="number" min={24} max={72} value={page.marginPt} onChange={(e) => onChange({ marginPt: clampNum(e.target.value, 24, 72, page.marginPt) })} />
      </label>
      <label className="rp-rl-field">
        <span>Base text size (pt)</span>
        <input type="number" min={8} max={14} value={page.baseFontSizePt} onChange={(e) => onChange({ baseFontSizePt: clampNum(e.target.value, 8, 14, page.baseFontSizePt) })} />
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={page.showPageNumbers} onChange={(e) => onChange({ showPageNumbers: e.target.checked })} />
        <span>Show page numbers</span>
      </label>
    </div>
  );
}

function FooterFields({ footer, onChange }: { footer: LayoutFooter; onChange: (footer: Partial<LayoutFooter>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={footer.showStatusLine} onChange={(e) => onChange({ showStatusLine: e.target.checked })} />
        <span>Show status + date line</span>
      </label>
      <label className="rp-rl-field">
        <span>Custom footer text (optional)</span>
        <input
          type="text"
          maxLength={200}
          value={footer.customText ?? ''}
          onChange={(e) => onChange({ customText: e.target.value || null })}
          placeholder="e.g. Confidential — for clinical use only"
        />
      </label>
      <div className="rp-rl-branding-note">
        <Lock size={12} strokeWidth={2} aria-hidden />
        <span>
          Every export always includes <strong>{LEGAL_DISCLAIMER_TEXT}</strong> and{' '}
          <strong>{BRANDING_FOOTER_TEXT}</strong> — neither can be removed.
        </span>
      </div>
    </div>
  );
}

function clampNum(raw: string, min: number, max: number, fallback: number): number {
  const n = Number(raw);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(min, Math.min(max, n));
}

// ------------------------------------------------------------------ blocks

function BlockInspector({ block, onChange }: { block: LayoutBlock; onChange: (patch: Record<string, unknown>) => void }) {
  switch (block.type) {
    case 'letterhead': return <LetterheadFields block={block} onChange={onChange} />;
    case 'studyInfo': return <StudyInfoFields block={block} onChange={onChange} />;
    case 'section': return <SectionFields block={block} onChange={onChange} />;
    case 'signatures': return <SignaturesFields block={block} onChange={onChange} />;
    case 'text': return <TextFields block={block} onChange={onChange} />;
    case 'divider': return <DividerFields block={block} onChange={onChange} />;
  }
}

function LetterheadFields({ block, onChange }: { block: LetterheadBlock; onChange: (patch: Record<string, unknown>) => void }) {
  const [logoError, setLogoError] = useState<string | null>(null);

  async function handleFile(file: File) {
    setLogoError(null);
    try {
      const dataUrl = await downscaleToPngDataUrl(file, 1200);
      const approxBytes = Math.ceil((dataUrl.length * 3) / 4);
      if (approxBytes > MAX_LOGO_BYTES) {
        setLogoError(`That image is still too large after resizing (${Math.round(approxBytes / 1024)} KB). Try a simpler image.`);
        return;
      }
      onChange({ logo: { dataUrl, widthPt: block.logo?.widthPt ?? 120 } });
    } catch {
      setLogoError("Couldn't read that image. Try a PNG or JPEG file.");
    }
  }

  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Clinic name (blank = your tenant name)</span>
        <input type="text" maxLength={200} value={block.clinicName ?? ''} onChange={(e) => onChange({ clinicName: e.target.value || null })} />
      </label>
      <label className="rp-rl-field">
        <span>Address / extra lines (one per line, up to 4)</span>
        <textarea
          rows={3}
          value={block.lines.join('\n')}
          onChange={(e) => onChange({ lines: e.target.value.split('\n').slice(0, 4).map((l) => l.slice(0, 120)) })}
        />
      </label>
      <label className="rp-rl-field">
        <span>Alignment</span>
        <select value={block.align} onChange={(e) => onChange({ align: e.target.value as BlockAlign })}>
          <option value="left">Left</option>
          <option value="center">Center</option>
          <option value="right">Right</option>
        </select>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showAccentRule} onChange={(e) => onChange({ showAccentRule: e.target.checked })} />
        <span>Show accent rule below</span>
      </label>

      <div className="rp-rl-field">
        <span>Logo</span>
        <input type="file" accept="image/png,image/jpeg" onChange={(e) => e.target.files?.[0] && handleFile(e.target.files[0])} />
        {logoError && <p className="rp-rl-field-error">{logoError}</p>}
        {block.logo && (
          <>
            <div className="rp-row rp-gap-sm rp-mt-sm">
              <img src={block.logo.dataUrl} alt="Logo preview" style={{ maxWidth: 80, maxHeight: 40 }} />
              <button type="button" className="ghost" onClick={() => onChange({ logo: null })}>Remove logo</button>
            </div>
            <label className="rp-rl-field">
              <span>Logo width (pt)</span>
              <input
                type="number" min={40} max={250}
                value={block.logo.widthPt}
                onChange={(e) => onChange({ logo: { ...block.logo!, widthPt: clampNum(e.target.value, 40, 250, block.logo!.widthPt) } })}
              />
            </label>
            <label className="rp-rl-field">
              <span>Logo position</span>
              <select value={block.logoPosition} onChange={(e) => onChange({ logoPosition: e.target.value as LogoPosition })}>
                <option value="left">Left of text</option>
                <option value="right">Right of text</option>
                <option value="above">Above text</option>
              </select>
            </label>
          </>
        )}
      </div>
    </div>
  );
}

function StudyInfoFields({ block, onChange }: { block: StudyInfoBlock; onChange: (patch: Record<string, unknown>) => void }) {
  const selectedKeys = new Set(block.fields.map((f) => f.key));

  function toggleField(key: StudyFieldKey) {
    if (selectedKeys.has(key)) {
      onChange({ fields: block.fields.filter((f) => f.key !== key) });
    } else if (block.fields.length < 12) {
      onChange({ fields: [...block.fields, { key, label: null }] });
    }
  }

  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Columns</span>
        <select value={block.columns} onChange={(e) => onChange({ columns: Number(e.target.value) })}>
          <option value={1}>1</option>
          <option value={2}>2</option>
          <option value={3}>3</option>
        </select>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showBox} onChange={(e) => onChange({ showBox: e.target.checked })} />
        <span>Show bordered box</span>
      </label>
      <div className="rp-rl-field">
        <span>Fields shown</span>
        <div className="rp-rl-checklist">
          {STUDY_FIELD_KEYS.map((key) => (
            <label key={key} className="rp-rl-field-row">
              <input type="checkbox" checked={selectedKeys.has(key)} onChange={() => toggleField(key)} />
              <span>{STUDY_FIELD_LABEL_FALLBACK[key]}</span>
            </label>
          ))}
        </div>
      </div>
    </div>
  );
}

function SectionFields({ block, onChange }: { block: SectionBlock; onChange: (patch: Record<string, unknown>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Heading label (blank = default)</span>
        <input type="text" maxLength={60} value={block.label ?? ''} onChange={(e) => onChange({ label: e.target.value || null })} />
      </label>
      <label className="rp-rl-field">
        <span>Heading style</span>
        <select value={block.headingStyle} onChange={(e) => onChange({ headingStyle: e.target.value as HeadingStyle })}>
          <option value="uppercase">Uppercase</option>
          <option value="accent-bar">Accent bar</option>
          <option value="underline">Underline</option>
          <option value="plain">Plain</option>
        </select>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.hideIfEmpty} onChange={(e) => onChange({ hideIfEmpty: e.target.checked })} />
        <span>Hide this section when empty</span>
      </label>
    </div>
  );
}

function SignaturesFields({ block, onChange }: { block: SignaturesBlock; onChange: (patch: Record<string, unknown>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showDate} onChange={(e) => onChange({ showDate: e.target.checked })} />
        <span>Show sign date</span>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showNote} onChange={(e) => onChange({ showNote: e.target.checked })} />
        <span>Show note</span>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showHash} onChange={(e) => onChange({ showHash: e.target.checked })} />
        <span>Show verification hash</span>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.showSignatureLine} onChange={(e) => onChange({ showSignatureLine: e.target.checked })} />
        <span>Show signature line</span>
      </label>
    </div>
  );
}

function TextFields({ block, onChange }: { block: TextBlock; onChange: (patch: Record<string, unknown>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Text</span>
        <textarea rows={4} maxLength={4000} value={block.content} onChange={(e) => onChange({ content: e.target.value })} />
      </label>
      <label className="rp-rl-field">
        <span>Alignment</span>
        <select value={block.align} onChange={(e) => onChange({ align: e.target.value as BlockAlign })}>
          <option value="left">Left</option>
          <option value="center">Center</option>
          <option value="right">Right</option>
        </select>
      </label>
      <label className="rp-rl-field rp-rl-field-row">
        <input type="checkbox" checked={block.italic} onChange={(e) => onChange({ italic: e.target.checked })} />
        <span>Italic</span>
      </label>
      <label className="rp-rl-field">
        <span>Size adjustment</span>
        <select value={block.fontSizeDelta} onChange={(e) => onChange({ fontSizeDelta: Number(e.target.value) })}>
          <option value={-2}>Smaller</option>
          <option value={-1}>Slightly smaller</option>
          <option value={0}>Normal</option>
          <option value={1}>Slightly larger</option>
          <option value={2}>Larger</option>
        </select>
      </label>
    </div>
  );
}

function DividerFields({ block, onChange }: { block: DividerBlock; onChange: (patch: Record<string, unknown>) => void }) {
  return (
    <div className="rp-rl-fields">
      <label className="rp-rl-field">
        <span>Style</span>
        <select value={block.style} onChange={(e) => onChange({ style: e.target.value as DividerStyle })}>
          <option value="line">Thin line</option>
          <option value="accent">Accent line</option>
          <option value="space">Blank space</option>
        </select>
      </label>
      <label className="rp-rl-field">
        <span>Spacing (pt)</span>
        <input type="number" min={4} max={48} value={block.spacePt} onChange={(e) => onChange({ spacePt: clampNum(e.target.value, 4, 48, block.spacePt) })} />
      </label>
    </div>
  );
}

// ------------------------------------------------------------------ logo upload

/** Downscales to at most `maxDim` px on the longest side and re-encodes as PNG —
 * both a size safeguard and a validity guarantee (canvas PNG output always
 * satisfies the server's PNG magic-byte check regardless of the source format). */
function downscaleToPngDataUrl(file: File, maxDim: number): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('read failed'));
    reader.onload = () => {
      const img = new Image();
      img.onerror = () => reject(new Error('decode failed'));
      img.onload = () => {
        const scale = Math.min(1, maxDim / Math.max(img.width, img.height));
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(img.width * scale));
        canvas.height = Math.max(1, Math.round(img.height * scale));
        const ctx = canvas.getContext('2d');
        if (!ctx) { reject(new Error('canvas unsupported')); return; }
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL('image/png'));
      };
      img.src = reader.result as string;
    };
    reader.readAsDataURL(file);
  });
}
