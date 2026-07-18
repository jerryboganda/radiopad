// F7 (dictation brief §6) — the per-user correction dictionary UI must not send junk the
// backend would reject, and must order entries predictably. These guard the pure helpers behind
// the personal-corrections screen.
import { describe, it, expect } from 'vitest';
import { validateCorrection, sortCorrections, type UserCorrection } from '@/lib/userCorrections';

const rows: UserCorrection[] = [
  { id: '1', from: 'apendix', to: 'appendix' },
  { id: '2', from: 'Gaul bladder', to: 'gallbladder' },
];

describe('validateCorrection', () => {
  it('rejects empty or whitespace-only fields', () => {
    expect(validateCorrection('', 'x', rows).ok).toBe(false);
    expect(validateCorrection('x', '   ', rows).ok).toBe(false);
  });

  it('rejects a no-op replacement (identical after trimming)', () => {
    const r = validateCorrection('  liver ', 'liver', rows);
    expect(r.ok).toBe(false);
    expect(r.error).toMatch(/identical/i);
  });

  it('allows a case-only correction (mri → MRI)', () => {
    const r = validateCorrection('mri', 'MRI', rows);
    expect(r.ok).toBe(true);
    expect(r.value).toEqual({ from: 'mri', to: 'MRI' });
  });

  it('trims the values it returns', () => {
    const r = validateCorrection('  hazy  ', '  hilar  ', rows);
    expect(r.ok).toBe(true);
    expect(r.value).toEqual({ from: 'hazy', to: 'hilar' });
  });

  it('warns (but allows) when the exact term already exists — the backend upserts', () => {
    const r = validateCorrection('apendix', 'appendix vermiformis', rows);
    expect(r.ok).toBe(true);
    expect(r.warning).toMatch(/overwrite/i);
  });

  it('treats a different-case term as new (backend matches exactly)', () => {
    const r = validateCorrection('APENDIX', 'appendix', rows);
    expect(r.ok).toBe(true);
    expect(r.warning).toBeUndefined();
  });

  it('does not flag a clash against the row being edited', () => {
    const r = validateCorrection('apendix', 'appendix vermiformis', rows, '1');
    expect(r.ok).toBe(true);
    expect(r.warning).toBeUndefined();
  });
});

describe('sortCorrections', () => {
  it('orders case-insensitively by the spoken form', () => {
    const sorted = sortCorrections([
      { id: 'a', from: 'zeta', to: 'z' },
      { id: 'b', from: 'Alpha', to: 'a' },
      { id: 'c', from: 'beta', to: 'b' },
    ]);
    expect(sorted.map((r) => r.from)).toEqual(['Alpha', 'beta', 'zeta']);
  });

  it('does not mutate the input array', () => {
    const input: UserCorrection[] = [
      { id: 'a', from: 'b', to: 'x' },
      { id: 'b', from: 'a', to: 'y' },
    ];
    sortCorrections(input);
    expect(input[0].from).toBe('b');
  });
});
