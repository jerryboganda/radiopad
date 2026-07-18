// F2 — seed a new report from a template's "normal" (default) section values, so the radiologist
// starts from a normal study and edits only the exceptions. Pure + deterministic; unit-tested.
//
// Only the six canonical report sections are mapped (a template may define extra sections for its
// own preview structure, but a Report has exactly these fields). Matching is by section id,
// case-insensitively; empty/whitespace normals are skipped so they never blank out a field.

export interface TemplateSection {
  id: string;
  label?: string;
  placeholder?: string;
  normal?: string;
}

export interface ReportSeed {
  indication?: string;
  technique?: string;
  comparison?: string;
  findings?: string;
  impression?: string;
  recommendations?: string;
}

const FIELD_KEYS = [
  'indication',
  'technique',
  'comparison',
  'findings',
  'impression',
  'recommendations',
] as const;

const FIELD_SET: ReadonlySet<string> = new Set(FIELD_KEYS);

/** Map a template's sections to the report fields their `normal` bodies should seed. */
export function sectionsToReportSeed(sections: readonly TemplateSection[]): ReportSeed {
  const seed: ReportSeed = {};
  for (const s of sections) {
    const key = s.id?.trim().toLowerCase();
    const normal = s.normal?.trim();
    if (!key || !normal || !FIELD_SET.has(key)) continue;
    (seed as Record<string, string>)[key] = normal;
  }
  return seed;
}

/** Parse a template's stored `sectionsJson` (bare array or `{ sections: [...] }`) defensively. */
export function parseTemplateSections(sectionsJson: string | null | undefined): TemplateSection[] {
  if (!sectionsJson) return [];
  try {
    const parsed = JSON.parse(sectionsJson) as { sections?: TemplateSection[] } | TemplateSection[];
    const arr = Array.isArray(parsed) ? parsed : parsed.sections ?? [];
    return Array.isArray(arr) ? arr.filter((s) => s && typeof s.id === 'string') : [];
  } catch {
    return [];
  }
}
