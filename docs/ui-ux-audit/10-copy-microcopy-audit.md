**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Copy and Microcopy Audit

| Issue ID | Route/Component | Text Area | Severity | Problem | Suggested Rewrite |
|---|---|---|---|---|---|
| COPY-01 | `/login` | Sign-in helper | HIGH | Developer/internal details expose headers, reverse proxy, and token storage. | "Sign in to your RadioPad workspace. Your organization manages identity and access." |
| COPY-02 | `/` | Backend error | MEDIUM | Mentions `dotnet run --project`, too technical for end users. | "RadioPad service is unavailable. Try again, or contact your administrator if the problem continues." |
| COPY-03 | `/validation` | Page description | MEDIUM | Describes internal design token mapping instead of user value. | "Review validation findings across non-exported reports. Blockers require action before export." |
| COPY-04 | `/analytics` | Headings/descriptions | MEDIUM | PRD references and "tenant-scoped" read as implementation language. | "Product metrics", "Governance metrics", "Showing data for your workspace." |
| COPY-05 | Report editor | Busy buttons | HIGH | Buttons collapse to "..." during work. | "Generating impression...", "Signing...", "Submitting addendum..." |
| COPY-06 | Validation/status badges | Status terms | MEDIUM | Mixed terms: "attention", "clean", "OK", "No blockers", "Pass/Fail". | Standardize: "Needs attention", "No findings", "Valid", "No blockers". |
| COPY-07 | Providers | Enable/disable button | MEDIUM | Label states current state, not action. | "Disable provider" when enabled, "Enable provider" when disabled; show current state as badge. |
| COPY-08 | Report editor | Voice toggle | LOW | "Voice Cmds" abbreviation is informal/unclear. | "Voice commands" / "Voice commands on". |
| COPY-09 | Tables | Repeated "Open ->" links | MEDIUM | Identical link text is weak in screen-reader link lists and dense tables. | "Open report {accession}", "Open rulebook {name}". |
| COPY-10 | Status mapping functions | Terminology consistency | MEDIUM | Status labels are duplicated and can diverge, e.g. "Review" vs "In review". | Centralize display labels in one frontend glossary/status helper. |
| COPY-11 | Pairing page | Device route mention | MEDIUM | Tells users to open `/devices`, which was not found as a frontend route. | Replace with actual route or "Open the Devices page in your admin workspace." |
| COPY-12 | Desktop status banner | Backend failures | MEDIUM | Failure text can be technical and passive. | "RadioPad service could not start. Restart service or view diagnostics." |

## Copy Principles

- Keep clinical workflow copy user-facing, not implementation-facing.
- Avoid exposing local commands, headers, route internals, or PRD references in production UI.
- Use action labels for buttons, not current state labels.
- Use one glossary for report, validation, rulebook, template, provider, and export statuses.
- Use descriptive link text in repeated table rows.
