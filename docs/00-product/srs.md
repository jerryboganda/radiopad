# Software Requirements Specification (SRS)

**Status:** Draft  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## 1. System overview

RadioPad is a multi-surface clinical reporting system. Backend = ASP.NET Core 8 + EF Core. Web = Next.js 16 App Router (static export). Desktop = Tauri 2 wrapping the web export. Mobile = Capacitor 6 wrapping the same export. CLI = .NET 8 global tool.

## 2. User classes

| Class | Description |
| --- | --- |
| Radiologist | Drafts, validates, signs reports. |
| Resident | Drafts; sign-off requires attending. (Planned.) |
| Admin | Manages providers, rulebooks, templates. |
| Operator | Deploys, monitors, runs `radiopad audit verify`. |

## 3. System features

See [prd.md §6](prd.md). Detailed features in [frd.md](frd.md).

## 4. External interfaces

- **HTTP API** — see [openapi/openapi.yaml](../../openapi/openapi.yaml).
- **CLI** — see [../08-user-docs/cli-guide.md](../08-user-docs/cli-guide.md).
- **AI providers** — Mock (in-process), Anthropic (HTTPS), Ollama (HTTP, local). Pluggable via `IAiProviderAdapter`.
- **FHIR** — `DiagnosticReport` text narrative (one-way export today; bidirectional planned).
- **Audit export** — `radiopad audit export` produces JSON-Lines + signed manifest.

## 5. Data requirements

See [../03-architecture/database-design.md](../03-architecture/database-design.md). Key entities: `Tenant`, `User`, `Report`, `ReportVersion`, `Rulebook`, `ReportTemplate`, `Provider`, `AuditEvent`.

## 6. Security requirements

See [../04-security/security-architecture.md](../04-security/security-architecture.md). Highlights:

- PHI policy enforced in `AiGateway`.
- Audit chain SHA-256.
- Tenant isolation via `ResolveContextAsync`.
- Backend binds 127.0.0.1 by default.

## 7. Performance requirements

| Metric | Target |
| --- | --- |
| Report list (paginated, ≤25 items) p95 | < 200 ms |
| Report PATCH p95 | < 250 ms |
| Validation run p95 | < 400 ms |
| AI gateway round-trip (mock) p95 | < 100 ms |
| AI gateway round-trip (network) p95 | < 4 s |

## 8. Reliability requirements

- Hosted SKU SLO: 99.5% monthly availability.
- Audit chain integrity: 100% verifiable across the dataset.
- Zero PHI leakage to non-compliant providers (binary).

## 9. Constraints

- Strict tech stack (no new frameworks/ORMs).
- Locked UI/UX system.
- Apache 2.0 licensed core.
- HIPAA-compatible architecture; full HIPAA compliance is a customer-deployment exercise.
