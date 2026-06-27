import { describe, it, expect } from 'vitest';
import { stringToDoc, docToString, plainOffsetToPmPos } from '@/lib/editor/plainText';

describe('plainText round-trip', () => {
  const cases = ['', 'abc', 'a\nb', 'a\n\nb', '\nx', 'x\n', 'line one\nline two\n\nfinal line'];
  for (const s of cases) {
    it(`round-trips ${JSON.stringify(s)}`, () => {
      expect(docToString(stringToDoc(s))).toBe(s);
    });
  }

  it('produces one paragraph per line', () => {
    expect(stringToDoc('a\n\nb').content).toHaveLength(3);
    expect(stringToDoc('').content).toHaveLength(1);
  });
});

describe('plainOffsetToPmPos', () => {
  it('maps within a single paragraph', () => {
    expect(plainOffsetToPmPos('abc', 0)).toBe(1);
    expect(plainOffsetToPmPos('abc', 1)).toBe(2);
    expect(plainOffsetToPmPos('abc', 3)).toBe(4);
  });

  it('accounts for paragraph boundary tokens across lines', () => {
    // doc = <p>ab</p><p>cd</p>; 'c' starts at PM pos 5, end of 'cd' is 7
    expect(plainOffsetToPmPos('ab\ncd', 0)).toBe(1);
    expect(plainOffsetToPmPos('ab\ncd', 2)).toBe(3);
    expect(plainOffsetToPmPos('ab\ncd', 3)).toBe(5);
    expect(plainOffsetToPmPos('ab\ncd', 5)).toBe(7);
  });

  it('clamps out-of-range offsets', () => {
    expect(plainOffsetToPmPos('abc', 99)).toBe(4);
    expect(plainOffsetToPmPos('abc', -5)).toBe(1);
  });
});
