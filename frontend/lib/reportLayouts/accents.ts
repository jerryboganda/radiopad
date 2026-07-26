/**
 * Report Templates (RPT-030) — presentation constants mirroring
 * `backend/RadioPad.Api/src/RadioPad.Api/Services/ReportLayouts/ReportLayoutBranding.cs`.
 *
 * These are applied as inline `style=` on the paper canvas (`LayoutPaper`), not as
 * CSS custom properties — the exported document is a fixed, theme-independent
 * print surface (like `.document-preview`), so it deliberately sits outside the
 * app's `--color-*` token contract rather than reusing it.
 */
import type { AccentColor, LayoutFont, SectionKey, StudyFieldKey } from './schema';

export const BRAND_LINE = 'Powered by RadioPad';
export const BRAND_WEBSITE = 'radiopadstudio.com';
export const BRANDING_FOOTER_TEXT = `${BRAND_LINE} — ${BRAND_WEBSITE}`;

/** Mandatory legal disclaimer — same non-removable contract as {@link BRANDING_FOOTER_TEXT}. */
export const LEGAL_DISCLAIMER_TEXT = 'NOT VALID IN COURT OF LAW';

export const ACCENT_HEX: Record<AccentColor, string> = {
  graphite: '#374151',
  navy: '#1e3a8a',
  teal: '#0f766e',
  burgundy: '#7f1d1d',
  forest: '#14532d',
  slate: '#475569',
};

export const ACCENT_LABEL: Record<AccentColor, string> = {
  graphite: 'Graphite',
  navy: 'Navy',
  teal: 'Teal',
  burgundy: 'Burgundy',
  forest: 'Forest',
  slate: 'Slate',
};

export const FONT_STACKS: Record<LayoutFont, string> = {
  sans: 'Helvetica, Arial, sans-serif',
  serif: "'Times New Roman', Georgia, serif",
  mono: "'Courier New', monospace",
};

export const FONT_LABEL: Record<LayoutFont, string> = {
  sans: 'Sans-serif',
  serif: 'Serif',
  mono: 'Monospace',
};

export const PAGE_SIZE_PT: Record<'letter' | 'a4', { width: number; height: number }> = {
  letter: { width: 612, height: 792 },
  a4: { width: 595, height: 842 },
};

export const SECTION_LABEL_FALLBACK: Record<SectionKey, string> = {
  indication: 'Indication',
  technique: 'Technique',
  comparison: 'Comparison',
  findings: 'Findings',
  impression: 'Impression',
  recommendations: 'Recommendations',
};

export const STUDY_FIELD_LABEL_FALLBACK: Record<StudyFieldKey, string> = {
  patientReference: 'Patient',
  accessionNumber: 'Accession',
  modality: 'Modality',
  bodyPart: 'Body part',
  contrast: 'Contrast',
  age: 'Age',
  gender: 'Gender',
  comparison: 'Comparison',
  priorReportSummary: 'Prior study',
  departmentTag: 'Department',
  reportDate: 'Report date',
  status: 'Status',
};
