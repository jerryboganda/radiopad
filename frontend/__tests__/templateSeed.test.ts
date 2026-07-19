// F2 — seeding a report from a template's normal values must map only the canonical report
// sections, skip empty normals, and parse both stored shapes defensively.
import { describe, it, expect } from 'vitest';
import { sectionScaffoldText, sectionScaffoldVariants, sectionsToReportSeed, parseTemplateSections } from '@/lib/templateSeed';

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

// ── Scaffold precedence (F2) ────────────────────────────────────────────────────────────────
// The report editor's swap engine had ZERO coverage, and read `placeholder` alone. A template
// authored with only `normal` therefore seeded nothing — while the backend template PREVIEW
// rendered the normal prose correctly ("the section's 'normal' is what a new report is actually
// seeded with"). The author got an affirmative confirmation the feature worked, then opened a
// blank report. These pin both halves of the fix.
describe('sectionScaffoldText', () => {
  it('prefers the normal body over the placeholder', () => {
    expect(sectionScaffoldText({ id: 'findings', placeholder: 'Describe…', normal: 'Lungs clear.' }))
      .toBe('Lungs clear.');
  });

  it('falls back to the placeholder when no normal is defined', () => {
    // The 40+ shipped templates are placeholder-only, so this path must keep working unchanged.
    expect(sectionScaffoldText({ id: 'findings', placeholder: 'Describe…' })).toBe('Describe…');
  });

  it('treats a whitespace-only normal as absent', () => {
    expect(sectionScaffoldText({ id: 'findings', placeholder: 'Describe…', normal: '   ' }))
      .toBe('Describe…');
  });

  it('is empty when the section defines neither', () => {
    expect(sectionScaffoldText({ id: 'findings' })).toBe('');
  });
});

describe('sectionScaffoldVariants', () => {
  it('counts BOTH the placeholder and the normal as untouched scaffold', () => {
    // Indexing only placeholders meant normal-seeded prose was classed as radiologist-authored and
    // survived a rebind verbatim — stale chest technique under a brain study.
    const variants = sectionScaffoldVariants({
      id: 'technique', placeholder: 'Describe…', normal: 'Axial CT of the chest.',
    });
    expect(variants).toContain('Describe…');
    expect(variants).toContain('Axial CT of the chest.');
  });

  it('omits blank entries so empty prose is never matched as scaffold', () => {
    expect(sectionScaffoldVariants({ id: 'findings', placeholder: '  ', normal: '' })).toEqual([]);
  });

  it('returns the placeholder alone for the shipped placeholder-only templates', () => {
    expect(sectionScaffoldVariants({ id: 'findings', placeholder: 'Describe…' })).toEqual(['Describe…']);
  });
});
