**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Interaction Flow Audit

| Issue ID | Flow/Page | Interaction | Severity | Friction | Recommended Fix |
|---|---|---|---|---|---|
| UX-001 | Reports dashboard `/` | Create new report | MEDIUM | Uses `location.href`, causing full page navigation instead of App Router transition. | Use `router.push(reportHref(id))`. |
| UX-002 | Report editor | Acknowledge/sign | HIGH | Native `confirm` is visually inconsistent and too weak for clinical risk acknowledgement. | Use locked-token confirmation dialog with explicit clinical copy. |
| UX-003 | Report editor | Export actions | HIGH | Five disabled export buttons occupy primary toolbar space until acknowledgement. | Collapse exports into one menu with disabled reason/help. |
| UX-004 | Validation center | Re-run validation | MEDIUM | Sequential report validation has limited progress/row feedback. | Add progress summary and row-level loading/error/success states. |
| UX-005 | Audit log | Empty/search/filter | MEDIUM | Empty state is plain table row; no search/filter for 200 events. | Use `EmptyState` and add search/filter/expandable details. |
| UX-006 | Analytics | Date range controls | MEDIUM | Active range state may not be styled. | Use `.rp-tabs` or documented active button state. |
| UX-007 | Rulebooks list | Row click plus child links | MEDIUM | Clickable table row with nested links can confuse pointer/keyboard users. | Use explicit row actions only. |
| UX-008 | Rulebook editor | Save/publish/validate | MEDIUM | High-impact actions are visually close and similar. | Separate publish behind confirmation/status gates. |
| UX-009 | Templates | Modal close | MEDIUM | Backdrop close can discard unsaved edits. | Add dirty-close confirmation. |
| UX-010 | Prompt Studio | Add custom block | HIGH | Uses browser `prompt()`, breaking app visual language and accessibility. | Replace with inline form or accessible dialog. |
| UX-011 | Marketplace | Tabs | MEDIUM | Button tabs lack proper tab semantics. | Use ARIA tab pattern with `.rp-tabs`. |
| UX-012 | Providers | Health test | MEDIUM | Inline health feedback may be missed in dense rows. | Use row status badge and timestamp/details. |
| UX-013 | Admin security | JSON details | MEDIUM | Long JSON rendered in dense table cells. | Truncate and expand in drawer/detail view. |
| UX-014 | Login | Sign-in copy | HIGH | Developer/internal copy exposes headers/reverse-proxy details. | Separate user sign-in copy from dev diagnostics. |
| UX-015 | Mobile dictate | Unsupported speech fallback | MEDIUM | Tells user to type elsewhere instead of providing immediate fallback. | Make transcript editable or add direct editor CTA. |
| UX-016 | Mobile sign | Post-export next step | HIGH | Export success does not route user back or guide RIS/EHR next step. | Add return/copy/open exported file actions. |
| UX-017 | Offline drafts | Discard draft | MEDIUM | Discard action has no confirmation. | Confirm discard, especially for unsynced drafts. |
| UX-018 | Pairing | Pair copy route | MEDIUM | Copy references `/devices`, but no route was found. | Add route or update copy to valid location. |

## Flow Coverage

Covered flows: login, report create/open/edit/AI rewrite/validate/acknowledge/export, validation re-run, audit review/verify, analytics range selection, rulebook list/detail/editor, templates CRUD, prompts/golden tests, marketplace tabs/submission, terminology browse, providers CRUD/health/sandbox, offline drafts, mobile dictate/edit/sign, pairing, and admin settings/security/billing/usage/governance/model-eval/PACS/FHIR/Copilot/MCP/SSO pages.
