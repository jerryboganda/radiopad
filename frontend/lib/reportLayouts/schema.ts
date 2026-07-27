/**
 * Report Templates (REPORT-TEMPLATES) — the LayoutJson document model, schema version 1.
 *
 * Mirrors `backend/RadioPad.Api/src/RadioPad.Api/Services/ReportLayouts/ReportLayoutModel.cs`
 * and `ReportLayoutParser.cs` field-for-field, including JSON key casing (camelCase
 * both directions) and validation limits. Keep the two in sync — the server is the
 * source of truth for what actually gets persisted and rendered; this file exists
 * so the designer UI can validate and preview without a round trip.
 *
 * The mandatory "Powered by RadioPad — radiopadstudio.com" branding footer has NO
 * field here on purpose — see `lib/reportLayouts/accents.ts` (`BRANDING_FOOTER_TEXT`)
 * and the server's `ReportLayoutBranding.FooterText`. It is rendered unconditionally,
 * never read from this document.
 */

export const REPORT_LAYOUT_SCHEMA_VERSION = 1 as const;

export const MAX_LAYOUT_JSON_BYTES = 256 * 1024;
export const MAX_LOGO_BYTES = 150 * 1024;
export const MAX_BLOCKS = 24;

export type PageSize = 'letter' | 'a4';
export type LayoutFont = 'sans' | 'serif' | 'mono';
export type AccentColor = 'graphite' | 'navy' | 'teal' | 'burgundy' | 'forest' | 'slate';
export type BlockAlign = 'left' | 'center' | 'right';
export type HeadingStyle = 'uppercase' | 'accent-bar' | 'underline' | 'plain';
export type LogoPosition = 'left' | 'right' | 'above';
export type DividerStyle = 'line' | 'accent' | 'space';

export type SectionKey =
  | 'indication' | 'technique' | 'comparison'
  | 'findings' | 'impression' | 'recommendations';

export const SECTION_KEYS: readonly SectionKey[] = [
  'indication', 'technique', 'comparison', 'findings', 'impression', 'recommendations',
];

export type StudyFieldKey =
  | 'patientReference' | 'accessionNumber' | 'modality' | 'bodyPart'
  | 'contrast' | 'age' | 'gender' | 'comparison' | 'priorReportSummary'
  | 'departmentTag' | 'reportDate' | 'status';

export const STUDY_FIELD_KEYS: readonly StudyFieldKey[] = [
  'patientReference', 'accessionNumber', 'modality', 'bodyPart',
  'contrast', 'age', 'gender', 'comparison', 'priorReportSummary',
  'departmentTag', 'reportDate', 'status',
];

export interface PageSetup {
  size: PageSize;
  marginPt: number;
  font: LayoutFont;
  baseFontSizePt: number;
  accent: AccentColor;
  showPageNumbers: boolean;
}

export interface LogoConfig {
  /** `data:image/(png|jpeg);base64,...` — ≤200,000 chars, ≤150KB decoded. */
  dataUrl: string;
  widthPt: number;
}

/** One contiguous run of identically-styled text within a `RichTextLine`. */
export interface RichTextRun {
  text: string;
  bold?: boolean;
  italic?: boolean;
  underline?: boolean;
  /** Per-run font override; absent = inherit the page font. */
  font?: LayoutFont;
  /** Per-run absolute point size override (6-24); absent = the line's base size. */
  sizePt?: number;
}

/** One line of the letterhead address block — a sequence of independently-styled runs. */
export interface RichTextLine {
  runs: RichTextRun[];
}

export interface LetterheadBlock {
  id: string;
  type: 'letterhead';
  clinicName: string | null;
  lines: RichTextLine[];
  logo: LogoConfig | null;
  logoPosition: LogoPosition;
  align: BlockAlign;
  showAccentRule: boolean;
}

export interface StudyInfoField {
  key: StudyFieldKey;
  label: string | null;
}

export interface StudyInfoBlock {
  id: string;
  type: 'studyInfo';
  columns: 1 | 2 | 3;
  showBox: boolean;
  fields: StudyInfoField[];
}

export interface SectionBlock {
  id: string;
  type: 'section';
  section: SectionKey;
  label: string | null;
  headingStyle: HeadingStyle;
  hideIfEmpty: boolean;
}

export interface SignaturesBlock {
  id: string;
  type: 'signatures';
  showDate: boolean;
  showNote: boolean;
  showHash: boolean;
  showSignatureLine: boolean;
}

export interface TextBlock {
  id: string;
  type: 'text';
  content: string;
  align: BlockAlign;
  italic: boolean;
  fontSizeDelta: -2 | -1 | 0 | 1 | 2;
}

export interface DividerBlock {
  id: string;
  type: 'divider';
  style: DividerStyle;
  spacePt: number;
}

export type LayoutBlock =
  | LetterheadBlock | StudyInfoBlock | SectionBlock
  | SignaturesBlock | TextBlock | DividerBlock;

export interface LayoutFooter {
  showStatusLine: boolean;
  /** ≤200 chars. Never the branding line — see the module doc comment above. */
  customText: string | null;
}

export interface ReportLayoutJson {
  schemaVersion: 1;
  page: PageSetup;
  blocks: LayoutBlock[];
  footer: LayoutFooter;
}

export const DEFAULT_PAGE_SETUP: PageSetup = {
  size: 'letter',
  marginPt: 36,
  font: 'sans',
  baseFontSizePt: 11,
  accent: 'graphite',
  showPageNumbers: true,
};

export const DEFAULT_SECTION_LABEL: Record<SectionKey, string> = {
  indication: 'Indication',
  technique: 'Technique',
  comparison: 'Comparison',
  findings: 'Findings',
  impression: 'Impression',
  recommendations: 'Recommendations',
};

export const DEFAULT_STUDY_FIELD_LABEL: Record<StudyFieldKey, string> = {
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

function newBlockId(prefix: string): string {
  const rand = Math.random().toString(36).slice(2, 8);
  return `${prefix}-${rand}`;
}

export function createEmptyLayout(): ReportLayoutJson {
  return {
    schemaVersion: REPORT_LAYOUT_SCHEMA_VERSION,
    page: { ...DEFAULT_PAGE_SETUP },
    blocks: [
      {
        id: newBlockId('letterhead'), type: 'letterhead',
        clinicName: null, lines: [], logo: null,
        logoPosition: 'left', align: 'left', showAccentRule: true,
      },
      {
        id: newBlockId('studyInfo'), type: 'studyInfo', columns: 2, showBox: false,
        fields: [
          { key: 'accessionNumber', label: null },
          { key: 'modality', label: null },
          { key: 'bodyPart', label: null },
          { key: 'reportDate', label: null },
        ],
      },
      ...SECTION_KEYS.map((section): SectionBlock => ({
        id: newBlockId(section), type: 'section', section,
        label: null, headingStyle: 'uppercase', hideIfEmpty: true,
      })),
      { id: newBlockId('signatures'), type: 'signatures', showDate: true, showNote: true, showHash: false, showSignatureLine: true },
    ],
    footer: { showStatusLine: true, customText: null },
  };
}

export function newBlockOfType(type: LayoutBlock['type']): LayoutBlock {
  switch (type) {
    case 'letterhead':
      return { id: newBlockId('letterhead'), type, clinicName: null, lines: [], logo: null, logoPosition: 'left', align: 'left', showAccentRule: true };
    case 'studyInfo':
      return { id: newBlockId('studyInfo'), type, columns: 2, showBox: false, fields: [] };
    case 'section':
      return { id: newBlockId('section'), type, section: 'findings', label: null, headingStyle: 'uppercase', hideIfEmpty: true };
    case 'signatures':
      return { id: newBlockId('signatures'), type, showDate: true, showNote: true, showHash: false, showSignatureLine: true };
    case 'text':
      return { id: newBlockId('text'), type, content: '', align: 'left', italic: false, fontSizeDelta: 0 };
    case 'divider':
      return { id: newBlockId('divider'), type, style: 'line', spacePt: 16 };
  }
}

// --------------------------------------------------------------- validation

export type ValidationResult = { ok: true; value: ReportLayoutJson } | { ok: false; errors: string[] };

const PAGE_SIZES: readonly PageSize[] = ['letter', 'a4'];
const FONTS: readonly LayoutFont[] = ['sans', 'serif', 'mono'];
const ACCENTS: readonly AccentColor[] = ['graphite', 'navy', 'teal', 'burgundy', 'forest', 'slate'];
const ALIGNS: readonly BlockAlign[] = ['left', 'center', 'right'];
const HEADING_STYLES: readonly HeadingStyle[] = ['uppercase', 'accent-bar', 'underline', 'plain'];
const LOGO_POSITIONS: readonly LogoPosition[] = ['left', 'right', 'above'];
const DIVIDER_STYLES: readonly DividerStyle[] = ['line', 'accent', 'space'];
const BLOCK_TYPES = ['letterhead', 'studyInfo', 'section', 'signatures', 'text', 'divider'] as const;

/**
 * Client-side mirror of `ReportLayoutParser.TryParse` — hand-rolled, no schema
 * library dependency (consistent with the rest of the codebase). Used by the
 * designer to show inline errors before ever calling the server, and by the
 * preset unit tests to assert every shipped preset is actually valid.
 */
export function validateLayout(value: unknown): ValidationResult {
  const errors: string[] = [];
  if (typeof value !== 'object' || value === null) {
    return { ok: false, errors: ['layout must be an object.'] };
  }
  const v = value as Record<string, unknown>;

  if (v.schemaVersion !== 1) {
    errors.push(`Unsupported schemaVersion ${String(v.schemaVersion)}; this build supports only 1.`);
  }

  const page = validatePage(v.page, errors);
  const blocks = validateBlocks(v.blocks, errors);
  const footer = validateFooter(v.footer, errors);

  const size = new TextEncoder().encode(JSON.stringify(value)).length;
  if (size > MAX_LAYOUT_JSON_BYTES) {
    errors.push(`layout exceeds the ${MAX_LAYOUT_JSON_BYTES / 1024} KB limit.`);
  }

  if (errors.length > 0 || !page || !footer) {
    return { ok: false, errors };
  }
  return { ok: true, value: { schemaVersion: 1, page, blocks, footer } };
}

function validatePage(raw: unknown, errors: string[]): PageSetup | null {
  if (typeof raw !== 'object' || raw === null) {
    errors.push('page is required and must be an object.');
    return null;
  }
  const p = raw as Record<string, unknown>;
  const size = enumOrDefault(p.size, PAGE_SIZES, DEFAULT_PAGE_SETUP.size, 'page.size', errors);
  const marginPt = numberInRange(p.marginPt, 24, 72, DEFAULT_PAGE_SETUP.marginPt, 'page.marginPt', errors);
  const font = enumOrDefault(p.font, FONTS, DEFAULT_PAGE_SETUP.font, 'page.font', errors);
  const baseFontSizePt = numberInRange(p.baseFontSizePt, 8, 14, DEFAULT_PAGE_SETUP.baseFontSizePt, 'page.baseFontSizePt', errors);
  const accent = enumOrDefault(p.accent, ACCENTS, DEFAULT_PAGE_SETUP.accent, 'page.accent', errors);
  const showPageNumbers = typeof p.showPageNumbers === 'boolean' ? p.showPageNumbers : true;
  return { size, marginPt, font, baseFontSizePt, accent, showPageNumbers };
}

function validateFooter(raw: unknown, errors: string[]): LayoutFooter | null {
  if (raw === undefined) return { showStatusLine: true, customText: null };
  if (typeof raw !== 'object' || raw === null) {
    errors.push('footer must be an object.');
    return null;
  }
  const f = raw as Record<string, unknown>;
  const showStatusLine = typeof f.showStatusLine === 'boolean' ? f.showStatusLine : true;
  const customText = optionalString(f.customText, 200, 'footer.customText', errors);
  return { showStatusLine, customText };
}

function validateBlocks(raw: unknown, errors: string[]): LayoutBlock[] {
  if (raw === undefined) return [];
  if (!Array.isArray(raw)) {
    errors.push('blocks must be an array.');
    return [];
  }
  if (raw.length > MAX_BLOCKS) {
    errors.push(`blocks may contain at most ${MAX_BLOCKS} entries.`);
    return [];
  }

  const out: LayoutBlock[] = [];
  const seenIds = new Set<string>();
  const seenSingleton = new Set<string>();
  const seenSections = new Set<SectionKey>();

  raw.forEach((entry, index) => {
    const path = `blocks[${index}]`;
    if (typeof entry !== 'object' || entry === null) {
      errors.push(`${path} must be an object.`);
      return;
    }
    const b = entry as Record<string, unknown>;
    const id = requiredString(b.id, 40, `${path}.id`, errors);
    if (id === null) return;
    if (seenIds.has(id)) {
      errors.push(`${path}: duplicate block id "${id}".`);
      return;
    }
    seenIds.add(id);

    const type = b.type;
    if (typeof type !== 'string' || !(BLOCK_TYPES as readonly string[]).includes(type)) {
      errors.push(`${path}.type "${String(type)}" is not a recognised block type.`);
      return;
    }

    if (type === 'letterhead' || type === 'studyInfo' || type === 'signatures') {
      if (seenSingleton.has(type)) {
        errors.push(`${path}: only one "${type}" block is allowed per layout.`);
        return;
      }
      seenSingleton.add(type);
    }

    const block = validateBlockByType(type as LayoutBlock['type'], id, b, path, errors, seenSections);
    if (block) out.push(block);
  });

  return out;
}

function validateBlockByType(
  type: LayoutBlock['type'], id: string, b: Record<string, unknown>, path: string,
  errors: string[], seenSections: Set<SectionKey>,
): LayoutBlock | null {
  switch (type) {
    case 'letterhead': {
      const clinicName = optionalString(b.clinicName, 200, `${path}.clinicName`, errors);
      const lines = richLineArray(b.lines, 4, 120, `${path}.lines`, errors);
      const logo = validateLogo(b.logo, `${path}.logo`, errors);
      const logoPosition = enumOrDefault(b.logoPosition, LOGO_POSITIONS, 'left', `${path}.logoPosition`, errors);
      const align = enumOrDefault(b.align, ALIGNS, 'left', `${path}.align`, errors);
      const showAccentRule = typeof b.showAccentRule === 'boolean' ? b.showAccentRule : false;
      return { id, type, clinicName, lines, logo, logoPosition, align, showAccentRule };
    }
    case 'studyInfo': {
      const columns = [1, 2, 3].includes(b.columns as number) ? (b.columns as 1 | 2 | 3) : 2;
      const showBox = typeof b.showBox === 'boolean' ? b.showBox : false;
      const fields = Array.isArray(b.fields) ? b.fields.slice(0, 12).map((f) => {
        const fo = (f ?? {}) as Record<string, unknown>;
        const key = (STUDY_FIELD_KEYS as readonly string[]).includes(fo.key as string) ? (fo.key as StudyFieldKey) : 'accessionNumber';
        const label = optionalString(fo.label, 80, `${path}.fields[].label`, errors);
        return { key, label };
      }) : [];
      return { id, type, columns, showBox, fields };
    }
    case 'section': {
      const section = SECTION_KEYS.includes(b.section as SectionKey) ? (b.section as SectionKey) : null;
      if (section === null) {
        errors.push(`${path}.section is required and must be one of: ${SECTION_KEYS.join(', ')}.`);
        return null;
      }
      if (seenSections.has(section)) {
        errors.push(`${path}: a "${section}" section block is already present; each section may appear at most once.`);
        return null;
      }
      seenSections.add(section);
      const label = optionalString(b.label, 60, `${path}.label`, errors);
      const headingStyle = enumOrDefault(b.headingStyle, HEADING_STYLES, 'uppercase', `${path}.headingStyle`, errors);
      const hideIfEmpty = typeof b.hideIfEmpty === 'boolean' ? b.hideIfEmpty : true;
      return { id, type, section, label, headingStyle, hideIfEmpty };
    }
    case 'signatures':
      return {
        id, type,
        showDate: b.showDate !== false,
        showNote: b.showNote !== false,
        showHash: b.showHash === true,
        showSignatureLine: b.showSignatureLine !== false,
      };
    case 'text': {
      const content = requiredString(b.content, 4000, `${path}.content`, errors) ?? '';
      const align = enumOrDefault(b.align, ALIGNS, 'left', `${path}.align`, errors);
      const italic = typeof b.italic === 'boolean' ? b.italic : false;
      const delta = typeof b.fontSizeDelta === 'number' && b.fontSizeDelta >= -2 && b.fontSizeDelta <= 2
        ? (b.fontSizeDelta as -2 | -1 | 0 | 1 | 2) : 0;
      return { id, type, content, align, italic, fontSizeDelta: delta };
    }
    case 'divider': {
      const style = enumOrDefault(b.style, DIVIDER_STYLES, 'line', `${path}.style`, errors);
      const spacePt = numberInRange(b.spacePt, 4, 48, 16, `${path}.spacePt`, errors);
      return { id, type, style, spacePt };
    }
  }
}

function validateLogo(raw: unknown, path: string, errors: string[]): LogoConfig | null {
  if (raw === null || raw === undefined) return null;
  if (typeof raw !== 'object') {
    errors.push(`${path} must be an object or null.`);
    return null;
  }
  const l = raw as Record<string, unknown>;
  const dataUrl = l.dataUrl;
  if (typeof dataUrl !== 'string' || !/^data:image\/(png|jpeg);base64,/.test(dataUrl)) {
    errors.push(`${path}.dataUrl must be a data:image/png;base64, or data:image/jpeg;base64, URL.`);
    return null;
  }
  if (dataUrl.length > 200_000) {
    errors.push(`${path}.dataUrl exceeds the 200,000 character limit.`);
    return null;
  }
  const widthPt = numberInRange(l.widthPt, 40, 250, 120, `${path}.widthPt`, errors);
  return { dataUrl, widthPt };
}

function enumOrDefault<T extends string>(value: unknown, allowed: readonly T[], fallback: T, path: string, errors: string[]): T {
  if (value === undefined) return fallback;
  if (typeof value === 'string' && (allowed as readonly string[]).includes(value)) return value as T;
  errors.push(`${path} must be one of: ${allowed.join(', ')}.`);
  return fallback;
}

function numberInRange(value: unknown, min: number, max: number, fallback: number, path: string, errors: string[]): number {
  if (value === undefined) return fallback;
  if (typeof value === 'number' && value >= min && value <= max) return value;
  errors.push(`${path} must be a number between ${min} and ${max}.`);
  return fallback;
}

function requiredString(value: unknown, maxLen: number, path: string, errors: string[]): string | null {
  if (typeof value !== 'string') {
    errors.push(`${path} is required and must be a string.`);
    return null;
  }
  if (value.length > maxLen) {
    errors.push(`${path} exceeds the ${maxLen} character limit.`);
    return null;
  }
  return value;
}

function optionalString(value: unknown, maxLen: number, path: string, errors: string[]): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'string') {
    errors.push(`${path} must be a string or null.`);
    return null;
  }
  if (value.length > maxLen) {
    errors.push(`${path} exceeds the ${maxLen} character limit.`);
    return null;
  }
  return value;
}

const MAX_RUNS_PER_LINE = 20;

/**
 * Validates the letterhead's rich-text lines. Each entry is either a legacy plain
 * string (pre-rich-text saved layouts — wrapped as a single unstyled run so old
 * layouts keep loading unchanged) or an object `{ runs: [...] }` carrying per-run
 * bold/italic/underline/font/size. A line's combined run text may not exceed
 * `maxLen` characters, matching the original per-line cap.
 */
function richLineArray(value: unknown, maxItems: number, maxLen: number, path: string, errors: string[]): RichTextLine[] {
  if (value === undefined) return [];
  if (!Array.isArray(value)) {
    errors.push(`${path} must be an array.`);
    return [];
  }
  if (value.length > maxItems) {
    errors.push(`${path} may contain at most ${maxItems} entries.`);
    return [];
  }
  const out: RichTextLine[] = [];
  for (let i = 0; i < value.length; i++) {
    const item = value[i];
    const linePath = `${path}[${i}]`;
    if (typeof item === 'string') {
      if (item.length > maxLen) {
        errors.push(`${path} entries must total at most ${maxLen} characters.`);
        return [];
      }
      out.push({ runs: item.length > 0 ? [{ text: item }] : [] });
      continue;
    }
    if (typeof item !== 'object' || item === null || !Array.isArray((item as Record<string, unknown>).runs)) {
      errors.push(`${linePath} must be a string or an object with a "runs" array.`);
      return [];
    }
    const rawRuns = (item as { runs: unknown[] }).runs;
    if (rawRuns.length > MAX_RUNS_PER_LINE) {
      errors.push(`${linePath}.runs may contain at most ${MAX_RUNS_PER_LINE} entries.`);
      return [];
    }
    const runs: RichTextRun[] = [];
    let totalLen = 0;
    for (const rawRun of rawRuns) {
      const run = richTextRun(rawRun, linePath, errors);
      if (run === null) return [];
      totalLen += run.text.length;
      runs.push(run);
    }
    if (totalLen > maxLen) {
      errors.push(`${path} entries must total at most ${maxLen} characters.`);
      return [];
    }
    out.push({ runs });
  }
  return out;
}

function richTextRun(value: unknown, linePath: string, errors: string[]): RichTextRun | null {
  if (typeof value !== 'object' || value === null) {
    errors.push(`${linePath}.runs entries must be objects.`);
    return null;
  }
  const r = value as Record<string, unknown>;
  if (typeof r.text !== 'string') {
    errors.push(`${linePath}.runs[].text is required and must be a string.`);
    return null;
  }
  const run: RichTextRun = { text: r.text };
  if (r.bold === true) run.bold = true;
  if (r.italic === true) run.italic = true;
  if (r.underline === true) run.underline = true;
  if (r.font !== undefined) {
    if (typeof r.font !== 'string' || !(FONTS as readonly string[]).includes(r.font)) {
      errors.push(`${linePath}.runs[].font must be one of: ${FONTS.join(', ')}.`);
      return null;
    }
    run.font = r.font as LayoutFont;
  }
  if (r.sizePt !== undefined) {
    if (typeof r.sizePt !== 'number' || r.sizePt < 6 || r.sizePt > 24) {
      errors.push(`${linePath}.runs[].sizePt must be a number between 6 and 24.`);
      return null;
    }
    run.sizePt = r.sizePt;
  }
  return run;
}
