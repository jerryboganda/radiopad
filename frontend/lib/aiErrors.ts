/**
 * Shared user-facing copy for AI-generation-job failures, keyed by the stable
 * machine-readable `errorKind` the backend job engine (and the desktop sidecar)
 * attach to a failed job. Centralised here so the report editor, the top-right
 * jobs widget, and any future surface render the SAME words for the same failure
 * instead of each re-inventing the phrasing.
 *
 * Two kinds are new with the durable async-job platform (2026-07-23):
 *   - `server_restart`  — a hosted job was interrupted by an API restart and
 *     swept as failed (the recovery sweep never re-runs a generation on its own).
 *   - `sidecar_restart` — the desktop's local llama-server process restarted, so
 *     an in-flight on-device generation was lost (the sidecar's job registry is
 *     in-memory by doctrine and does not survive the process).
 * Both are recoverable by re-submitting, hence the "retry to run it again" copy.
 *
 * `errorKind` values the backend already emits: not_found, report_modified,
 * quota_exceeded, provider_policy, provider_transport, rulebook_governance,
 * timeout, server_error. See RadioPad.Domain `AiJob.ErrorKind`.
 */

const AI_ERROR_COPY: Record<string, string> = {
  not_found: 'This report or AI job no longer exists.',
  report_modified:
    'The report changed while the AI was working. Review the report, then run it again.',
  quota_exceeded:
    'Your workspace has reached its AI usage limit for this billing period. Contact your administrator or try again later.',
  provider_policy:
    'The selected AI provider is not available for this request. Choose another provider or check its settings.',
  provider_transport:
    'The AI provider could not be reached. Please try again in a moment.',
  rulebook_governance: 'This action is blocked by a rulebook governance rule.',
  timeout: 'The AI request timed out. Please try again.',
  server_error: 'Something went wrong while running the AI request. Please try again.',
  // New with the durable async-job platform.
  server_restart: 'Interrupted by a server restart — retry to run it again.',
  sidecar_restart: 'The local AI process restarted — retry to run it again.',
  apply_failed:
    'The report could not be updated with the generated draft — retry to run it again.',
  // Client-synthesised: a tracked job vanished from the server before it reached
  // a terminal state (the durable table should make this rare).
  lost: 'This AI job was lost before it finished — retry to run it again.',
};

/** Last-resort copy when we have neither a known `errorKind` nor a server message. */
const GENERIC_AI_ERROR = 'The AI request could not be completed. Please try again.';

/**
 * Map an AI-job `errorKind` to user-facing copy.
 *
 * Known kinds return curated app copy; unknown, null, or absent kinds fall back
 * to `fallback` (typically the raw server `error` message from the job envelope)
 * and, failing that, to a generic message. Callers that already hold a specific
 * server message should prefer it and use this only when that message is absent —
 * see the report editor's `err.body?.error || describeAiError(...)` pattern.
 */
export function describeAiError(
  errorKind: string | null | undefined,
  fallback?: string | null,
): string {
  if (errorKind && Object.prototype.hasOwnProperty.call(AI_ERROR_COPY, errorKind)) {
    return AI_ERROR_COPY[errorKind];
  }
  return fallback?.trim() ? fallback : GENERIC_AI_ERROR;
}
