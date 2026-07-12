import { describe, expect, it } from 'vitest';
import { clinicalLineRole } from '@/lib/editor/clinicalStructure';

describe('clinicalLineRole', () => {
  it('recognizes generated clinical headings', () => {
    expect(clinicalLineRole('CRANIAL VAULT / EXTRA-AXIAL SPACES:')).toBe('heading');
    expect(clinicalLineRole('Ventricles / mass effect')).toBe('body');
  });

  it('recognizes bullet and numbered findings', () => {
    expect(clinicalLineRole('• No acute intracranial hemorrhage.')).toBe('bullet');
    expect(clinicalLineRole('- No midline shift.')).toBe('bullet');
    expect(clinicalLineRole('2. Chronic right frontal encephalomalacia.')).toBe('numbered');
  });
});
