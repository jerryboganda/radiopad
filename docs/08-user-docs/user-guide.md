# User Guide (Radiologist)

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

This guide is for radiologists using RadioPad to draft, validate, and sign reports.

## Sign in

- Web: open the RadioPad URL provided by your administrator.
- Desktop: launch the RadioPad app; press `Ctrl+Shift+R` (Windows/Linux) or `⌘⇧R` (macOS) to bring it to the front.
- Mobile: launch the app for read / acknowledge access.

## Dashboard

- Lists your reports with filters: modality, status (Draft / Validated / Acknowledged / Exported), free-text search.
- Pagination: 25 per page; the URL preserves your position so a refresh keeps you in place.

## Drafting

1. Click **New report**.
2. Pick the modality + body part; the matching template loads.
3. Fill in: Indication, Technique, Comparison, Findings, Impression, Recommendations.
4. Save anytime — drafts auto-save.

## Asking AI for a draft

1. Open the report editor.
2. Choose a provider in the AI dropdown (Mock / Anthropic / Ollama / etc., per your admin's configuration).
3. Click **Suggest impression** (or recommendations / technique).
4. Review the suggestion — it appears in the **purple AI mark**.
5. Edit freely, or click **Acknowledge AI suggestions** to accept.
6. **You** are responsible for the final wording.

If you mark the request as containing PHI and the chosen provider isn't permitted to receive PHI, the request is blocked with a clear banner. Either remove the PHI flag (only if it's truly de-identified) or pick a provider with the right compliance class.

## Validation

- Click **Validate**. The right pane shows findings grouped by severity:
  - **Blocker (red)** — must be addressed before sign-off.
  - **Warning (amber)** — review carefully.
  - **Info (blue)** — informational.
- The status moves to **Validated** if there are no Blockers.

## Acknowledge & sign

- Click **Acknowledge** to confirm the report is ready.
- Status moves to **Acknowledged**. The audit log records who, when, and the integrity hash.

## Export

- Click **Export → Text** for a plain-text radiology report.
- Click **Export → FHIR** for a `DiagnosticReport` JSON ready to send to your EHR / RIS.

## Versions

- Every save creates a `ReportVersion`. The **History** tab shows them newest-first; you can view any prior version's text.

## Tips

- Use keyboard navigation: `Tab` between sections; `Esc` closes modals.
- The clipboard is wiped after a short delay on the desktop app — paste promptly.
- If a request fails, note the request id from the banner and share it with support.


## Mobile workflows (iter-36)

The mobile app ships three touch-friendly pages served by the same Next.js
frontend that the Capacitor WebView wraps. They use only the locked Open
Design tokens; AI-drafted prose continues to wear the purple `.ai-mark`
treatment until you accept it.

### Dictate findings — `/mobile/dictate/[reportId]`

1. Open the report on your phone and tap **Dictate**.
2. Tap the large mic tile to start recording. The transcript appears live
   in the serif transcript area below the button.
3. Tap **Save as Findings** to append the transcript to the report's
   Findings section. The page keeps an offline draft in `localStorage`
   keyed by report id, so a network drop or accidental page reload does
   not lose your spoken notes.
4. If your browser does not expose the Web Speech API (older iOS Safari),
   the page shows a clear fallback banner — open the report in the editor
   and type, or retry on Capacitor / Android Chrome.

### Edit a draft — `/mobile/reports/[reportId]/edit`

Each report section (Indication, Technique, Comparison, Findings,
Impression, Recommendations) is its own collapsible panel sized for a
thumb. Tap a section to expand and edit. AI-drafted text stays wrapped in
`.ai-mark` (and tagged with the `AI draft` badge) until you save.

### Acknowledge and export — `/mobile/reports/[reportId]/sign`

RadioPad **never** auto-signs. Sign in your RIS / EHR; this screen records
your acknowledgement and unlocks export. To proceed:

1. Review the read-only report. Validation findings render with the locked
   severity colours: **Blocker → red**, **Warning → amber**, **Info → blue**.
2. Tick *I have reviewed all AI-generated text* (mandatory).
3. If unresolved warnings exist, tick *I acknowledge any unresolved warnings*.
4. Pick a format (Text / JSON / FHIR / PDF) and tap **Acknowledge & Export**.
   You cannot proceed while a Blocker is outstanding.
