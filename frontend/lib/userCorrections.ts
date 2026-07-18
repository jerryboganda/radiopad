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
 * not block — when the exact term already exists, because the backend upserts on it by design.
 * A case-only difference (e.g. `mri` → `MRI`) is a legitimate correction and is allowed.
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
  // The backend matches `From` exactly (case-sensitive unique index), so only an exact clash
  // is an overwrite; a different-case term is a genuinely new entry.
  const clash = existing.find((e) => e.id !== editingId && e.from.trim() === from);
  const value = { from, to };
  if (clash) {
    return { ok: true, warning: `This overwrites your existing correction for “${clash.from}”.`, value };
  }
  return { ok: true, value };
}

/** Stable, case-insensitive ordering by the spoken form — mirrors the backend list order. */
export function sortCorrections(rows: readonly UserCorrection[]): UserCorrection[] {
  return [...rows].sort((a, b) => a.from.localeCompare(b.from, undefined, { sensitivity: 'base' }));
}
