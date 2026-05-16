# RadioPad Roadmap

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

> This is the strategic roadmap. The detailed Ralph-loop log lives in [PROGRESS.md](PROGRESS.md). Per-release scope lives in [docs/00-product/release-scope.md](docs/00-product/release-scope.md).

## Phase 0 — Architecture baseline (✅ shipped, v0.1.0)

- ASP.NET Core 8 + Next.js 16 + Tauri 2 + Capacitor 6 + .NET 8 CLI.
- Locked Open Design UI/UX system.
- Append-only audit log with SHA-256 chain.
- AI gateway with PHI policy and Mock/Anthropic/Ollama adapters.
- Five seed rulebooks and matching templates.
- FHIR `DiagnosticReport` text export.

## Phase 1 — Operational hardening (🟡 in progress)

- Server-side pagination and search across all list views.
- Readiness probes and structured liveness metrics.
- Report version history (server snapshot + diff UI).
- CLI provider round-trip smoke test.
- Validation panel polish (severity grouping).

## Phase 2 — Clinical breadth (planned)

- Modality coverage: cardiac MRI, mammography, paediatric chest X-ray.
- Bidirectional FHIR (`DiagnosticReport` import + `ServiceRequest` linkage).
- Structured prior-comparison workflow.
- Multi-radiologist sign-off and addendum workflow.
- Real-time peer review (read-only screen share).

## Phase 3 — Enterprise (planned)

- SSO via OIDC (Okta, Azure AD, Google Workspace).
- Tenant-level audit export (CSV + signed bundle).
- BAA-ready hosted offering on PHI-approved providers.
- Role-based access control matrix exposed in admin UI.
- SOC 2 Type I readiness (see [docs/04-security/compliance-matrix.md](docs/04-security/compliance-matrix.md)).

## Phase 4 — Intelligence (planned)

- RAG over institutional reporting templates and prior cases.
- Configurable AI quality rubric and inline reviewer feedback.
- Fine-tuned local model bundle for on-prem deployments.
- Voice dictation with structured-section routing.

## Risks

- Provider-policy regressions silently leaking PHI — mitigated by mandatory `ProviderBlocked` audit and integration tests.
- Rulebook drift across versions — mitigated by golden cases gating `status: approved`.
- Design-system erosion under deadline pressure — mitigated by the UI/UX lock and review checklist.
- Audit chain corruption — mitigated by `radiopad audit verify` and immutable storage policy.
