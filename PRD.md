# RadioPad — Product Requirements Document (PRD)

> **Status:** Active development (May 2026)
> **Mission:** AI-assisted radiology reporting platform delivered as Web + Desktop + Mobile + CLI.
> **Source PRD:** See `RadioPad — Enterprise PRD _ Project Requirement Detail Document.md` for the full enterprise spec. This file is the *engineering* PRD that drives the Ralph loop in `PROGRESS.md`.

---

## 1. Tech Stack (STRICT)

| Layer | Technology |
| --- | --- |
| **Frontend (web)** | Next.js 16 (App Router, TypeScript, React 18) |
| **Backend** | ASP.NET Core 8 Web API (C#), EF Core, SQLite (dev) / PostgreSQL (prod) |
| **Desktop** | Tauri 2 (Rust core + Next.js webview) |
| **Mobile** | Capacitor 6 (iOS + Android wrapping the Next.js web app) |
| **CLI** | `radiopad` — .NET 8 global tool (`dotnet tool`) |
| **AI providers** | Provider-abstraction layer with adapters for: Anthropic (BAA / BYOK), Azure OpenAI (BAA), AWS Bedrock, local models (Ollama / vLLM via HTTP), browser BYOK fallback |
| **Auth** | OIDC (enterprise SSO) + email/password (dev) via ASP.NET Identity |
| **Standards** | FHIR R4 DiagnosticReport, DICOMweb metadata, RadLex/RadElement (where licensed), HL7 v2 ORU |

> **Hard rule:** No other backend frameworks (Node/Express has been retired). The legacy Node daemon (`daemon/*.js`) is archived under `_legacy/` once the .NET equivalent ships.

---

## 2. Repository Layout (target)

```
/
├── frontend/                  # Next.js 16 app (App Router)
│   ├── app/
│   │   ├── layout.tsx
│   │   ├── page.tsx           # Dashboard
│   │   ├── reports/[id]/      # Reporting workspace
│   │   ├── rulebooks/         # Rulebook center
│   │   ├── templates/         # Template library
│   │   ├── governance/        # AI governance dashboard
│   │   └── api/               # Next route handlers (proxy → ASP.NET)
│   ├── components/
│   ├── lib/
│   └── package.json
├── backend/
│   └── RadioPad.Api/          # ASP.NET Core 8 solution
│       ├── RadioPad.Api.sln
│       ├── src/
│       │   ├── RadioPad.Api/          # Web API host
│       │   ├── RadioPad.Domain/       # Entities, value objects
│       │   ├── RadioPad.Application/  # Use cases / services
│       │   ├── RadioPad.Infrastructure/  # EF Core, providers
│       │   └── RadioPad.Validation/   # Rulebook validation engine
│       └── tests/
├── desktop/                   # Tauri 2 app (Rust + bundled web build)
│   └── src-tauri/
├── mobile/                    # Capacitor 6 app
│   └── ios/  android/
├── cli/
│   └── RadioPad.Cli/          # .NET global tool
├── rulebooks/                 # Versioned starter rulebooks (YAML)
├── templates/                 # Starter report templates
├── docs/                      # Full documentation hierarchy
└── PRD.md  PROGRESS.md  README.md  CLAUDE.md  AGENTS.md
```

---

## 3. Core Features (MVP scope, mapped to enterprise PRD)

| # | Feature | Source req | Owner layer |
| --- | --- | --- | --- |
| F1 | Tenant + user auth (RBAC) | AUTH-001..006 | Backend |
| F2 | Report editor with sections | RPT-001..003 | Frontend |
| F3 | AI draft from dictation/findings | RPT-004, AI-001..002 | Backend AI gateway |
| F4 | Findings → Impression generator | RPT-005, AI-003 | Backend |
| F5 | AI text highlighting until reviewed | RPT-008 | Frontend |
| F6 | Rulebook engine (CRUD + versioning + approval) | RB-001..010 | Backend |
| F7 | Validation engine (laterality, contradictions, required sections, negation, modality, hallucination flags) | AI-004..007, §12.1 | Backend |
| F8 | Template library | TMP-001..008 | Backend + Frontend |
| F9 | Provider registry + PHI policy enforcement | PROV-001..010 | Backend |
| F10 | Audit log (immutable) | §13.2 | Backend |
| F11 | FHIR DiagnosticReport + JSON/PDF/DOCX/text export | RPT-011, STD-003 | Backend |
| F12 | CLI: login, daemon, rulebook validate/test, generate, audit export | CLI-001..010 | CLI |
| F13 | Desktop companion (Tauri): hotkeys, secure clipboard, local daemon | DESK-001..010 | Desktop |
| F14 | Mobile (Capacitor) wrapper of web reporting workspace | (extension) | Mobile |
| F15 | AI governance dashboard | §25.6 | Frontend |
| F16 | Billing/usage metering scaffolding | BILL-001..007 | Backend |

Beta and GA features remain in the enterprise PRD; this PRD drives MVP.

---

## 4. Safety boundaries (non-negotiable)

1. RadioPad never auto-signs reports.
2. AI-generated text MUST be visually marked until reviewed.
3. PHI MUST NOT be sent to any provider unless `provider.compliance = phi_approved` for the active tenant.
4. Audit events are append-only (no UPDATE/DELETE on the `AuditEvents` table at the DB level).
5. Default deployment is `127.0.0.1`-bound; remote exposure requires an explicit config flag.
6. Browser BYOK uses `dangerouslyAllowBrowser` only in dev; production routes through the ASP.NET backend.

---

## 5. Acceptance criteria (MVP exit)

- [ ] User can log in (dev: email/password) and create a report.
- [ ] User can paste findings and get an AI-generated impression.
- [ ] AI output is visibly marked and requires explicit acknowledgement before export.
- [ ] At least one approved rulebook (`chest_ct_v1`) is bundled and runs validation.
- [ ] Validation flags laterality conflicts and missing required sections in test fixtures.
- [ ] Report exports to text, JSON, PDF, and FHIR DiagnosticReport JSON.
- [ ] Audit log records: tenant, user, model, prompt version, rulebook version, input/output hash, action.
- [ ] PHI block: attempting to use a `not_phi_approved` provider for a tenant with `phi_required` is rejected with a clear error.
- [ ] CLI: `radiopad daemon status`, `radiopad rulebook validate <file>`, `radiopad rulebook test <file> --cases <dir>` all work end-to-end.
- [ ] Tauri desktop builds and loads the web app; global hotkey scaffold present.
- [ ] Capacitor mobile project builds for at least one platform (Android dev) using shared web bundle.
- [ ] `dotnet test` and `pnpm test` pass; `dotnet build` and `pnpm build` succeed.

---

## 6. Out of scope (MVP)

- Real PACS/DICOMweb integration (mocked).
- Real HL7 v2 listener.
- SCIM, customer-managed keys, on-prem deployment.
- Production-grade billing (Stripe scaffolded only).
- ACR RADS module licensing — placeholder rulebooks only.
- Voice dictation (text input only in MVP; mic capture is a follow-up).

---

## 7. Open questions / decisions parked

- DB engine in dev: **SQLite** (chosen for low-friction local). Prod: PostgreSQL via same EF Core provider abstraction.
- AI default provider in dev: **Anthropic API key (BYOK env var)**. PHI workflows blocked unless tenant policy explicitly opts in.
- Tauri webview content: serves the **statically exported Next.js bundle** (`next build` with `output: 'export'`).
- Mobile: Capacitor wraps the same exported bundle; native plugins (clipboard, biometrics) added incrementally.
