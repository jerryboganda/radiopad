// Report Templates (RPT-030) — every shipped preset must be a valid,
// server-acceptable layout: the gallery renders them without ever calling the
// API, so a broken preset would only surface when a radiologist tried to save it.
import { describe, it, expect } from 'vitest';
import { REPORT_LAYOUT_PRESETS, findPreset } from '@/lib/reportLayouts/presets';
import { validateLayout, MAX_LAYOUT_JSON_BYTES } from '@/lib/reportLayouts/schema';

describe('report layout presets', () => {
  it('ships at least four presets with unique keys', () => {
    expect(REPORT_LAYOUT_PRESETS.length).toBeGreaterThanOrEqual(4);
    const keys = REPORT_LAYOUT_PRESETS.map((p) => p.key);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it.each(REPORT_LAYOUT_PRESETS.map((p) => [p.key, p] as const))(
    'preset "%s" passes validateLayout',
    (_key, preset) => {
      const result = validateLayout(preset.layout);
      expect(result.ok, JSON.stringify(!result.ok ? result.errors : [])).toBe(true);
    },
  );

  it.each(REPORT_LAYOUT_PRESETS.map((p) => [p.key, p] as const))(
    'preset "%s" has unique block ids',
    (_key, preset) => {
      const ids = preset.layout.blocks.map((b) => b.id);
      expect(new Set(ids).size).toBe(ids.length);
    },
  );

  it.each(REPORT_LAYOUT_PRESETS.map((p) => [p.key, p] as const))(
    'preset "%s" has at most one letterhead/studyInfo/signatures block',
    (_key, preset) => {
      for (const type of ['letterhead', 'studyInfo', 'signatures'] as const) {
        const count = preset.layout.blocks.filter((b) => b.type === type).length;
        expect(count).toBeLessThanOrEqual(1);
      }
    },
  );

  it.each(REPORT_LAYOUT_PRESETS.map((p) => [p.key, p] as const))(
    'preset "%s" has at most one block per section key',
    (_key, preset) => {
      const sectionKeys = preset.layout.blocks
        .filter((b) => b.type === 'section')
        .map((b) => (b as { section: string }).section);
      expect(new Set(sectionKeys).size).toBe(sectionKeys.length);
    },
  );

  it.each(REPORT_LAYOUT_PRESETS.map((p) => [p.key, p] as const))(
    'preset "%s" is well under the size cap',
    (_key, preset) => {
      const bytes = new TextEncoder().encode(JSON.stringify(preset.layout)).length;
      expect(bytes).toBeLessThan(MAX_LAYOUT_JSON_BYTES);
    },
  );

  it('findPreset resolves a known key and returns undefined for an unknown one', () => {
    expect(findPreset('classic')?.name).toBe('Classic');
    expect(findPreset('does-not-exist')).toBeUndefined();
  });
});
