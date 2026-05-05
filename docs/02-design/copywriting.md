# Copywriting

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## Tone

Calm, clinical, declarative, never cute. Mirror the radiology reporting voice: short sentences, present tense, no hedging.

## Buttons

| Action | Copy |
| --- | --- |
| Create report | "New report" |
| Save changes | "Save" |
| Validate | "Validate" |
| Ask AI | "Ask AI" (with model/provider name in subtitle) |
| Acknowledge / sign | "Acknowledge" (v0.1) |
| Export | "Export FHIR" |
| Cancel | "Cancel" |
| Delete | "Delete" — only when there is an immediately reversible undo. Otherwise "Archive". |

Do **not** use exclamation marks. Avoid "Submit", "Click here", "Done!".

## Error messages

- Lead with the user-actionable fact, not the technical cause.
- Never expose stack traces to the radiologist.
- Always include the request id for support: "Save failed. Request `req_abc123`."

Examples:
- "Provider not allowed for PHI. Choose a PHI-approved provider or remove the PHI flag." (good)
- "ProviderPolicyException at AiGateway+EnforcePhiPolicy …" (bad)

## Empty states

- "No reports yet. Create your first one to get started."
- "No findings. Click *Validate* to run rulebook checks."
- "No providers configured. Add one in *Providers*."

## Onboarding text

(v0.1 has no onboarding wizard; this is the dashboard banner.)

> Welcome to RadioPad. Drafts are private to your tenant; AI suggestions are highlighted in purple until you accept them.

## Notification text

- Use the verb-then-object form: "Saved", "Validated", "Acknowledged", "Exported".
- Errors prefix with the failing verb: "Save failed", "Validate failed".

## AI provenance text

- Tag for `.ai-mark`: "AI suggestion".
- After acceptance: no tag (the text is now authored by the radiologist).
- After dismissal: silently revert.

## Accessibility text

- Every icon-only button has an `aria-label` matching its visual purpose.
- Severity badges include the severity word in the accessible name even when the icon already conveys it.
