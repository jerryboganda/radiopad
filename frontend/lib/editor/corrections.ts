// Client-side shape of a cross-check correction as the section editor needs it
// to render an inline highlight. The backend wire `Correction` type (added in a
// later phase) is a superset of this — the editor only needs the offsets,
// severity (→ highlight colour) and id (→ click target).

export type CorrectionSeverity = 'safety' | 'warning' | 'info';

export interface EditorCorrection {
  id: string;
  /** Inclusive start char offset within the section's plain text. */
  startOffset: number;
  /** Exclusive end char offset within the section's plain text. */
  endOffset: number;
  severity: CorrectionSeverity;
  originalText?: string;
  correctedText?: string;
  reason?: string;
  /** Which engine/model produced the correction (e.g. parakeet, llm, rover). */
  source?: string;
  /** laterality | negation | anatomy | measurement | drug_term | ... */
  category?: string;
}
