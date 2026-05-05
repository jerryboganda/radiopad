# Human-in-the-Loop

**Status:** Current  ·  **Owner:** AI + Clinical  ·  **Last Updated:** 2026-05-04

## Principle

Every clinically relevant AI output is reviewed and explicitly acknowledged by a human radiologist before it can be exported.

## Mechanisms

1. **Visual marking.** AI-drafted text is wrapped in `.ai-mark` until the radiologist edits or explicitly acknowledges.
2. **Status gating.** Reports advance Draft → Validated → Acknowledged → Exported. Validation requires a rulebook pass (or accepted findings); acknowledge requires Validated; export requires Acknowledged.
3. **Audit trail.** `AiRequest` / `AiResponse` events bracket every AI call; `ReportAcknowledged` records the human sign-off.
4. **No auto-sign.** There is no API path that simultaneously generates and signs a report.

## When human review is required

- Every AI-drafted impression / recommendation / technique paragraph.
- Every approval / deprecation of a rulebook (`RulebookApproved`).
- Every change to the human-review-required files in [human-review-policy.md](../01-ai-agent/human-review-policy.md).
- Every model swap or provider compliance change.

## How a radiologist acknowledges

- Edits the AI draft (which removes the `.ai-mark` from edited text).
- Or clicks "Acknowledge AI suggestions" — this strips the visual mark and writes an audit event.

## Override

- A radiologist can always reject an AI draft and write the section themselves.
- The AI draft is preserved in the audit log; it does not contaminate the final report unless the radiologist keeps it.

## Failure modes

- Radiologist signs without reviewing the `.ai-mark` text → status transitions still record who signed; the report is theirs.
- AI draft inserted by automation → forbidden; no automated path generates and merges in one step.
- Tenant attempts to disable `.ai-mark` rendering → forbidden; the class is part of the design lock and cannot be hidden.
