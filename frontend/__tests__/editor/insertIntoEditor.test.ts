import { describe, it, expect } from 'vitest';
import { withSmartSpacing } from '@/lib/dictation/insertIntoEditor';

describe('withSmartSpacing', () => {
  it('adds a space when appending mid-prose', () => {
    expect(withSmartSpacing('the lung', 'is clear')).toBe(' is clear');
  });

  it('does not add a space at the start', () => {
    expect(withSmartSpacing('', 'No acute findings')).toBe('No acute findings');
  });

  it('does not double a space', () => {
    expect(withSmartSpacing('findings ', 'normal')).toBe('normal');
  });

  it('does not space before closing punctuation', () => {
    expect(withSmartSpacing('normal', '.')).toBe('.');
    expect(withSmartSpacing('lobe', ')')).toBe(')');
  });

  it('passes empty text through', () => {
    expect(withSmartSpacing('x', '')).toBe('');
  });
});
