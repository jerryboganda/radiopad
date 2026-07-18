// F5 — auto-comparison statement. Given the most-recent prior study (from the /compare-prior
// endpoint), compose the conventional "Compared to …" sentence a radiologist drops into the
// Comparison section. Pure + deterministic.
//
// SAFETY: this states only the study reference + date — it invents no clinical interpretation and
// asserts no interval change. Any actual comparison of findings remains the radiologist's to write.

export interface PriorRef {
  bodyPart?: string;
  createdAt?: string; // ISO 8601
}

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** Deterministic "Month D, YYYY" from an ISO date (UTC components → timezone-stable). */
export function formatPriorDate(iso: string | undefined | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return `${MONTHS[d.getUTCMonth()]} ${d.getUTCDate()}, ${d.getUTCFullYear()}`;
}

/** The comparison sentence, or '' when there is no prior to reference. */
export function buildComparisonStatement(prior: PriorRef | null | undefined): string {
  if (!prior) return '';
  const date = formatPriorDate(prior.createdAt);
  const bodyPart = (prior.bodyPart ?? '').trim();
  const subject = bodyPart ? `${bodyPart.toLowerCase()} study` : 'study';
  return date
    ? `Compared to the prior ${subject} dated ${date}.`
    : `Compared to the prior ${subject}.`;
}
