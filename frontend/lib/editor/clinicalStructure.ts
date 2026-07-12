export type ClinicalLineRole = 'heading' | 'bullet' | 'numbered' | 'body';

const CLINICAL_HEADING = /^[A-Z][A-Z0-9 /&(),'-]{2,}:?$/;

/** Classify a plain-text report line without changing the stored report. */
export function clinicalLineRole(value: string): ClinicalLineRole {
  const line = value.trim();
  if (CLINICAL_HEADING.test(line)) return 'heading';
  if (/^(?:•|[-–—])\s+\S/.test(line)) return 'bullet';
  if (/^\d+[.)]\s+\S/.test(line)) return 'numbered';
  return 'body';
}
