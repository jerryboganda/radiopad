/**
 * PRD §14.13 (PR-003) — plain-English labelling for the RADPEER scale.
 *
 * The numeric RADPEER score is what the backend stores and benchmarks on, but
 * "2b" means nothing to a radiologist who does not run the quality programme.
 * Every score and discrepancy category therefore ships with a short label and a
 * one-line explanation, and the UI never shows the bare number on its own.
 *
 * Pure data + pure functions: no React, no fetch — so the review form and its
 * tests share exactly one definition of what each score means.
 */

import type {
  PeerReviewComplexityName,
  PeerReviewDiscrepancyCategoryName,
  PeerReviewItem,
} from './api';

export type PeerReviewScoreOption = {
  /** RADPEER 1..4 as stored by the backend. */
  value: 1 | 2 | 3 | 4;
  label: string;
  help: string;
  /** Maps onto the documented severity colour families (blue/amber/red). */
  tone: 'ok' | 'info' | 'warn' | 'danger';
};

export const PEER_REVIEW_SCORES: readonly PeerReviewScoreOption[] = [
  {
    value: 1,
    label: 'I agree with the original read',
    help: 'Concordant — I would have reported this the same way.',
    tone: 'ok',
  },
  {
    value: 2,
    label: 'Minor difference, unlikely to matter',
    help: 'A discrepancy, but one that would not have changed care.',
    tone: 'info',
  },
  {
    value: 3,
    label: 'Should have been caught most of the time',
    help: 'A discrepancy most radiologists would be expected to spot.',
    tone: 'warn',
  },
  {
    value: 4,
    label: 'Should have been caught almost every time',
    help: 'A discrepancy nearly all radiologists would be expected to spot.',
    tone: 'danger',
  },
];

export type PeerReviewCategoryOption = {
  /** Matches `PeerReviewDiscrepancyCategory` on the backend. */
  value: 1 | 2 | 3 | 4;
  name: Exclude<PeerReviewDiscrepancyCategoryName, 'None'>;
  label: string;
};

export const PEER_REVIEW_CATEGORIES: readonly PeerReviewCategoryOption[] = [
  { value: 1, name: 'Perceptual', label: 'The finding was there but not seen' },
  { value: 2, name: 'Interpretive', label: 'Seen, but described or graded differently' },
  { value: 3, name: 'Communication', label: 'Right finding, but the report did not convey it' },
  { value: 4, name: 'Technique', label: 'Scan technique or protocol limited the read' },
];

export const PEER_REVIEW_COMPLEXITY: ReadonlyArray<{
  value: 0 | 1;
  name: PeerReviewComplexityName;
  label: string;
}> = [
  { value: 0, name: 'Routine', label: 'Routine study' },
  { value: 1, name: 'Complex', label: 'Difficult study' },
];

/** A score of 1 (concur) must carry no category; 2–4 must carry one. */
export function categoryRequired(score: number | null): boolean {
  return score !== null && score >= 2 && score <= 4;
}

/**
 * Mirrors the server-side rationale rule so the form can disable Submit rather
 * than round-trip to a 400. The backend remains the enforcing authority.
 */
export function canSubmit(score: number | null, category: number | null): boolean {
  if (score === null || score < 1 || score > 4) return false;
  return categoryRequired(score) ? category !== null && category > 0 : true;
}

export function scoreOption(score: number): PeerReviewScoreOption | undefined {
  return PEER_REVIEW_SCORES.find((s) => s.value === score);
}

/** Open work: what belongs in the "to review" queue rather than the history. */
export function isOpen(review: PeerReviewItem): boolean {
  return review.status === 'Assigned' || review.status === 'InProgress';
}

/** Formats a 0..1 concordance rate as a whole-percent string, or an em dash. */
export function formatRate(rate: number | null | undefined): string {
  if (rate === null || rate === undefined || Number.isNaN(rate)) return '—';
  return `${Math.round(rate * 100)}%`;
}

/** Short "CT Chest · ACC-123" study line, tolerant of a missing study block. */
export function studyLabel(review: PeerReviewItem): string {
  const s = review.study;
  if (!s) return 'Study details unavailable';
  const parts = [s.modality, s.bodyPart].filter(Boolean).join(' ');
  return s.accessionNumber ? `${parts || 'Study'} · ${s.accessionNumber}` : parts || 'Study';
}
