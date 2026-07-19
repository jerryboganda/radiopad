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

/**
 * The scaffold text a section should contribute to a report.
 *
 * Mirrors the backend's template-preview precedence exactly (`OtherControllers.cs`: "the section's
 * 'normal' (default body) is what a new report is actually seeded with, so it is the preferred
 * preview fallback; the greyed placeholder is only used when no normal is defined").
 *
 * The editor's swap engine originally read `placeholder` alone, so a template authored with only
 * `normal` seeded nothing — while the preview rendered the normal prose correctly. The author got
 * an affirmative confirmation the feature worked, then opened a blank report.
 */
export function sectionScaffoldText(section: TemplateSection): string {
  return section.normal?.trim() || section.placeholder?.trim() || '';
}

/**
 * Every string that counts as untouched scaffold for a section — BOTH the placeholder and the
 * normal body.
 *
 * Indexing only placeholders meant normal-seeded prose was never recognised as scaffold: on a
 * rebind it was classified as radiologist-authored and preserved verbatim under the new template,
 * leaving e.g. chest technique text sitting under a brain study. Stale normals are more dangerous
 * than a blank section, because a blank section is obvious and stale prose reads as deliberate.
 */
export function sectionScaffoldVariants(section: TemplateSection): string[] {
  return [section.placeholder, section.normal]
    .map((v) => v?.trim() ?? '')
    .filter((v) => v.length > 0);
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
