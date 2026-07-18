import { describe, it, expect } from 'vitest';
import { formatDictation } from '@/lib/dictation/medicalFormat';

describe('formatDictation — spoken punctuation', () => {
  it('turns "full stop" into a period attached to the previous word', () => {
    expect(formatDictation('lungs are clear full stop')).toBe('Lungs are clear.');
  });

  it('turns "comma" into a comma without capitalising the next word', () => {
    expect(formatDictation('no acute findings comma but note this')).toBe(
      'No acute findings, but note this',
    );
  });

  it('handles "new line" and "colon"', () => {
    expect(formatDictation('impression colon new line normal study full stop')).toBe(
      'Impression:\nNormal study.',
    );
  });

  it('handles "new paragraph" and capitalises the next sentence', () => {
    expect(formatDictation('clear full stop new paragraph next section')).toBe(
      'Clear.\n\nNext section',
    );
  });

  it('supports period/question mark/semicolon synonyms', () => {
    expect(formatDictation('is this normal question mark')).toBe('Is this normal?');
    expect(formatDictation('first semicolon second')).toBe('First; second');
  });
});

describe('formatDictation — capitalisation & whitespace', () => {
  it('capitalises the first letter of the utterance', () => {
    expect(formatDictation('measures 2 millimeters')).toBe('Measures 2 millimeters');
  });

  it('collapses runs of spaces but preserves newlines', () => {
    expect(formatDictation('hello   world')).toBe('Hello world');
  });

  it('returns an empty string unchanged', () => {
    expect(formatDictation('')).toBe('');
    expect(formatDictation('   ')).toBe('');
  });

  it('does not insert a space inside a decimal number', () => {
    expect(formatDictation('lesion is 2.5 cm')).toBe('Lesion is 2.5 cm');
  });
});

describe('formatDictation — spoken measurements (F1)', () => {
  it('digitises a spoken decimal measurement and abbreviates the unit', () => {
    expect(formatDictation('the mass measures three point five centimeters full stop')).toBe(
      'The mass measures 3.5 cm.',
    );
  });

  it('joins spoken axes with x when no intermediate unit is given', () => {
    expect(formatDictation('nodule is two by three millimeters full stop')).toBe(
      'Nodule is 2 x 3 mm.',
    );
  });

  it('still passes already-typed digit measurements through unchanged', () => {
    expect(formatDictation('measures 2 millimeters')).toBe('Measures 2 millimeters');
  });

  it('attaches a spoken percent to the preceding number', () => {
    expect(formatDictation('stenosis of fifty percent full stop')).toBe('Stenosis of 50%.');
  });

  it('joins tokens spoken with slash', () => {
    expect(formatDictation('T2 slash FLAIR hyperintensity')).toBe('T2/FLAIR hyperintensity');
  });
});
