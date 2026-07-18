// F5 — the auto-comparison sentence must reference the prior study + date, stay timezone-stable,
// and never fabricate an interval change.
import { describe, it, expect } from 'vitest';
import { buildComparisonStatement, formatPriorDate } from '@/lib/comparisonStatement';

describe('formatPriorDate', () => {
  it('formats an ISO date deterministically in UTC', () => {
    expect(formatPriorDate('2026-03-15T23:30:00Z')).toBe('March 15, 2026');
  });
  it('returns empty for missing / invalid input', () => {
    expect(formatPriorDate(undefined)).toBe('');
    expect(formatPriorDate('not-a-date')).toBe('');
  });
});

describe('buildComparisonStatement', () => {
  it('references the prior body part and date', () => {
    expect(buildComparisonStatement({ bodyPart: 'Chest', createdAt: '2026-01-02T00:00:00Z' })).toBe(
      'Compared to the prior chest study dated January 2, 2026.',
    );
  });

  it('falls back to a generic subject when body part is absent', () => {
    expect(buildComparisonStatement({ createdAt: '2026-01-02T00:00:00Z' })).toBe(
      'Compared to the prior study dated January 2, 2026.',
    );
  });

  it('omits the date clause when there is no valid date', () => {
    expect(buildComparisonStatement({ bodyPart: 'Abdomen' })).toBe(
      'Compared to the prior abdomen study.',
    );
  });

  it('returns empty when there is no prior', () => {
    expect(buildComparisonStatement(null)).toBe('');
    expect(buildComparisonStatement(undefined)).toBe('');
  });

  it('states no interval change (no invented clinical interpretation)', () => {
    const s = buildComparisonStatement({ bodyPart: 'Head', createdAt: '2026-05-01T00:00:00Z' });
    expect(s).not.toMatch(/increase|decrease|stable|unchanged|worse|improv|new|resolv/i);
  });
});
