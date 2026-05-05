# IEC 62304 Software Lifecycle Mapping

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

This document maps the IEC 62304:2006/A1:2015 process clauses to the artefacts that already exist in this repository. It is the regulatory contributor's index into the engineering record. **Software safety class** for v0.1 is provisionally **Class A** (no injury or damage to health is possible) given the [intended-use.md](intended-use.md) boundaries; this will be re-assessed if the SaMD trigger conditions in [samd-classification.md](samd-classification.md) §4 are reached.

## 5.1 Software development planning

The Ralph-loop iteration plan in [PROGRESS.md](../../PROGRESS.md) is the project's lightweight development plan. Each iteration block declares scope, owners, and exit criteria. Architectural decisions are captured as ADRs under [docs/03-architecture/adr/](../03-architecture/adr/) and never edited after merge — superseded ADRs replace earlier ones. The [CONTRIBUTING.md](../../CONTRIBUTING.md), [AGENTS.md](../../AGENTS.md), [CLAUDE.md](../../CLAUDE.md), and the strict design lock in [docs/02-design/design.md](../02-design/design.md) act as the configurable process baseline.

## 5.2 Software requirements analysis

The authoritative requirement list is the Enterprise PRD: [RadioPad — Enterprise PRD](../../RadioPad%20%E2%80%94%20Enterprise%20PRD%20_%20Project%20Requirement%20Detail%20Document.md). All 119 functional requirement ids (AUTH-, RPT-, AI-, RB-, TMP-, STD-, DESK-, CLI-, PROV-, MCP-, SEC-, BILL-, PERF-) trace to implementation and tests through [traceability-matrix.md](traceability-matrix.md). Lightweight engineering scope lives in [PRD.md](../../PRD.md).

## 5.3 Software architectural design

The system architecture is documented at three levels of detail:

- C4 context / container / component: [docs/03-architecture/c4-context.md](../03-architecture/c4-context.md), [c4-container.md](../03-architecture/c4-container.md), [c4-component.md](../03-architecture/c4-component.md).
- Backend layered design (Domain → Application → Validation → Infrastructure → Api): [docs/03-architecture/backend-architecture.md](../03-architecture/backend-architecture.md).
- Cross-cutting: [auth-architecture.md](../03-architecture/auth-architecture.md), [multi-tenancy.md](../03-architecture/multi-tenancy.md), [database-design.md](../03-architecture/database-design.md), [error-handling.md](../03-architecture/error-handling.md), [logging.md](../03-architecture/logging.md), [observability.md](../03-architecture/observability.md).

Architectural decisions with regulatory bearing (auth model, FHIR mapping, audit log integrity, PHI gateway) are captured as ADRs in [docs/03-architecture/adr/](../03-architecture/adr/).

## 5.4 Software detailed design

Detailed design of the safety-critical units is in:

- AI gateway PHI policy — `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs` (human-review-gated; see [AGENTS.md §5](../../AGENTS.md)).
- Validation engine — `backend/RadioPad.Api/src/RadioPad.Validation/Engine/ReportValidator.cs`.
- FHIR DiagnosticReport serialisation — `backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs`.
- Append-only audit log — `backend/RadioPad.Api/src/RadioPad.Infrastructure/` (`IAuditLog.AppendAsync`, SHA-256 chain `sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")`).

The strict design lock for the user interface lives at [docs/02-design/design.md](../02-design/design.md) and [frontend/app/globals.css](../../frontend/app/globals.css).

## 5.5 Software unit implementation and verification

Unit-level verification uses xUnit + plain `Assert` (see `.github/instructions/testing.instructions.md`). Test projects live under `backend/RadioPad.Api/tests/RadioPad.Api.Tests/`:

- `ValidationTests.cs` — rulebook engine cases.
- `AiGatewayPolicyTests.cs` — PHI policy + provider compliance gating + `ProviderBlocked` audit assertion.
- `Integration/` — `WebApplicationFactory<Program>` against in-memory SQLite (`tenant slug = it`).

Frontend uses `pnpm typecheck` and component-level tests in `frontend/`.

## 5.6 Software integration and integration testing

Integration tests under `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/` exercise controller-to-database flows with `WebApplicationFactory<Program>`. CI also runs the rulebook golden suites under `rulebooks/_tests/<rulebook_id>/` for every approved rulebook (`chest_ct_v1`, `brain_mri_v1`, `cardiac_mri_v1`, `mammography_v1`, `paediatric_chest_xray_v1`, `liver_mri_v1`, `abdomen_us_v1`, `musculoskeletal_xr_v1`, `spine_mri_v1`).

## 5.7 Software system testing

System-level acceptance is tracked in [PROGRESS.md](../../PROGRESS.md) iteration exit criteria and against the [PRD.md](../../PRD.md) §5 acceptance criteria. CLI behaviour is verified via `dotnet run --project cli/RadioPad.Cli -- rulebook validate ...`. CI must validate every `rulebooks/*.yaml` file and run every matching golden suite under `rulebooks/_tests/*` per `.github/instructions/testing.instructions.md`.

## 5.8 Software release

Release artefacts:

- Backend: `dotnet publish` of `RadioPad.Api`, deployed via `deploy/Dockerfile.api` and `deploy/docker-compose.yml`.
- Frontend: `pnpm build` static export to `frontend/out/`.
- Desktop: `cargo tauri build` producing signed bundles (signing keys live outside the repo).
- Mobile: Capacitor `npx cap copy <platform>` then platform-native packaging.
- CLI: `.NET 8` global tool packaged via `dotnet pack`.

Release notes follow Keep a Changelog in [CHANGELOG.md](../../CHANGELOG.md); semantic versioning per [VERSIONING.md](../../VERSIONING.md).

## 6. Software maintenance process

Maintenance follows the iteration loop in [PROGRESS.md](../../PROGRESS.md). Security patches go to the next minor (`0.x.y`) regardless of feature cadence per `.github/instructions/security.instructions.md`. Dependencies are pinned at minor; SCA reports reviewed weekly.

## 7. Software risk management process

Risk management is documented in [iso-14971-risk-register.md](iso-14971-risk-register.md). The starter register is derived from Enterprise PRD §23. Each risk row links to its existing control (rulebooks, AI gateway, audit log, PHI policy, design lock, golden cases) and to the verification step (test, doc, or process) that demonstrates residual risk acceptability.

## 8. Software configuration management process

Source control: Git (this repository). Branching, commit, and PR conventions are in [CONTRIBUTING.md](../../CONTRIBUTING.md) and [CONVENTIONS.md](../../CONVENTIONS.md). Build configuration is in `package.json`, `pnpm-workspace.yaml`, `backend/RadioPad.Api/Directory.Build.props`, and `cli/RadioPad.Cli/`. Approved rulebook content is versioned by `version:` semver and snapshotted onto each report (RB-001..010). Migrations live under `backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/` and are documented in [docs/03-architecture/migrations.md](../03-architecture/migrations.md).

## 9. Software problem resolution process

Problem reports are tracked as GitHub issues. Security disclosures follow [SECURITY.md](../../SECURITY.md) (private channel; never discussed in public issues until patched). Iteration exit blockers are recorded inline in [PROGRESS.md](../../PROGRESS.md). The `CHANGELOG.md` `[Unreleased]` block lists the corrective actions until a tag is cut.
