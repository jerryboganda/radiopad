// F7 — applying the resolved correction dictionary client-side.
//
// This is a faithful port of `DeterministicPassThrough.ApplyCorrections`, and it exists because the
// PRIMARY dictation path never reached code that applied corrections at all. DictationOverlay
// inserts its transcript straight into the focused editor after `formatDictation` (spoken numbers
// and punctuation only), so a radiologist's configured corrections applied when they used the
// dictation-draft panel and silently did nothing when they used the microphone — the same settings
// screen, opposite behaviour depending on which control they reached for.
//
// The cases below mirror the backend's own tests, because the two implementations must agree: text
// that differs by where it was processed is exactly the invisible inconsistency this product
// cannot afford.
import { describe, it, expect } from 'vitest';
import { applyCorrections, resolveCorrections } from '@/lib/dictation/resolveCorrections';

describe('applyCorrections', () => {
  it('replaces case-insensitively, preserving the canonical replacement form', () => {
    const out = applyCorrections('the mri and the MRI and the Mri', [{ from: 'mri', to: 'MRI' }]);
    expect(out).toBe('the MRI and the MRI and the MRI');
  });

  it('matches a multi-word phrase across any run of whitespace', () => {
    const out = applyCorrections('no acute\n  cardiopulmonary process', [
      { from: 'no acute cardiopulmonary process', to: 'No acute cardiopulmonary abnormality' },
    ]);
    expect(out).toBe('No acute cardiopulmonary abnormality');
  });

  it('respects word boundaries', () => {
    // "PE" must not fire inside "PERFUSION" or "tape".
    const out = applyCorrections('PE on the tape, PERFUSION normal', [
      { from: 'pe', to: 'pulmonary embolism' },
    ]);
    expect(out).toBe('pulmonary embolism on the tape, PERFUSION normal');
  });

  it('treats regex metacharacters in the source as literal text', () => {
    expect(applyCorrections('c.t of the chest', [{ from: 'c.t', to: 'CT' }])).toBe('CT of the chest');
    // The dot must not have matched an arbitrary character.
    expect(applyCorrections('cot of the chest', [{ from: 'c.t', to: 'CT' }])).toBe('cot of the chest');
  });

  it('cannot match a source phrase ending in punctuation — same as the backend', () => {
    // Both implementations wrap the source in \b…\b, and a word boundary cannot anchor after a
    // trailing '.', so a rule like "c.t." never fires. This test documents the limitation instead
    // of hiding it: the frontend deliberately reproduces it rather than quietly correcting more
    // than the backend would, because the two paths disagreeing is the worse failure. If this is
    // ever fixed, fix DeterministicPassThrough.ApplyCorrections in the same change.
    expect(applyCorrections('c.t. of the chest', [{ from: 'c.t.', to: 'CT' }])).toBe('c.t. of the chest');
  });

  it('keeps a literal $ in the replacement', () => {
    expect(applyCorrections('cost', [{ from: 'cost', to: '$5' }])).toBe('$5');
  });

  it('is a no-op for empty text or no rules', () => {
    expect(applyCorrections('', [{ from: 'a', to: 'b' }])).toBe('');
    expect(applyCorrections('unchanged', [])).toBe('unchanged');
    expect(applyCorrections('unchanged', null)).toBe('unchanged');
  });

  it('applies longest-phrase-first so a short rule cannot pre-empt a long one', () => {
    // resolveCorrections orders by source length; applying in that order is what makes the
    // specific phrase win over the general term.
    const rules = resolveCorrections(
      [
        { term: 'lung', replacement: 'pulmonary' },
        { term: 'lung nodule', replacement: 'pulmonary nodule (solid)' },
      ],
      [],
    );
    expect(applyCorrections('a lung nodule and a lung', rules)).toBe(
      'a pulmonary nodule (solid) and a pulmonary',
    );
  });

  it('lets a personal correction beat the org lexicon for the same term', () => {
    const rules = resolveCorrections([{ term: 'MRI', replacement: 'magnetic resonance imaging' }], [
      { from: 'mri', to: 'MRI' },
    ]);
    expect(applyCorrections('the mri report', rules)).toBe('the MRI report');
  });
});
