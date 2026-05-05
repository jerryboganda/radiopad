# Scope

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## In scope (v0.x → v1.0)

- Structured radiology reporting across the locked UI surfaces (web, desktop, mobile read-only, CLI).
- Rulebook-driven validation (YAML; semver; golden-case tested).
- AI-assisted impression / technique / recommendation suggestions with PHI routing controls.
- Append-only audit log with SHA-256 chain and CLI verification.
- FHIR `DiagnosticReport` text export.
- Per-tenant providers, rulebooks, templates.
- Five seed modalities (chest CT, brain MRI, abdomen US, lumbar spine MRI, knee X-ray).

## Out of scope

- Image viewing / DICOM manipulation. RadioPad is a *reporting* layer, not a PACS.
- Voice dictation (planned, Phase 4).
- Auto-signing or autonomous AI completion of reports.
- Patient-facing portals.
- Billing/CPT coding (we expose the data surface; coding is a downstream system).
- Custom on-device model training.

## Assumptions

- Customers use an existing PACS / RIS for image review and order management.
- Customers have a FHIR-capable endpoint for report exchange or accept text export.
- Customers operate their own AI provider accounts when PHI routing is required.

## Dependencies

- PostgreSQL 14+ in production.
- An OS that supports `.NET 8` and modern Chromium WebView2 / WKWebView for the desktop shell.
- An OIDC-capable identity provider (Phase 3).

## Constraints

- Strict tech stack: Next.js 16 / ASP.NET Core 8 / Tauri 2 / Capacitor 6 / .NET 8 CLI.
- Locked Open Design UI/UX system.
- Append-only audit log.
- Apache 2.0 license for core; commercial add-ons under separate terms.
