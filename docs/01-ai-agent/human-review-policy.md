# Human Review Policy

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

Work in these areas always requires a human reviewer (not just an AI agent's self-review).

## Always human-reviewed

| Area | Files / surfaces |
| --- | --- |
| AI gateway / PHI policy | `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs` |
| Clinical validation | `backend/RadioPad.Api/src/RadioPad.Validation/Engine/ReportValidator.cs` |
| Interop contract | `backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs` |
| Persistence / migrations | `backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs`, anything under `Migrations/` |
| Audit log integrity | any file referencing `IAuditLog`, `AuditEvents`, or `IntegrityChain` |
| Rulebook approval | `rulebooks/<id>.yaml` flipping `status: approved` |
| Permissions / billing | controllers/middleware that change auth, RBAC, or billing semantics |
| Production config | `appsettings.Production.json`, `deploy/`, anything that ships to a tenant |
| Legal / compliance text | `LICENSE`, `SECURITY.md`, `docs/04-security/privacy.md`, `docs/04-security/data-retention.md`, `docs/04-security/compliance-matrix.md` |
| AI safety behaviour | `docs/05-data-ai/*`, `docs/01-ai-agent/agent-safety.md`, prompts shipped to production |

## Why

Each file or topic above governs clinical safety, data integrity, or legal exposure. A bug here is unrecoverable through normal hot-fix processes — it requires audit explanation, customer disclosure, or both.

## Process

1. Author marks the PR with `human-review-required` label.
2. At least one reviewer from Engineering and (if security/clinical) one from Clinical or Security must approve.
3. Reviewer documents their review evidence in the PR description.
4. The merge button is gated by the label.

## Emergency override

Only the on-call engineer may merge without dual review, and only to mitigate an active SEV-1. The override must be followed by a postmortem within 5 business days using [../07-devops/postmortem-template.md](../07-devops/postmortem-template.md).
