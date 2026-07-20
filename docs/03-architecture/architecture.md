# Architecture overview

RadioPad is a **local-first, audit-first** radiology reporting platform. The
authoritative requirements document is the
[Enterprise PRD](../../RadioPad%20%E2%80%94%20Enterprise%20PRD%20_%20Project%20Requirement%20Detail%20Document.md)
at the repo root; this page is the engineering map onto that document.

## Components

```
┌─────────────────────────────┐    ┌────────────────────────────────────────┐
│ frontend/  (Next.js 16)     │    │ backend/  (ASP.NET Core 8)             │
│  ├ app/                     │    │  ├ RadioPad.Domain   (entities/enums)  │
│  ├ lib/api.ts ──────────────┼────►│  ├ RadioPad.Application              │
│  ├ globals.css  (LOCKED)    │    │  │   ├ AiGateway   (PHI policy)       │
│  └ radiopad.css (LOCKED)    │    │  │   └ ReportingService               │
└──────────────┬──────────────┘    │  ├ RadioPad.Validation (rulebooks)    │
               │                   │  ├ RadioPad.Infrastructure (EF Core)  │
               │ static export     │  └ RadioPad.Api    (controllers)      │
               ▼                   └──────────────┬─────────────────────────┘
┌─────────────────────────────┐                   │
│ desktop/  (Tauri 2 / Rust)  │                   │
│ mobile/   (Capacitor 6)     │                   │
│ cli/      (.NET 8 tool)     │───────────────────┘
└─────────────────────────────┘            HTTP, X-RadioPad-Tenant header
```

- **Frontend** is a single Next.js 16 App Router project, statically exported
  for embedding into Tauri / Capacitor. All UI follows the locked Open Design
  tokens (see [02-design/design.md](../02-design/design.md)).
- **Backend** is one ASP.NET Core 8 process. Hosted at `127.0.0.1:7457` by
  default (override via `RADIOPAD_BIND`). Persistence is SQLite in dev,
  PostgreSQL in prod (toggle via connection string).
- **CLI** is a .NET 8 global tool that links the same Domain/Validation
  assemblies as the API for offline rulebook validation.
- **Desktop** wraps the static frontend; **Mobile** does the same on
  iOS/Android. Both talk to the API over HTTPS.

## Data model (key entities)

- `Tenant` — institution boundary; everything else is keyed by `TenantId`.
- `User` — radiologist / admin / governance / auditor.
- `Report` — owns `StudyContext` (modality, body part, indication, accession),
  carries Findings/Impression/Recommendations narrative, references a
  `Rulebook` and a chosen `ProviderConfig`.
- `ReportVersion` — immutable snapshot per save; the report itself stores the
  current narrative.
- `Rulebook` — versioned YAML; status flow `Draft → InReview → Approved →
  Deprecated`; approved rulebooks must have passing golden-case tests.
- `ProviderConfig` — tenant-level AI provider with `ProviderComplianceClass`
  in {Blocked, Sandbox, DeIdentifiedOnly, PhiApproved, LocalOnly}.
- `AiRequest` — every prompt + response, hashed, linked back to the report.
- `AuditEvent` — append-only, SHA-256 chained via `IntegrityChain`.

## AI gateway

`AiGateway` is the single choke-point for AI calls.

1. The caller supplies the report id, the requested provider id, and the
   prompt string.
2. The gateway computes whether the prompt contains PHI (heuristic — see
   `ReportingService.ContainsPhi`) and looks up the provider's compliance
   class. The PHI flag is recorded, not acted on: it is written to the
   audit event and the `AiRequest` usage row.
3. **Policy decision matrix** — the compliance-class routing gate was
   removed on 2026-07-20 by operator decision, leaving two operator
   switches:
   - Provider `Enabled = false` → `ProviderPolicyException` is thrown;
     nothing is sent downstream.
   - Provider class `Blocked` → same.
   - Anything else → allowed, whether or not the prompt contains PHI, and
     regardless of compliance class. PHI may therefore reach a third-party
     provider with no BAA; the audit trail is what records that it did.
4. Allowed requests hit the provider adapter (Mock / Anthropic / Ollama).
5. Both prompt and response are hashed (SHA-256) and persisted via
   `IAuditLog.AppendAsync` along with the response itself.

## Validation engine

`ReportValidator` evaluates a `Report` against a `RulebookSpec` and returns
`ValidationFinding[]` with severities `Blocker | Warning | Info`. Built-in
checks (regex-driven):

- Required sections present and non-empty.
- Laterality conflicts (left/right contradictions across sections).
- Measurements in Impression that aren't in Findings (mm-normalised).
- Negation conflicts (positive vs negated mentions of the same finding).
- Modality mismatch between study context and narrative.
- Bullet count cap on Impression (per rulebook style).
- Critical-finding language requiring documented communication.
- `avoid_terms` from the rulebook style block.

## Audit chain

Every audit row stores `IntegrityChain = SHA-256(prevHash || canonicalJson)`.
The first row in a tenant chains from the empty string. Tampering with any
prior row breaks the chain on verification.

## Deployment topology

- **Single-tenant clinic:** one process per machine; SQLite; backups via
  filesystem.
- **Multi-tenant hospital network:** one ASP.NET Core service behind reverse
  proxy; PostgreSQL; KMS-backed secret storage for provider API keys.
- **Air-gapped:** Ollama provider only; outbound network egress denied at
  firewall.
