// Phase 3 — regulated (assist-only) feature registry for the admin governance surface. Mirrors the
// backend `RegulatedFeatures` catalog. Flags live in Tenant.featureFlagsJson under the `regulated.`
// prefix and default OFF (regulatory review required). Pure helpers so the parse/toggle logic is
// unit-tested independently of React.

export const REGULATED_PREFIX = 'regulated.';

export interface RegulatedFeatureDef {
  key: string;
  title: string;
  description: string;
}

/** Keep in step with backend RadioPad.Application.Governance.RegulatedFeatures.Catalog. */
export const REGULATED_FEATURES: RegulatedFeatureDef[] = [
  {
    key: 'regulated.autoImpression',
    title: 'Automatic impression draft',
    description:
      'AI-drafted impression offered as an editable suggestion. The radiologist authors or confirms the final impression; never auto-applied and never signed.',
  },
  {
    key: 'regulated.criticalFindingFlagging',
    title: 'Critical-finding flagging',
    description:
      'Flags possible critical findings (e.g. suspected PE, a new mass) for a communicate/acknowledge workflow. Suggestion-only; requires explicit radiologist confirmation.',
  },
  {
    key: 'regulated.followUpStandardisation',
    title: 'Follow-up standardisation',
    description:
      'Cites structured follow-up frameworks (Fleischner, LI-RADS, TI-RADS, Bosniak) as a non-auto-applied, radiologist-confirmed suggestion.',
  },
  {
    key: 'regulated.intervalChangeTracking',
    title: 'Interval-change / RECIST tracking',
    description:
      'Computed interval deltas / RECIST-style lesion measurements the radiologist confirms. Assistive only.',
  },
];

export function parseFlags(featureFlagsJson: string | null | undefined): Record<string, boolean> {
  if (!featureFlagsJson) return {};
  try {
    const parsed: unknown = JSON.parse(featureFlagsJson);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    const out: Record<string, boolean> = {};
    for (const [k, v] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof v === 'boolean') out[k] = v;
      else if (v === 'true' || v === 'false') out[k] = v === 'true';
    }
    return out;
  } catch {
    return {};
  }
}

/** True only when the flag is explicitly enabled — absent / false / malformed → OFF (fail safe). */
export function isRegulatedEnabled(featureFlagsJson: string | null | undefined, key: string): boolean {
  return parseFlags(featureFlagsJson)[key] === true;
}

/** A new featureFlagsJson string with `key` set to `enabled`, preserving every other flag. */
export function setRegulatedFlag(
  featureFlagsJson: string | null | undefined,
  key: string,
  enabled: boolean,
): string {
  let obj: Record<string, unknown> = {};
  if (featureFlagsJson) {
    try {
      const parsed: unknown = JSON.parse(featureFlagsJson);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        obj = parsed as Record<string, unknown>;
      }
    } catch {
      obj = {};
    }
  }
  obj[key] = enabled;
  return JSON.stringify(obj, null, 2);
}
