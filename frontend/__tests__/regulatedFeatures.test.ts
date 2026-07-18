// Phase 3 — the admin surface must read regulated flags as OFF unless explicitly enabled, and
// toggling one must never disturb the others. A default-on bug would surface an unvalidated
// clinical-assist feature.
import { describe, it, expect } from 'vitest';
import {
  parseFlags,
  isRegulatedEnabled,
  setRegulatedFlag,
  REGULATED_FEATURES,
} from '@/lib/regulatedFeatures';

describe('isRegulatedEnabled', () => {
  it('is off by default (absent / empty / malformed)', () => {
    expect(isRegulatedEnabled(undefined, 'regulated.autoImpression')).toBe(false);
    expect(isRegulatedEnabled('{}', 'regulated.autoImpression')).toBe(false);
    expect(isRegulatedEnabled('{bad', 'regulated.autoImpression')).toBe(false);
    expect(isRegulatedEnabled('[]', 'regulated.autoImpression')).toBe(false);
  });

  it('is on only when the flag is explicitly true (bool or "true")', () => {
    expect(isRegulatedEnabled('{"regulated.autoImpression": true}', 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('{"regulated.autoImpression": "true"}', 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('{"regulated.autoImpression": false}', 'regulated.autoImpression')).toBe(false);
  });
});

describe('setRegulatedFlag', () => {
  it('sets a flag while preserving other (including non-regulated) flags', () => {
    const before = '{"billing.pro": true, "regulated.autoImpression": false}';
    const after = setRegulatedFlag(before, 'regulated.autoImpression', true);
    const parsed = JSON.parse(after);
    expect(parsed['regulated.autoImpression']).toBe(true);
    expect(parsed['billing.pro']).toBe(true); // untouched
  });

  it('starts from {} when the input is missing or malformed', () => {
    const after = setRegulatedFlag('not json', 'regulated.criticalFindingFlagging', true);
    expect(JSON.parse(after)).toEqual({ 'regulated.criticalFindingFlagging': true });
  });

  it('round-trips through parseFlags', () => {
    const json = setRegulatedFlag('{}', 'regulated.followUpStandardisation', true);
    expect(parseFlags(json)['regulated.followUpStandardisation']).toBe(true);
  });
});

describe('REGULATED_FEATURES catalog', () => {
  it('lists four features, all under the regulated prefix', () => {
    expect(REGULATED_FEATURES).toHaveLength(4);
    expect(REGULATED_FEATURES.every((f) => f.key.startsWith('regulated.'))).toBe(true);
  });
});
