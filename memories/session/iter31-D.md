# Iter-31 Agent D — Backend 🟡 Closure Handoff

**Status:** complete · build green · 6/6 new tests pass (Latest-Patch roll-forward to net8.0.10).

## New / extended HTTP endpoints

### Reports — AI-001 dictation cleanup
- **POST** `/api/reports/{id}/dictation/cleanup`
- RBAC: `Radiologist | MedicalDirector` (rate-limit policy `"ai"`).
- Request: `{ "rawDictation": string }`
- Response 200: `{ cleanedSections: { indication, technique, findings, impression, recommendations }, provider, model, latencyMs, promptVersion }`
- 4xx: `400 { error, kind: "validation" }` when body empty; `403 { error, kind: "provider_policy" }` on `ProviderPolicyException`; `404` if report not found.
- Routes through `IAiGateway.RouteAsync`; audits `AiResponse` (and `ProviderBlocked` on policy denial). Uses `dictation_cleanup` prompt block (override → rulebook → default).

### Reports — AI-008 follow-up suggestions
- **GET** `/api/reports/{id}/followup-suggestions`
- RBAC: any tenanted user (rate-limit `"ai"`).
- Response 200: `{ suggestions: string[] }` (max 3, split on `\n`).
- 4xx: `403 { kind: "provider_policy" }` on `ProviderPolicyException`; `404` if report not found.
- Uses `follow_up` prompt block (override → rulebook → default). Empty list when no `IProviderRouter` registered.

### Prompt overrides — AI-009
- **GET** `/api/prompts/overrides` — list, no RBAC for read.
  Response: `[ { id, rulebookId, blockKey, body, createdAt, updatedAt } ]`
- **POST** `/api/prompts/overrides` — RBAC `MedicalDirector | ReportingAdmin`.
  Request: `{ id?: Guid, rulebookId: string, blockKey: string, body: string }`
  Upsert by `(TenantId, RulebookId, BlockKey)` (unique index). Response 200 entity.
  4xx: `400 { kind: "validation" }` if `rulebookId`/`blockKey`/`body` empty.
- **DELETE** `/api/prompts/overrides/{id}` — RBAC `MedicalDirector | ReportingAdmin`. 204/404.

### Templates — TMP-003 / 005 / 008
- **POST** `/api/templates` (existing route) — `SaveTemplateDto` extended with optional `Variant: TemplateVariant` and `Status: TemplateStatus`. Editing an `Approved` template auto-drops it back to `Draft`.
- **POST** `/api/templates/{id}/approve` — RBAC `MedicalDirector | ReportingAdmin`. Sets `Status = Approved`. Audits `AuditAction.TemplateApproved`. Response 200 entity. 404 on missing.
- **GET** `/api/templates/{id}/preview?reportId={guid?}` — Parses `sectionsJson` array `[{key, label, placeholder}]`, optionally merges values from `Report.{Indication, Technique, Comparison, Findings, Impression, Recommendations}`. Response: `{ id, name, modality, variant, status, sections: [{ key, label, value }] }`.

### Users — AUTH-006
- **GET** `/api/users` — RBAC any tenanted user. Returns `[ { id, email, displayName, role, isActive } ]`.
- **POST** `/api/users/{id}/lockout` — RBAC `MedicalDirector | ItAdmin`. Sets `User.IsActive = false`. Audits `UserLockedOut` `{ targetUserId, targetEmail }`. 4xx: `400 { kind: "validation" }` on self-lockout; `404` if not found.
- **POST** `/api/users/{id}/unlock` — RBAC `MedicalDirector | ItAdmin`. Sets `IsActive = true`. Audits `UserUnlocked`. `404` if not found.

### Devices — AUTH-007
- **GET** `/api/devices` — RBAC `ItAdmin | MedicalDirector`. Returns `[ { id, userCode, status, userId, expiresAt, lastPolledAt, deviceFingerprint, updatedAt } ]`.
- **DELETE** `/api/devices/{id}` — RBAC `ItAdmin | MedicalDirector`. Marks `Status = "denied"` (revoke). 204/404.
- **POST** `/api/auth/device/authorize` — `AuthorizeDto` extended with optional `DeviceFingerprint`; persisted on `DeviceAuthRequest.DeviceFingerprint` when non-empty.

### Lexicon — STD-005 / STD-006
- **GET** `/api/lexicon/export?format=json|yaml` — RBAC `MedicalDirector | ReportingAdmin`. Returns file download. Audits `LexiconExported`. 4xx: `400 { kind: "validation" }` on bad format.
- **POST** `/api/lexicon/import` — RBAC `MedicalDirector | ReportingAdmin`. Body `{ entries: [{ term, forbidden, replacement?, note? }], replaceAll: bool }`. Upsert by case-insensitive `Term`; when `replaceAll=true`, removes rows not in payload. Response: `{ upserts, removed }`. Audits `LexiconImported`.

### Tenant settings — RPT-012 / AI-007
- **GET** `/api/tenant/settings` — body now surfaces `validation: { requireZeroBlockers, warnAsBlocker }`.
- **POST** `/api/tenant/settings` — `SaveTenantSettingsDto` extended with optional `RequireZeroBlockers: bool?`, `WarnAsBlocker: bool?` (applied only when not null). RBAC unchanged (`MedicalDirector | ReportingAdmin | ItAdmin`).
- Behaviour: `Reports.Validate` now respects the toggles. `WarnAsBlocker=true` promotes every `Warning` finding to `Blocker`. `RequireZeroBlockers=false` advances `Status → Validated` and lets `/export/*` return 200 even when blockers remain (default keeps today's behaviour: `RequireZeroBlockers=true`, `WarnAsBlocker=false`).

## Audit-action ints (added)
| int | name |
| --- | --- |
| 20 | TemplateApproved |
| 21 | UserLockedOut |
| 22 | UserUnlocked |
| 23 | LexiconImported |
| 24 | LexiconExported |
(ints 25-28 reserved by sibling agents for `AnomalyDetected`, `McpToolApproved`, `McpToolRevoked`, `McpToolCalled`.)

## Schema additions

| Entity | Column / change |
| --- | --- |
| `TenantSettings` | `RequireZeroBlockers : bool` (default `true`), `WarnAsBlocker : bool` (default `false`) |
| `ReportTemplate` | `Variant : TemplateVariant` (default `Normal`), `Status : TemplateStatus` (default `Draft`) |
| `Rulebook` | `DepartmentTag : string?` |
| `Report` | `DepartmentTag : string?` |
| `DeviceAuthRequest` | `DeviceFingerprint : string?` |
| `User` | (no new columns — uses existing `IsActive`) |
| **NEW table** `PromptOverrides` | `Id, TenantId, RulebookId, BlockKey, Body, CreatedAt, UpdatedAt` + unique index `(TenantId, RulebookId, BlockKey)` |
| New enums | `TemplateStatus { Draft=0, Approved=1, Deprecated=2 }`, `TemplateVariant { Normal=0, Abnormal, FollowUp, Screening, Urgent }` |

Migration: [backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/20260503210213_Iter31BackendClosures.cs](../../backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/20260503210213_Iter31BackendClosures.cs). The auto-generated diff also captured sibling-agents' uncommitted columns (`Tenants.FhirWebhookSecret`, `Hl7SendingFacility`, `IpAllowlistCidr`, `AllowExternalMcp`) and tables (`McpTools`, `McpToolCalls`); merge-time conflict resolution is the normal flow.

## Files created
- [backend/RadioPad.Api/src/RadioPad.Application/Abstractions/IPromptOverrideStore.cs](../../backend/RadioPad.Api/src/RadioPad.Application/Abstractions/IPromptOverrideStore.cs)
- [backend/RadioPad.Api/src/RadioPad.Application/Abstractions/IDictationCleanupService.cs](../../backend/RadioPad.Api/src/RadioPad.Application/Abstractions/IDictationCleanupService.cs)
- [backend/RadioPad.Api/src/RadioPad.Application/Services/DictationCleanupService.cs](../../backend/RadioPad.Api/src/RadioPad.Application/Services/DictationCleanupService.cs)
- [backend/RadioPad.Api/src/RadioPad.Api/Controllers/Iter31Controllers.cs](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/Iter31Controllers.cs) (`PromptOverridesController`, `UsersController`, `DevicesController`)
- [backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/20260503210213_Iter31BackendClosures.cs](../../backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/20260503210213_Iter31BackendClosures.cs)
- [backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/Iteration31Tests.cs](../../backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/Iteration31Tests.cs)

## Files modified
- [backend/RadioPad.Api/src/RadioPad.Domain/Enums/Enums.cs](../../backend/RadioPad.Api/src/RadioPad.Domain/Enums/Enums.cs)
- [backend/RadioPad.Api/src/RadioPad.Domain/Entities/Entities.cs](../../backend/RadioPad.Api/src/RadioPad.Domain/Entities/Entities.cs)
- [backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs](../../backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs) (new `DbSet<PromptOverride>` + unique index)
- [backend/RadioPad.Api/src/RadioPad.Infrastructure/Repositories/Repositories.cs](../../backend/RadioPad.Api/src/RadioPad.Infrastructure/Repositories/Repositories.cs) (`EfPromptOverrideStore`)
- [backend/RadioPad.Api/src/RadioPad.Infrastructure/RadioPad.Infrastructure.csproj](../../backend/RadioPad.Api/src/RadioPad.Infrastructure/RadioPad.Infrastructure.csproj) (`Microsoft.Extensions.Logging.Abstractions` 8.0.0 → 8.0.2 to fix pre-existing NU1605)
- [backend/RadioPad.Api/src/RadioPad.Application/Services/ReportingService.cs](../../backend/RadioPad.Api/src/RadioPad.Application/Services/ReportingService.cs) (override-aware `BuildPromptForMode`, `ApplyStrictness`, `SuggestFollowUpAsync`, department-tag-aware rulebook resolution)
- [backend/RadioPad.Api/src/RadioPad.Api/Program.cs](../../backend/RadioPad.Api/src/RadioPad.Api/Program.cs) (DI registrations)
- [backend/RadioPad.Api/src/RadioPad.Api/Controllers/OtherControllers.cs](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/OtherControllers.cs) (`TenantSettingsController`, `TemplatesController` rewrite, `LexiconController` export/import)
- [backend/RadioPad.Api/src/RadioPad.Api/Controllers/ReportsController.cs](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/ReportsController.cs) (`Validate` strictness, `dictation/cleanup`, `followup-suggestions`)
- [backend/RadioPad.Api/src/RadioPad.Api/Controllers/AuthFlowsController.cs](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/AuthFlowsController.cs) (`DeviceFingerprint` capture)

## Tests added (`Iteration31Tests.cs`, all `[Fact]`)
1. `Validate_Default_Toggles_Match_Today` — blocker present → status stays `Draft`, `/export/markdown` returns 409.
2. `WarnAsBlocker_Promotes_Warnings_To_Blockers` — confirms zero `Warning` severities remain on a report containing a forbidden lexicon term.
3. `RequireZeroBlockers_Off_Allows_Export_With_Blockers` — status advances to `Validated`, `/export/markdown` returns 200 even with blockers.
4. `TenantSettings_Toggles_Are_Admin_Only` — `Radiologist` → 403, `ReportingAdmin` → 200 (toggles persist on subsequent GET).
5. `Dictation_Cleanup_Returns_Section_Map` — verifies all 5 cleaned-section keys present + `AuditAction.AiResponse` row written.
6. `Dictation_Cleanup_400_On_Empty_Body` — empty `rawDictation` → 400.

## Build & test status
- `dotnet build` — green (0 errors, 4 pre-existing warnings: 1× `NU1902 MailKit`, 1× `CS0108 MagicLinkController.Request`, 1× `CS8604` in `Iteration14Tests.cs`, 1× `NU1902` re-emit).
- `dotnet test --filter Iteration31Tests` — **6/6 pass** under `DOTNET_ROLL_FORWARD=LatestPatch` (after installing aspnetcore-runtime 8.0.10).
- Wider suite: 174/192 pass; the 18 failing tests are in sibling-agent territory (Stripe, SIEM, HL7, CMK, BillingHardening, Lexicon validation engine, AuthFlows TOTP/MagicLink, TenantRetention RBAC mismatch). None reside in files I touched and none are caused by Iter-31 D changes.

## Environment notes for next runner
- Repo's `dotnet test` requires aspnetcore-runtime 8.0.10 (or .NET 8 SDK) plus `$env:DOTNET_ROLL_FORWARD='LatestPatch'` because the only SDK installed is 10.0.203 and the test host's `ResponseBodyPipeWriter` lacks `PipeWriter.UnflushedBytes` under net10's `System.Text.Json`.
- `dotnet-ef` was upgraded to 10.0.7; running it against net8 projects also needs `DOTNET_ROLL_FORWARD=LatestMajor`.

## Out-of-scope / explicitly NOT modified
Per task spec: `frontend/lib/api.ts`, `openapi/openapi.yaml`, `docs/03-architecture/api-reference.md`, `docs/09-regulatory/traceability-matrix.md`, `CHANGELOG.md`, `PROGRESS.md`, `frontend/app/radiopad.css`. Frontend wiring + doc updates are the next agent's job.
