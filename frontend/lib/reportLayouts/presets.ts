/**
 * Report Templates (RPT-030) — built-in starter designs. Presets live ONLY here:
 * "Use preset" in the gallery POSTs the full `layoutJson` to `/api/report-layouts`,
 * so the backend has zero preset-specific knowledge — it only ever sees a normal
 * saved layout. `presets.test.ts` asserts every entry below passes `validateLayout`.
 */
import type { ReportLayoutJson, SectionKey } from './schema';

const SECTIONS: readonly SectionKey[] = [
  'indication', 'technique', 'comparison', 'findings', 'impression', 'recommendations',
];

export interface ReportLayoutPreset {
  key: string;
  name: string;
  description: string;
  layout: ReportLayoutJson;
}

export const REPORT_LAYOUT_PRESETS: ReportLayoutPreset[] = [
  {
    key: 'classic',
    name: 'Classic',
    description: "RadioPad's original document look — a close approximation of the current default export.",
    layout: {
      schemaVersion: 1,
      page: { size: 'letter', marginPt: 36, font: 'sans', baseFontSizePt: 11, accent: 'graphite', showPageNumbers: true },
      blocks: [
        { id: 'classic-letterhead', type: 'letterhead', clinicName: null, lines: [], logo: null, logoPosition: 'left', align: 'left', showAccentRule: false },
        { id: 'classic-studyinfo', type: 'studyInfo', columns: 1, showBox: false, fields: [
          { key: 'accessionNumber', label: null },
          { key: 'modality', label: null },
          { key: 'bodyPart', label: null },
        ] },
        ...SECTIONS.map((section): ReportLayoutJson['blocks'][number] => ({
          id: `classic-${section}`, type: 'section', section,
          label: null, headingStyle: 'uppercase', hideIfEmpty: false,
        })),
        { id: 'classic-signatures', type: 'signatures', showDate: true, showNote: false, showHash: false, showSignatureLine: false },
      ],
      footer: { showStatusLine: true, customText: null },
    },
  },
  {
    key: 'modern',
    name: 'Modern',
    description: 'A clean two-column study grid with accent-bar section headings — a fresher clinical look.',
    layout: {
      schemaVersion: 1,
      page: { size: 'letter', marginPt: 40, font: 'sans', baseFontSizePt: 11, accent: 'teal', showPageNumbers: true },
      blocks: [
        { id: 'modern-letterhead', type: 'letterhead', clinicName: null, lines: [], logo: null, logoPosition: 'left', align: 'left', showAccentRule: true },
        { id: 'modern-studyinfo', type: 'studyInfo', columns: 2, showBox: true, fields: [
          { key: 'accessionNumber', label: null },
          { key: 'modality', label: null },
          { key: 'bodyPart', label: null },
          { key: 'contrast', label: null },
          { key: 'age', label: null },
          { key: 'gender', label: null },
          { key: 'reportDate', label: null },
        ] },
        ...SECTIONS.map((section): ReportLayoutJson['blocks'][number] => ({
          id: `modern-${section}`, type: 'section', section,
          label: null, headingStyle: 'accent-bar',
          hideIfEmpty: section === 'comparison' || section === 'recommendations',
        })),
        { id: 'modern-signatures', type: 'signatures', showDate: true, showNote: true, showHash: false, showSignatureLine: true },
      ],
      footer: { showStatusLine: true, customText: null },
    },
  },
  {
    key: 'compact',
    name: 'Compact',
    description: 'Tighter margins and type for dense multi-page reports; empty sections are omitted entirely.',
    layout: {
      schemaVersion: 1,
      page: { size: 'letter', marginPt: 28, font: 'sans', baseFontSizePt: 10, accent: 'slate', showPageNumbers: true },
      blocks: [
        { id: 'compact-letterhead', type: 'letterhead', clinicName: null, lines: [], logo: null, logoPosition: 'left', align: 'left', showAccentRule: false },
        { id: 'compact-studyinfo', type: 'studyInfo', columns: 3, showBox: false, fields: [
          { key: 'accessionNumber', label: null },
          { key: 'modality', label: null },
          { key: 'bodyPart', label: null },
          { key: 'contrast', label: null },
          { key: 'age', label: null },
          { key: 'gender', label: null },
        ] },
        ...SECTIONS.map((section): ReportLayoutJson['blocks'][number] => ({
          id: `compact-${section}`, type: 'section', section,
          label: null, headingStyle: 'plain', hideIfEmpty: true,
        })),
        { id: 'compact-signatures', type: 'signatures', showDate: true, showNote: false, showHash: false, showSignatureLine: false },
      ],
      footer: { showStatusLine: false, customText: null },
    },
  },
  {
    key: 'formal-letterhead',
    name: 'Formal Letterhead',
    description: 'A centered, serif letterhead with a signature line and verification hash — a formal presentation.',
    layout: {
      schemaVersion: 1,
      page: { size: 'letter', marginPt: 48, font: 'serif', baseFontSizePt: 11, accent: 'burgundy', showPageNumbers: true },
      blocks: [
        {
          id: 'formal-letterhead', type: 'letterhead', clinicName: null,
          lines: ['123 Radiology Way, Suite 400', 'Reporting Department'],
          logo: null, logoPosition: 'above', align: 'center', showAccentRule: true,
        },
        { id: 'formal-studyinfo', type: 'studyInfo', columns: 2, showBox: true, fields: [
          { key: 'accessionNumber', label: null },
          { key: 'modality', label: null },
          { key: 'bodyPart', label: null },
          { key: 'contrast', label: null },
          { key: 'reportDate', label: null },
          { key: 'status', label: null },
        ] },
        ...SECTIONS.map((section): ReportLayoutJson['blocks'][number] => ({
          id: `formal-${section}`, type: 'section', section,
          label: null, headingStyle: 'underline', hideIfEmpty: false,
        })),
        { id: 'formal-signatures', type: 'signatures', showDate: true, showNote: true, showHash: true, showSignatureLine: true },
      ],
      footer: { showStatusLine: true, customText: null },
    },
  },
];

export function findPreset(key: string): ReportLayoutPreset | undefined {
  return REPORT_LAYOUT_PRESETS.find((p) => p.key === key);
}
