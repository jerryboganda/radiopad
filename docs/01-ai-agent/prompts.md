# Reusable Prompts

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

Drop-in prompts for common RadioPad coding tasks. Replace `<placeholders>`.

## New feature

```
Goal: <one sentence>
Surface: backend / frontend / cli / desktop / mobile (pick one or more)
Files to inspect: <paths>
Files likely to change: <paths>
Constraints: locked Open Design tokens; tenant isolation via ResolveContextAsync; append-only audit; PHI policy untouched.
Tests: add <path> covering <behaviour>; CI must remain green.
Docs: update <docs/...> and openapi/openapi.yaml if API changes.
Definition of done: <acceptance criteria>
```

## Bug fix

```
Bug: <symptom> in <file>:<line>.
Repro: <steps>.
Hypothesis: <root cause>.
Fix scope: minimal change in <file>.
Test: add a regression test under <path>.
Docs: update CHANGELOG under [Unreleased] / Fixed.
```

## Refactor

```
Goal: <what becomes clearer/safer>
Constraint: behaviour must not change; tests stay green.
Touch: <files>.
Do not touch: AiGateway.cs, ReportValidator.cs, FhirDiagnosticReportSerializer.cs, RadioPadDbContext.cs (require human review).
Docs: ADR if cross-layer.
```

## Test generation

```
Target: <method or endpoint>.
Test project: <RadioPad.Api.Tests | RadioPad.Validation.Tests | …>.
Style: xUnit + plain Assert. Integration tests use WebApplicationFactory<Program>.
Cover: happy path; tenant isolation; PHI policy block; pagination if applicable.
```

## Security review

```
Surface: <files>.
Lens: OWASP Top 10 + RadioPad PHI policy + audit chain integrity.
Output: a findings list with severity, evidence, and fix suggestion.
Do not weaken or bypass any safety control.
```

## Performance review

```
Target: <endpoint or page>.
Goal: meet docs/00-product/nfr.md targets.
Method: identify allocations, N+1 queries, missing indexes; benchmark before/after.
Constraint: behaviour must not change.
```

## Documentation update

```
Trigger: behaviour changed in <file>.
Update: <docs path>; openapi/openapi.yaml if API; CHANGELOG under [Unreleased]; PROGRESS.md iteration entry.
Style: keep the existing structure; bump Last Updated; preserve historical content under "Source Notes Consolidated" if merging.
```

## API design

```
Endpoint: <verb path>
Purpose: <one sentence>
Auth: tenant-scoped (X-RadioPad-Tenant + ResolveContextAsync).
Request schema: <shape>; Response schema: <shape>.
Errors: 400/403/404/409/422; provider-policy block ⇒ 403 with kind: "provider_policy".
Pagination: skip/take + X-Total-Count if listing.
Audit: emit <AuditAction> with <details>.
```

## Database migration

```
Goal: <schema change>
Backward compatible: yes/no (must be yes for in-flight reports).
Migration: dotnet ef migrations add <Name>.
Backfill: <script or one-time job>.
Review: human review required for any migration; do not autosign.
```

## Release preparation

```
Version: <MAJOR.MINOR.PATCH>
Steps:
- Move CHANGELOG [Unreleased] entries under [<version>] - <date>.
- Tag vX.Y.Z; push.
- Run `dotnet build && dotnet test`, `pnpm typecheck && pnpm build`, all golden suites.
- Update docs/00-product/release-scope.md.
- Announce in PROGRESS.md.
```
