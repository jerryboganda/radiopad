// F1 (dictation brief) — spoken-measurement formatting. Must convert spoken number words to
// digits and abbreviate units, in lock-step with the backend §5.2 pass-through so the draft
// endpoint's re-normalisation is a no-op. Pure function; exhaustively covered here.
import { describe, it, expect } from 'vitest';
import { normalizeSpokenNumbers } from '@/lib/dictation/spokenNumbers';

describe('normalizeSpokenNumbers — cardinals', () => {
  it('converts single number words', () => {
    expect(normalizeSpokenNumbers('there are three lesions')).toBe('there are 3 lesions');
  });

  it('converts teens and compound tens', () => {
    expect(normalizeSpokenNumbers('fifteen')).toBe('15');
    expect(normalizeSpokenNumbers('twenty five nodes')).toBe('25 nodes');
  });

  it('handles hundreds with "and" bridging', () => {
    expect(normalizeSpokenNumbers('one hundred and twenty')).toBe('120');
    expect(normalizeSpokenNumbers('two hundred')).toBe('200');
  });
});

describe('normalizeSpokenNumbers — decimals', () => {
  it('reads "point" digits individually', () => {
    expect(normalizeSpokenNumbers('three point five')).toBe('3.5');
    expect(normalizeSpokenNumbers('zero point seven five')).toBe('0.75');
  });

  it('leaves a dangling "point" with no following digits alone', () => {
    expect(normalizeSpokenNumbers('five point of tenderness')).toBe('5 point of tenderness');
  });
});

describe('normalizeSpokenNumbers — units & dimensions', () => {
  it('abbreviates a trailing unit word', () => {
    expect(normalizeSpokenNumbers('five centimeters')).toBe('5 cm');
    expect(normalizeSpokenNumbers('ten millimetres')).toBe('10 mm');
  });

  it('joins axes spoken with "by" using x, keeping one unit', () => {
    expect(normalizeSpokenNumbers('two by three centimeters')).toBe('2 x 3 cm');
    expect(normalizeSpokenNumbers('two by three by four millimeters')).toBe('2 x 3 x 4 mm');
  });

  it('formats a realistic measurement phrase', () => {
    expect(normalizeSpokenNumbers('the mass measures three point five centimetres')).toBe(
      'the mass measures 3.5 cm',
    );
  });
});

describe('normalizeSpokenNumbers — pass-through & idempotency', () => {
  it('leaves text without number words untouched', () => {
    expect(normalizeSpokenNumbers('no acute intracranial abnormality')).toBe(
      'no acute intracranial abnormality',
    );
  });

  it('leaves already-digit measurements untouched (idempotent under the backend pass)', () => {
    expect(normalizeSpokenNumbers('lesion is 2.5 cm')).toBe('lesion is 2.5 cm');
    expect(normalizeSpokenNumbers('measures 2 millimeters')).toBe('measures 2 millimeters');
  });

  it('is stable when applied twice', () => {
    const once = normalizeSpokenNumbers('two by three centimeters and three point two cm');
    expect(normalizeSpokenNumbers(once)).toBe(once);
  });

  it('handles empty input', () => {
    expect(normalizeSpokenNumbers('')).toBe('');
  });

  it('preserves surrounding spacing', () => {
    expect(normalizeSpokenNumbers('measures five cm at the base')).toBe('measures 5 cm at the base');
  });
});
