// F2 — seeding a report from a template's normal values must map only the canonical report
// sections, skip empty normals, and parse both stored shapes defensively.
import { describe, it, expect } from 'vitest';
import { sectionsToReportSeed, parseTemplateSections } from '@/lib/templateSeed';

describe('sectionsToReportSeed', () => {
  it('maps canonical sections by id (case-insensitive) using their normal body', () => {
    const seed = sectionsToReportSeed([
      { id: 'Findings', normal: 'No acute intracranial abnormality.' },
      { id: 'impression', normal: 'Normal study.' },
      { id: 'technique', normal: 'Axial CT of the head without contrast.' },
    ]);
    expect(seed).toEqual({
      findings: 'No acute intracranial abnormality.',
      impression: 'Normal study.',
      technique: 'Axial CT of the head without contrast.',
    });
  });

  it('skips empty / whitespace normals so a field is never blanked', () => {
    const seed = sectionsToReportSeed([
      { id: 'findings', normal: '   ' },
      { id: 'impression', normal: '' },
      { id: 'comparison' },
    ]);
    expect(seed).toEqual({});
  });

  it('ignores non-report sections a template may define for its own structure', () => {
    const seed = sectionsToReportSeed([
      { id: 'custom_addendum', normal: 'ignored' },
      { id: 'findings', normal: 'seen' },
    ]);
    expect(seed).toEqual({ findings: 'seen' });
  });
});

describe('parseTemplateSections', () => {
  it('parses the wrapped { sections } shape', () => {
    const rows = parseTemplateSections(JSON.stringify({ sections: [{ id: 'findings', normal: 'x' }] }));
    expect(rows).toEqual([{ id: 'findings', normal: 'x' }]);
  });

  it('parses a bare array', () => {
    const rows = parseTemplateSections(JSON.stringify([{ id: 'impression' }]));
    expect(rows).toEqual([{ id: 'impression' }]);
  });

  it('returns [] for null / invalid JSON', () => {
    expect(parseTemplateSections(null)).toEqual([]);
    expect(parseTemplateSections('{not json')).toEqual([]);
  });
});
