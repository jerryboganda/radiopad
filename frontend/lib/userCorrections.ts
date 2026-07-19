// F7 (dictation brief §6) — per-user correction dictionary: client-side helpers for the
// personal-corrections management screen. Pure and framework-free so they can be unit-tested
// directly. The backend (`UserCorrectionsController`) is the source of truth and upserts on the
// exact `from` term; these helpers keep the UI honest before the round-trip.

export interface UserCorrection {
  id: string;
  from: string;
  to: string;
}

export interface CorrectionValidation {
  /** False only when the entry cannot be saved at all. */
  ok: boolean;
  /** Present when ok=false — a human-readable reason the entry is rejected. */
  error?: string;
  /** A non-blocking heads-up (e.g. this will overwrite an existing entry). */
  warning?: string;
  /** Trimmed, ready-to-send values (present when ok=true). */
  value?: { from: string; to: string };
}

/**
 * Validate a single add/edit before it is sent to the backend. Blocks the cases the backend
 * would reject or that are pointless (empty fields, an identical replacement); warns — but does
 * not block — when the term already exists.
 *
 * Two kinds of clash, both warned about, for different reasons:
 *   - EXACT: the backend upserts on it by design, so saving replaces the old entry.
 *   - CASE-ONLY: the backend's unique index is case-sensitive so both rows persist and both are
 *     listed, but `CorrectionDictionary.Resolve` and its frontend port key case-INSENSITIVELY —
 *     only one of them can ever apply. This file previously asserted that a case-only difference
 *     "is a legitimate correction", which is true of the store and false of the resolver: the
 *     losing entry sat in the list looking active and did nothing.
 */
export function validateCorrection(
  fromRaw: string,
  toRaw: string,
  existing: readonly UserCorrection[],
  editingId?: string,
): CorrectionValidation {
  const from = fromRaw.trim();
  const to = toRaw.trim();
  if (from.length === 0 || to.length === 0) {
    return { ok: false, error: 'Enter both the spoken form and its replacement.' };
  }
  if (from === to) {
    return { ok: false, error: 'The replacement is identical to the spoken form.' };
  }
  const value = { from, to };
  const others = existing.filter((e) => e.id !== editingId);

  // Exact clash: the backend upserts, so this genuinely replaces the stored entry.
  const exact = others.find((e) => e.from.trim() === from);
  if (exact) {
    return { ok: true, warning: `This overwrites your existing correction for “${exact.from}”.`, value };
  }

  // Case-only clash: both rows persist, but correction resolution is case-insensitive, so only one
  // of them will ever be applied to dictation.
  const caseOnly = others.find((e) => e.from.trim().toLowerCase() === from.toLowerCase());
  if (caseOnly) {
    return {
      ok: true,
      warning:
        `Corrections are matched without regard to case, so this and your existing “${caseOnly.from}” ` +
        `cannot both apply — only one will take effect. Edit “${caseOnly.from}” instead if you meant to change it.`,
      value,
    };
  }

  return { ok: true, value };
}

/** Stable, case-insensitive ordering by the spoken form — mirrors the backend list order. */
export function sortCorrections(rows: readonly UserCorrection[]): UserCorrection[] {
  return [...rows].sort((a, b) => a.from.localeCompare(b.from, undefined, { sensitivity: 'base' }));
}
