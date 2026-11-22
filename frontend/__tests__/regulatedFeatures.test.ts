// Phase 3 — the admin surface must show exactly what the backend gate enforces
// (RegulatedFeatures.IsEnabled: ON unless explicitly false), and toggling one must never disturb
// the others.
import { describe, it, expect } from 'vitest';
import {
  parseFlags,
  isRegulatedEnabled,
  setRegulatedFlag,
  REGULATED_FEATURES,
} from '@/lib/regulatedFeatures';

describe('isRegulatedEnabled', () => {
  it('is on by default (absent / empty / malformed) — same as the backend gate', () => {
    expect(isRegulatedEnabled(undefined, 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('{}', 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('{bad', 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('[]', 'regulated.autoImpression')).toBe(true);
  });

  it('is off only when the flag is explicitly false (bool or "false")', () => {
    expect(isRegulatedEnabled('{"regulated.autoImpression": false}', 'regulated.autoImpression')).toBe(false);
    expect(isRegulatedEnabled('{"regulated.autoImpression": "false"}', 'regulated.autoImpression')).toBe(false);
    expect(isRegulatedEnabled('{"regulated.autoImpression": true}', 'regulated.autoImpression')).toBe(true);
    expect(isRegulatedEnabled('{"regulated.autoImpression": "true"}', 'regulated.autoImpression')).toBe(true);
  });

  it('one flag being off leaves the others on', () => {
    const json = '{"regulated.autoImpression": false}';
    expect(isRegulatedEnabled(json, 'regulated.criticalFindingFlagging')).toBe(true);
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
