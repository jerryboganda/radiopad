// Dictation-cleanup apply logic (durable async-job platform, 2026-07-23).
//
// The cleanup job is a suggestion set — it NEVER writes the report. The client
// owns the apply/preview gate that decides whether the proposed sections may
// overwrite the editor without a manual confirm. This module holds the pure,
// unit-tested core of that decision (kept out of the heavyweight ReportClient
// component so it can be tested directly, per the reportPage.test.tsx doctrine).
//
// Inline English copy convention matches the sibling modules aiErrors.ts /
// jobs.ts (deliberately not routed through next-intl).
import { canAutoApplyAiResult } from './jobs';

/** The five editable report sections a dictation-cleanup pass may rewrite, in
 *  document order. */
export const CLEANUP_SECTION_KEYS = [
  'indication',
  'technique',
  'findings',
  'impression',
  'recommendations',
] as const;

export type CleanupSectionKey = (typeof CLEANUP_SECTION_KEYS)[number];

/** A partial map of section text — the cleanup job's proposed sections, the
 *  editor's live section values, or a submit-time snapshot. */
export type CleanupSectionMap = Partial<Record<CleanupSectionKey, string>>;

/**
 * The dictation-cleanup result envelope — mirrors the sync
 * `POST /dictation/cleanup` response AND the async cleanup job's ResultJson
 * (`RunCleanupAsync`, PR-B5). A suggestion set only: nothing here is written to
 * the report without passing {@link planCleanupApply}.
 */
export interface CleanupJobResult {
  cleanedSections: CleanupSectionMap;
  provider?: string;
  model?: string;
  latencyMs?: number;
  promptVersion?: string;
}

/**
 * Assemble the raw dictation the cleanup job runs on — each of the five
 * sections' trimmed text, in document order, non-empty only, joined by
 * newlines. Returns `''` when every section is empty; the caller short-circuits
 * with an `empty` overlay and never submits a job. Byte-for-byte the raw the
 * sync `runDictationCleanup` already assembled — the transport is what changed
 * (this raw is sent in the async submit body), not the assembly.
 */
export function assembleDictationRaw(sections: CleanupSectionMap): string {
  return CLEANUP_SECTION_KEYS.map((k) => (sections[k] ?? '').trim())
    .filter(Boolean)
    .join('\n');
}

/** The apply decision for a settled cleanup job: apply every proposed section,
 *  preview the whole set, or nothing was proposed. Never a partial apply. */
export type CleanupApplyPlan =
  | { action: 'no-changes'; keys: [] }
  | { action: 'apply'; keys: CleanupSectionKey[] }
  | { action: 'preview'; keys: CleanupSectionKey[] };

/**
 * All-or-preview staleness gate for a dictation-cleanup suggestion set — the
 * clinical-safety decision behind the cleanup apply flow. It is NEVER a partial
 * apply: either EVERY proposed section auto-applies, or the WHOLE result is
 * routed to the non-destructive preview.
 *
 * - `cleaned` — the job's proposed sections. Only non-empty ones are targets.
 * - `current` — the editor's live section text (the overwrite candidates).
 * - `before`  — per-section text captured at submit. `undefined` for a
 *               deep-linked result opened fresh from the widget (no snapshot),
 *               which makes the per-section gate the empty-only rule via
 *               {@link canAutoApplyAiResult}.
 *
 * Returns `apply` only when EVERY target section still passes
 * `canAutoApplyAiResult` (byte-unchanged since submit, or currently empty when
 * there is no snapshot); `preview` if any target is stale/occupied; `no-changes`
 * when the job proposed nothing.
 */
export function planCleanupApply(
  cleaned: CleanupSectionMap,
  current: CleanupSectionMap,
  before: Record<string, string> | undefined,
): CleanupApplyPlan {
  const keys = CLEANUP_SECTION_KEYS.filter((k) => (cleaned[k] ?? '').trim().length > 0);
  if (keys.length === 0) return { action: 'no-changes', keys: [] };
  const allPass = keys.every((k) => canAutoApplyAiResult(String(current[k] ?? ''), before?.[k]));
  return { action: allPass ? 'apply' : 'preview', keys };
}
