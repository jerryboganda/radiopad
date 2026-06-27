import { describe, it, expect } from 'vitest';
import { anchorCorrections, applyCorrection } from '@/lib/dictation/anchorCorrections';
import type { CrossCheckCorrection } from '@/lib/api';

function c(partial: Partial<CrossCheckCorrection>): CrossCheckCorrection {
  return {
    id: '1', sectionKey: 'findings', originalText: '', correctedText: '',
    startOffset: 0, endOffset: 0, reason: '', category: '', source: '',
    confidence: 0, severity: 'info', ...partial,
  };
}

describe('anchorCorrections', () => {
  it('anchors by searching originalText in the current section text', () => {
    const res = anchorCorrections('the left lung is clear', [
      c({ id: 'a', originalText: 'left', correctedText: 'right' }),
    ]);
    expect(res).toHaveLength(1);
    expect(res[0].startOffset).toBe(4);
    expect(res[0].endOffset).toBe(8);
  });

  it('drops corrections that cannot be located, and insertions', () => {
    const res = anchorCorrections('the lung', [
      c({ originalText: 'spleen', correctedText: 'x' }),
      c({ originalText: '', correctedText: 'left' }),
    ]);
    expect(res).toHaveLength(0);
  });

  it('matches in document order, non-overlapping', () => {
    const res = anchorCorrections('lung lung', [
      c({ id: 'a', originalText: 'lung', correctedText: 'lobe' }),
      c({ id: 'b', originalText: 'lung', correctedText: 'lobe' }),
    ]);
    expect(res.map((r) => r.startOffset)).toEqual([0, 5]);
  });
});

describe('applyCorrection', () => {
  it('replaces the first occurrence', () => {
    expect(applyCorrection('the left lung', c({ originalText: 'left', correctedText: 'right' })))
      .toBe('the right lung');
  });

  it('returns null when the original is gone', () => {
    expect(applyCorrection('the lung', c({ originalText: 'spleen', correctedText: 'x' }))).toBeNull();
  });
});
