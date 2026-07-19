import { describe, expect, it } from 'vitest';
import { resolveCorrections } from '@/lib/dictation/resolveCorrections';

// Mirrors CorrectionDictionary.Resolve (RadioPad.Application.Dictation). The two implementations
// must agree: the on-device draft path is stateless and cannot resolve corrections server-side, so
// this decides what the radiologist's dictionary actually does when formatting happens on-device.
// A divergence here means a report says something different depending on WHERE it was formatted.

describe('resolveCorrections', () => {
  it('includes org lexicon entries', () => {
    const rules = resolveCorrections([{ term: 'c-spine', replacement: 'cervical spine' }], []);
    expect(rules).toEqual([{ from: 'c-spine', to: 'cervical spine' }]);
  });

  it('includes personal corrections', () => {
    const rules = resolveCorrections([], [{ from: 'mri', to: 'MRI' }]);
    expect(rules).toEqual([{ from: 'mri', to: 'MRI' }]);
  });

  it('lets a personal correction override the org lexicon for the same term', () => {
    const rules = resolveCorrections(
      [{ term: 'CT', replacement: 'computed tomography' }],
      [{ from: 'CT', to: 'CT' }],
    );
    expect(rules).toHaveLength(1);
    expect(rules[0].to).toBe('CT');
  });

  it('matches the override case-INSENSITIVELY, as the backend dictionary does', () => {
    // Backend keys on OrdinalIgnoreCase, so "mri" and "MRI" are the same source term. Treating
    // them as distinct here would apply two conflicting rules to the same word.
    const rules = resolveCorrections(
      [{ term: 'MRI', replacement: 'magnetic resonance imaging' }],
      [{ from: 'mri', to: 'MRI' }],
    );
    expect(rules).toHaveLength(1);
    expect(rules[0].to).toBe('MRI');
  });

  it('orders the longest source phrase first', () => {
    // A long phrase must not be pre-empted by a shorter rule that matches inside it.
    const rules = resolveCorrections(
      [
        { term: 'cor', replacement: 'coronal' },
        { term: 'coronal reformat', replacement: 'coronal reformatted images' },
      ],
      [],
    );
    expect(rules[0].from).toBe('coronal reformat');
  });

  it('drops entries missing either side', () => {
    const rules = resolveCorrections(
      [
        { term: 'good', replacement: 'fine' },
        { term: '', replacement: 'x' },
        { term: 'y', replacement: '   ' },
      ],
      [{ from: '  ', to: 'z' }],
    );
    expect(rules).toEqual([{ from: 'good', to: 'fine' }]);
  });

  it('trims surrounding whitespace on both sides', () => {
    const rules = resolveCorrections([{ term: '  c-spine ', replacement: ' cervical spine  ' }], []);
    expect(rules[0]).toEqual({ from: 'c-spine', to: 'cervical spine' });
  });

  it('handles null/undefined inputs without throwing', () => {
    expect(resolveCorrections(null, undefined)).toEqual([]);
    expect(resolveCorrections(undefined, null)).toEqual([]);
  });
});
