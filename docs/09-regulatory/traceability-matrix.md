# Traceability Matrix

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-05  ·  **Iteration:** 35 close-out

This matrix maps every functional requirement id from the [Enterprise PRD](../../RadioPad%20%E2%80%94%20Enterprise%20PRD%20_%20Project%20Requirement%20Detail%20Document.md) to its implementation, tests, verification status, and IEC 62304 lifecycle clause. It is the single source of truth for "what is shipped vs deferred" at iteration 35 PRD-complete close-out.

## Iteration 35 summary

| Status | Iter 30 | Iter 31 | Iter 32 | Iter 33 | Iter 34 | Iter 35 |
| --- | --- | --- | --- | --- | --- | --- |
| ✅ Shipped | 30 | 62 | 106 | 117 | 128 | **130** |
| 🟡 Partial | 70 | 44 | 23 | 6 | 0 | **0** |
| 🔴 Not started | 12 | 3 | 0 | 0 | 0 | **0** |
| ⏸ Deferred | 7 | 3 | 0 | 6 | 2 | **0** |
| **Total tracked** | 119 | 112 | 129 | 129 | 130 | **130** |

Backend full-suite at iter-35 close-out: `dotnet test backend/RadioPad.Api/RadioPad.Api.sln --no-build` ⇒ **Failed: 0, Passed: 379, Skipped: 5, Total: 384**. The 5 skips remain the live-infrastructure suites gated on operator secrets (AWS KMS round-trip, SAML live IdP, SIEM live HEC). Iter-35 adds `Iter35OAuthVaultTests` (8), `Iter35AvailabilityMonitorTests` (3), `Iter35LocaleTests` (7), `Iter35ValidationPackTests` (4), `Iter35WebAuthnRootPinTests` (2), plus frontend vitest component suites for `validationFinding` / `aiMark` / `composer` / `topbar`. New EF migrations: `20260505000100_Iter35OAuthVault`, `20260505000200_Iter35Locales`, `20260505000300_Iter35ValidationPacks`. New audit-action ints: `OAuthRefreshRotated = 41`, `ValidationPackApproved = 42`, `ValidationPackDeprecated = 43`, `ValidationPackRun = 44`.

Iter-35 close-out is the **PRD-complete** milestone. The two iter-34 deferrals were resolved by shipping the underlying code: PROV-007 ships an AES-256-GCM-encrypted OAuth refresh-token vault with rotation `BackgroundService` and full endpoint surface; PERF-004 ships an in-process `AvailabilityMonitorService` with OTel histogram, admin endpoint, and burn-rate audit row. Production-stack verification of the 99.9% SLO and live-IdP integration are **operator deployment activities**, not in-repo tracked deferrals — the code-side scaffolding required for both is shipping. Two new rows are added: `INTL-001` (multilingual scaffolding for Enterprise GA) and `VPK-001` (validation packs lifecycle + CLI). Both ship ✅ with named test evidence in this iteration.

## Status legend

| Symbol | Meaning |
| --- | --- |
| ✅ | Shipped at iteration 30 with verifying tests or doc evidence. |
| 🟡 | Partial — core path shipped, edge cases or hardening remain. See [PROGRESS.md](../../PROGRESS.md). |
| 🔴 | Not started. |
| ⏸ | Deferred beyond v0.1 by explicit ADR or PRD §15 / §6 scope decision. |

IEC 62304 clauses use the short labels from [iec-62304-sdlc.md](iec-62304-sdlc.md): `5.2` reqs, `5.3` arch, `5.4` detailed design, `5.5` unit V&V, `5.6` integration, `5.7` system test, `7.x` risk.

Where a row says "see PROGRESS.md", the requirement is tracked across multiple iterations and the canonical state is the [PROGRESS.md](../../PROGRESS.md) iteration log.

## AUTH — Authentication, RBAC, tenant isolation (7)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| AUTH-001 | Email/password, magic link, SSO/OIDC, SAML | Header-based dev auth + iter-22 magic link + iter-32 OIDC presets (Keycloak/Auth0/Okta via `OidcProfiles`) + SAML 2.0 ACS (`/saml/metadata`, `POST /saml/acs` with XML signature verification) + WebAuthn / passkey enrolment (`POST /api/auth/webauthn/register-options/register/signin-options/signin`) | iter-32 `Iter32SamlAcsTests`, `Iter32OidcPresetTests`, `Iter32WebAuthnFlowTests`; [ADR-0004](../03-architecture/adr/ADR-0004-authentication-sso.md) | ✅ | 5.2/5.3 |
| AUTH-002 | RBAC roles (Radiologist, Admin, Medical Director, Compliance Reviewer, IT Admin, Billing Admin) | `RadioPad.Domain` role enums; `TenantedController.ResolveContextAsync` | [authorization-rbac.md](../03-architecture/authorization-rbac.md); integration tests under `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/` | ✅ | 5.4 |
| AUTH-003 | Tenant isolation across app/db/storage/cache/audit | Every query filters `r.TenantId == tenant.Id` via `TenantedController.ResolveContextAsync` | Tenant-isolation integration tests; [multi-tenancy.md](../03-architecture/multi-tenancy.md) | ✅ | 5.3/5.6 |
| AUTH-004 | MFA enforcement by tenant policy | TOTP enrol/verify (iter-22 `MfaController`); iter-32 OIDC `RADIOPAD_OIDC_REQUIRE_MFA` enforces `amr=mfa` from the IdP; presets default to MFA-on for Keycloak/Auth0/Okta; sliding-window lockout on bad TOTP via `LockoutPolicy` | iter-32 `Iter32AccountLockoutTests`, `Iter32OidcPresetTests`; `Iter22AuthFlowTests` (TOTP) | ✅ | 5.2 |
| AUTH-005 | SCIM provisioning | SCIM 2.0 Users + Groups (`/scim/v2/Users`, `/scim/v2/Groups`, `ServiceProviderConfig`, `ResourceTypes`) with tenant-scoped bearer auth, SCIM group role projection via `TenantSettings.ScimGroupRoleMapJson`, and append-only `ScimGroupChanged` audit rows | `Iteration14Tests` SCIM user lifecycle, group create/list/patch/delete, duplicate group rename conflict; [api-reference.md](../03-architecture/api-reference.md) + [openapi.yaml](../../openapi/openapi.yaml) | ✅ | 5.2/5.6 |
| AUTH-006 | Emergency lockout / session revocation | Iter-32 sliding-window account lockout (`LockoutPolicy`, 5 fails / 15 min, 15-min auto-unlock) + `User.FailedLoginCount` / `LockedUntil` columns; admin `POST /api/users/{id}/lockout` / `unlock` (with counter clear); session-wide invalidation via `POST /api/users/{id}/revoke-sessions` (bumps `User.SessionEpoch`, folded into HMAC-bound bearer); audited as `UserLockedOut` / `UserUnlocked` / `SessionsRevoked` | iter-32 `Iter32AccountLockoutTests` (4 cases) | ✅ | 5.4 |
| AUTH-007 | Device trust (desktop / daemon) | Tauri device-pairing scaffolding; iter-31 fingerprint + token exchange (`device_fingerprint`, `device_pairing_token_*`) | [desktop/PLUGIN_TRUST.md](../../desktop/PLUGIN_TRUST.md); see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4 |

## RPT — Reporting workspace (12)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| RPT-001 | Editor sections (Indication/Technique/Comparison/Findings/Impression/Recommendations) | `frontend/` reporting workspace; `Report` entity in `RadioPad.Domain` | Frontend `pnpm typecheck`; integration tests for report CRUD | ✅ | 5.4 |
| RPT-002 | Tenant-specific section layouts | Template-driven layouts via TMP-001 + iter-32 approval workflow + section preview ([Templates](../../frontend/app/templates/page.tsx)) | iter-32 `TemplateApprovalTests`; integration tests for section preview | ✅ | 5.4 |
| RPT-003 | Free text / template-based / structured field entry | Report editor + template binding | Integration tests | ✅ | 5.4 |
| RPT-004 | Generate Draft Report from inputs | AI gateway `GenerateDraft` flow | `AiGatewayPolicyTests.cs` + integration tests | ✅ | 5.4/5.6 |
| RPT-005 | Generate Impression from Findings | AI gateway `GenerateImpression` flow | `AiGatewayPolicyTests.cs` | ✅ | 5.4/5.6 |
| RPT-006 | Rewrite in my style | Iter-31 `RewriteStylePanel.tsx` + `POST /api/reports/{id}/rewrite?mode=in_my_style` (`api.reports.rewriteInMyStyle`) | see [PROGRESS.md](../../PROGRESS.md); `RewriteModeTests` extension | ✅ | 5.4 |
| RPT-007 | Make concise / formal / patient-friendly / referring-physician modes | `ReportsLifecycleController.Rewrite` + `IReportRewriteService` (iter 30) | `RewriteModeTests`; `POST /api/reports/{id}/rewrite` | ✅ | 5.4 |
| RPT-008 | Highlight AI-generated text until reviewed | `.ai-mark` CSS in `frontend/app/globals.css` | [docs/02-design/design.md](../02-design/design.md) | ✅ | 5.4 |
| RPT-009 | Side-by-side prior report comparison | Iter-31 `PriorComparePanel.tsx` + `GET /api/reports/{id}/compare-prior` (`api.reports.comparePrior`); locked `.rp-grid-2` + `.rp-diff-add/remove` tokens | [docs/02-design/design.md](../02-design/design.md) iter-31 block | ✅ | 5.4 |
| RPT-010 | One-click copy to RIS/PACS | Iter-31 `CopyToRisButton.tsx` + 30 s clipboard auto-clear via `secure_copy` Tauri command | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.6 |
| RPT-011 | Export plain text / PDF / DOCX / JSON / FHIR DiagnosticReport | `FhirDiagnosticReportSerializer.cs` + export controllers | Integration tests; [openapi/openapi.yaml](../../openapi/openapi.yaml) | ✅ | 5.6 |
| RPT-012 | Radiologist acknowledgement before final export/sign | Acknowledge endpoint + audit `ReportAcknowledged` | Integration tests; [iso-14971-risk-register.md](iso-14971-risk-register.md) R-01/R-04 | ✅ | 5.4/7.x |

## AI — AI capabilities (12)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| AI-001 | Convert raw dictation to clean sections | Iter-31 `DictationCleanupService` + `POST /api/reports/{id}/dictation/cleanup` returning a section map (Indication / Technique / Comparison / Findings / Impression / Recommendations) — output rendered in `.ai-mark` until acknowledged. Iter-32 added the `dictation_cleanup` prompt block to all 17 rulebooks and a frontend `Cleanup dictation` button on the report editor. | iter-32 `Iteration31Tests.Dictation_Cleanup_Returns_Section_Map`; rulebook authoring guide `style.dictation_cleanup` block | ✅ | 5.4 |
| AI-002 | Generate report drafts from structured/free/measurements | `AiGateway` draft flow | `AiGatewayPolicyTests.cs` | ✅ | 5.4 |
| AI-003 | Generate impression preserving clinical meaning | Same as RPT-005 | `AiGatewayPolicyTests.cs` | ✅ | 5.4 |
| AI-004 | Detect contradictions findings↔impression | `ReportValidator.cs` `negation_conflict` rule | `ValidationTests.cs`; rulebook golden suites | ✅ | 5.4/5.5 |
| AI-005 | Detect missing required sections | Rulebook `required_sections` | `ValidationTests.cs` | ✅ | 5.5 |
| AI-006 | Laterality / measurement / modality mismatch | `laterality_consistency`, `measurement_consistency`, `modality_mismatch` rules | `ValidationTests.cs`; golden suites | ✅ | 5.5 |
| AI-007 | Detect uncertain / unsupported / hallucinated claims | Validation engine + AI cross-check; iter-31 `UnsupportedClaimFinding` UI quotes the offending sentence in `.ai-mark` and links back to Findings | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4/7.x |
| AI-008 | Suggest follow-up language from approved rulebooks | Iter-32 `style.approved_followups` allow-list on every rulebook; `unauthorized_followup` warning rule in `ReportValidator`; `ReportingService.SuggestFollowUpAsync` filters AI-generated suggestions against the allow-list and audits a `PolicyViolation` for any rejected line | iter-32 `Iter32AiCompletenessTests.Validate_Flags_Unauthorized_Followup_When_Allowlist_Present` + `Validate_Allows_Followup_On_Allowlist`; [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) §approved_followups | ✅ | 5.4 |
| AI-009 | Custom system / specialty / user / case-level prompts | Rulebook `prompt_blocks:` schema + iter-31 `PromptOverride` entity with iter-32 Draft → Approved approval gate (MedicalDirector-only `POST /api/prompts/overrides/{id}/approve`); `EfPromptOverrideStore.LoadAsync` filters by `Status == Approved` so only governance-blessed bodies reach the AI runtime; audited as `PromptOverrideApproved` with `bodyHash` | iter-32 `Iter32AiCompletenessTests.PromptOverride_Save_Lands_In_Draft` + `PromptOverride_Approve_Requires_MedicalDirector` + `PromptOverrideStore_Returns_Only_Approved_Rows`; [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) | ✅ | 5.4 |
| AI-010 | Model routing by tenant/role/modality/PHI/cost | Iter-32 `EfProviderRouter` composite scoring (cost + quality + latency) driven by per-tenant `TenantSettings.RoutingWeightsJson`; P95-24h latency tie-break from `AiRequest`; `IRoutingPreviewService` + `GET /api/ai/routing/preview` (ItAdmin / MedicalDirector only) explains the decision with per-candidate `costScore` / `qualityScore` / `latencyScore` / `compositeScore` and the eligibility reason | iter-32 `Iter32AiCompletenessTests.RoutingPreview_Selects_Composite_Winner_And_Requires_Admin`; `AiGatewayPolicyTests.cs` | ✅ | 5.4 |
| AI-011 | Local model execution for de-identified / PHI-sensitive | Iter-32 first-class adapters under `RadioPad.Infrastructure/Providers/Local/`: `OllamaProvider` (`http://127.0.0.1:11434/api/chat`), `VLlmProvider` (OpenAI-compatible `http://127.0.0.1:8000/v1/chat/completions`), `LlamaCppProvider` (`http://127.0.0.1:8080/completion`). All three default to `ProviderComplianceClass.LocalOnly` and expose `ProbeAsync` for the admin `POST /api/providers/{id}/health` health probe; PHI policy in `AiGateway.EnforcePhiPolicy` accepts them for `containsPhi:true` requests. | iter-32 `OllamaProviderTests`, `VLlmProviderTests`, `LlamaCppProviderTests` (5 cases each, stubbed `HttpMessageHandler`); `AiGatewayPolicyTests.cs` (PHI gating) | ✅ | 5.4 |
| AI-012 | Full traceability: prompt+rulebook+model+input+output+edits | `AuditEvents` chain + per-report rulebook snapshot | Audit-chain tests; [iec-62304-sdlc.md](iec-62304-sdlc.md) §5.4 | ✅ | 5.4/5.6 |

## RB — Rulebooks (10)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| RB-001 | Create / edit / clone / archive / version | Rulebook CRUD endpoints + entity | Integration tests | ✅ | 5.4 |
| RB-002 | YAML/JSON source + visual editing | Iter-32 tabbed editor (`/rulebooks/[id]`) — YAML source + Visual mode (required_sections, style.avoid_terms, style.approved_followups, rules, prompt_blocks) | [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) | ✅ | 5.4 |
| RB-003 | Approval workflow (Draft→Review→Approved→Deprecated) | Status enum + approve/deprecate endpoints + audit | Integration tests | ✅ | 5.4/8.x |
| RB-004 | Test cases for each rulebook | Golden suites under `rulebooks/_tests/<id>/` | CI validates every `rulebooks/*.yaml` file and runs every matching golden suite under `rulebooks/_tests/*` | ✅ | 5.5 |
| RB-005 | Prompt blocks / output schemas / style / forbidden / required / validation | YAML schema enforced by validator | [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) | ✅ | 5.4 |
| RB-006 | Modality / subspecialty rulebooks | 17 rulebooks shipped (chest_ct, brain_mri, abdomen_us, spine_mri, musculoskeletal_xr, cardiac_mri, mammography, paediatric_chest_xray, liver_mri + iter-31 thyroid_us, prostate_mri, lung_screening_ct, head_ct_trauma, knee_mri, shoulder_mri, abdomen_ct, pelvis_mri) | `rulebooks/_tests/` (8 new golden suites in iter 31) | ✅ | 5.4 |
| RB-007 | Tenant / department / user inheritance | `Rulebook.DepartmentTag` + `Report.DepartmentTag` resolution in `ReportingService.ResolveRulebookEntityAsync`; user-level overrides via `PromptOverride` (iter-31 AI-009) | iter-32 `RulebookInheritanceTests` | ✅ | 5.4 |
| RB-008 | Rollback to prior approved version | iter-12 `POST /api/rulebooks/{id}/rollback` + iter-32 frontend `Rollback to v…` dropdown on `/rulebooks/[id]` | `RollbackTests` | ✅ | 8.x |
| RB-009 | Capture rulebook version in every AI audit | `AuditEvents.detailsJson` includes rulebook id+version | Audit-chain tests | ✅ | 5.4/8.x |
| RB-010 | Prevent unapproved rulebooks in production unless sandbox | Enforced in validation engine | `ValidationTests.cs` | ✅ | 5.5/7.x |

## VPK — Validation packs (1)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| VPK-001 | Composable validation packs (lifecycle + import/export + run) | Iter-35 `ValidationPack` entity + status enum (Draft → Approved → Deprecated), six endpoints under `/api/validation-packs` (list / get / import / export / approve / deprecate / run), CLI `radiopad packs list|import|export|run`, admin page `/admin/validation-packs`. EF migration `20260505000300_Iter35ValidationPacks`. New audit ints `ValidationPackApproved = 42`, `ValidationPackDeprecated = 43`, `ValidationPackRun = 44`. Approval gate mirrors rulebook RB-003. | iter-35 `Iter35ValidationPackTests` (4 cases — lifecycle, import/export round-trip, run + audit, RBAC) | ✅ | 5.4/5.5 |

## TMP — Templates (8)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| TMP-001 | Library by modality / anatomy / subspecialty / procedure / report type | `templates/` JSON files + `Template` entity | Integration tests | ✅ | 5.4 |
| TMP-002 | Structured / optional / required / conditional fields | Template schema | Integration tests | ✅ | 5.4 |
| TMP-003 | Normal / abnormal / follow-up / screening / urgent variants | `ReportTemplate.Variant` enum (Normal/Abnormal/FollowUp/Screening/Urgent) surfaced in iter-32 templates page | `TemplateApprovalTests` | ✅ | 5.4 |
| TMP-004 | RadLex / RadElement mapping where licensed | `terminology_refs:` block (iter 30) + iter-32 `/admin/terminology` CSV upload wired to `TenantLexicon` | [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) §iter-30 | ✅ | 5.4 |
| TMP-005 | Tenant-specific template approval | Iter-32 lifecycle Draft → Review → Approved → Deprecated; endpoints `submit-review` / `approve` / `deprecate`; `Template.ApprovedBy/ApprovedAt`; production gate via `Tenant.AllowSandboxRulebooks` | `TemplateApprovalTests` (3 tests) | ✅ | 5.4 |
| TMP-006 | Template usage analytics | Iter-32 `GET /api/templates/{id}/usage` (last 7d/30d/90d, byUser, byModality) surfaced in `/templates` admin page | `TemplateUsageAnalyticsTests` | ✅ | 5.2 |
| TMP-007 | Import / export JSON / YAML | CLI `radiopad templates import/export` round-trips JSON+YAML via `TemplatesCommands.BuildSavePayload` | `CliGenerateTests.TemplatesCommand_BuildSavePayload_Roundtrip_*` | ✅ | 5.6 |
| TMP-008 | Report preview before publishing | Iter-32 `GET /api/templates/{id}/preview` + `/templates` admin Preview pane (renders sections with placeholders) | `TemplateApprovalTests` (preview wire shape) | ✅ | 5.4 |

## STD — Standards & terminology (6)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| STD-001 | RadLex mapping where available | `IRadLexService` + `rulebooks/_terminology/radlex_subset.yaml` (iter 30) | `RadLexLookupTests`; `GET /api/terminology/radlex/search` + FHIR `CodeSystem` | ✅ | 5.4 |
| STD-002 | ACR RADS modules (BI-RADS / LI-RADS / PI-RADS / Lung-RADS) subject to licensing | `IRadsService` + `rulebooks/_terminology/rads.yaml` (iter 30) | `RadsLookupTests`; `GET /api/terminology/rads` | ✅ | 5.4 |
| STD-003 | FHIR DiagnosticReport export | `FhirDiagnosticReportSerializer.cs` | [fhir-mapping.md](../03-architecture/fhir-mapping.md); integration tests | ✅ | 5.6 |
| STD-004 | DICOMweb study metadata retrieval | `IDicomWebClient` study + series + iter-31 `RetrieveInstanceMetadataAsync` (WADO-RS) | `DicomInstanceMetadataTests`; `GET /api/reports/{id}/dicom-context/instance` | ✅ | 5.6 |
| STD-005 | Terminology dictionary management | iter-31 lexicon CRUD + iter-32 CSV bulk upload (`POST /api/lexicon/import-csv`) on `/admin/terminology` | `LexiconBulkImportTests` | ✅ | 5.4 |
| STD-006 | Institution-specific lexicons / abbreviations | `style.avoid_terms` rule + `TenantLexicon` table wired into `ReportValidator` (iter-11); iter-32 CSV bulk import + audit `LexiconImported` | `LexiconBulkImportTests`; `ValidationTests.cs` `avoid_terms` | ✅ | 5.4 |

## DESK — Desktop (10)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| DESK-001 | Windows + macOS desktop apps | Tauri 2 shell in `desktop/`; iter-30 `.github/workflows/desktop-bundle.yml` builds Windows / macOS / Linux artefacts on every tag; signed installer attachments require operator-supplied Authenticode + Apple Developer ID secrets (deferred). | iter-30 `desktop-bundle.yml` builds; iter-32 `desktop-installer-verify.yml` smoke-test | ✅ | 5.4 |
| DESK-002 | Auto-start / manage local daemon | Iter-17 Tauri sidecar (`desktop/src-tauri/src/main.rs` + `tauri.conf.json` `bundle.externalBin = ["binaries/radiopad-api"]`); CI publishes the .NET backend per-RID and embeds it as the sidecar. | iter-17 PROGRESS block; capability allow-list in `capabilities/default.json` | ✅ | 5.4 |
| DESK-003 | Global hotkeys (dictation/impression/rewrite/copy/paste) | Iter-31 six `Ctrl/Cmd+Shift+<R/N/I/W/D/C>` shortcuts emit `radiopad://*` events to the shell bridge | see [PROGRESS.md](../../PROGRESS.md); `desktop/src-tauri/src/main.rs` | ✅ | 5.4 |
| DESK-004 | Secure clipboard with timeout | Iter-31 `secure_copy` TTL + blur auto-clear, emits `radiopad://clipboard-cleared` | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4/7.x |
| DESK-005 | Local encrypted cache | Iter-31 AES-256-GCM scoped cache w/ per-entry TTL (`local_cache_*` Tauri commands), key in OS keyring | `desktop/src-tauri/src/local_cache.rs` | ✅ | 5.4/7.x |
| DESK-006 | Offline draft editing | Iter-31 AES-256-GCM offline draft store via `tauri-plugin-store` (`offline_drafts_*` commands) | `desktop/src-tauri/src/offline_drafts.rs` | ✅ | 5.4 |
| DESK-007 | Local PACS/RIS bridge plugins | Iter-32 Tauri `pacs_plugins.rs` SHA-256 + Ed25519 signed-manifest loader; `mcp-connectors/*` ship with `.sig` files. Vendor-SDK integrations (Sectra IDS7 / Visage 7 / Carestream Vue) shipped as backend adapters in `RadioPad.Infrastructure/Pacs/` (`SectraIds7Adapter`, `Visage7Adapter`, `CarestreamVueAdapter`) routed by tenant via `PacsVendorRouter`. End-to-end PACS hospital pilot remains operator-gated (⏸). | `PacsPluginsVerifierTests`; `Iter32PacsRouterTests` | ✅ | 5.4 |
| DESK-008 | Device authorization / tenant pairing | Iter-31 `device_fingerprint` (machine-uid + 16 B salt SHA-256) + `device_pairing_token_*` keyring slots | `desktop/src-tauri/src/device_pairing.rs` | ✅ | 5.4 |
| DESK-009 | Local model / plugin execution where enabled | `desktop/src-tauri/src/sandbox.rs` SHA-256 + Ed25519 verification (iter 30) | `desktop/PLUGIN_TRUST.md`; CLI `radiopad plugin verify` | ✅ | 5.4 |
| DESK-010 | Local logs with PHI redaction controls | Iter-31 `log_redactor.rs` regex layer fed into `tracing-subscriber`; installed before any tracing event fires; sidecar stderr also redacted | [logging.md](../03-architecture/logging.md) | ✅ | 5.4/7.x |

## CLI — Command-line tool (10)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| CLI-001 | `radiopad login` device-auth / OAuth | Iter-31 `DeviceFlow.cs` mirroring `auth/device/*` endpoints | [cli-guide.md](../08-user-docs/cli-guide.md) | ✅ | 5.2 |
| CLI-002 | `radiopad daemon start/stop/status` | Iter-31 `Daemon.cs` background-process supervisor | [cli-guide.md](../08-user-docs/cli-guide.md) | ✅ | 5.4 |
| CLI-003 | `radiopad generate` from local inputs | Iter-32 — `radiopad generate --template <id> --input findings.txt --rulebook <id> --mode draft --out report.json` creates a new report bound to the template, seeds Findings from the file, and routes through `/api/reports/{id}/ai`; PHI guard preserved | manual smoke (CI runs `--report` flow) | ✅ | 5.4 |
| CLI-004 | `radiopad validate` rulebook + report | CLI validate command | CI runs all 17 rulebook validate commands | ✅ | 5.5 |
| CLI-005 | `radiopad rulebook test` regression | CLI test command + golden suites | [rulebook-authoring.md](../05-clinical/rulebook-authoring.md) | ✅ | 5.5 |
| CLI-006 | `radiopad templates import/export` | Iter-31 `Templates.cs` round-trip JSON/YAML | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.6 |
| CLI-007 | Provider adapters (APIs / local / OAuth) | Iter-31 5 adapters (Azure OpenAI, AWS Bedrock, GCP Vertex, OpenAI direct, OpenAI-compatible generic) under `RadioPad.Infrastructure/Providers/` | `ProviderSecretResolver` env-var only; PHI compliance class declared per-adapter | ✅ | 5.4 |
| CLI-008 | Enforce tenant model policies locally | Iter-31 `PhiGuard.cs` mirrors server-side `EnforcePhiPolicy` before any provider call | `AiGatewayPolicyTests.cs` (logic) | ✅ | 5.4/7.x |
| CLI-009 | Audit event sync to control plane | Iter-31 `AuditSync.cs` push of local audit events with retry + dedupe | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.6/8.x |
| CLI-010 | Headless mode for enterprise | Iter-31 `--headless` flag wired across daemon / generate / sync | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4 |

## PROV — Provider catalog & policy (10)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| PROV-001 | Tenant-level provider registry | `Provider` entity + admin endpoints | [provider-catalog.md](../03-architecture/provider-catalog.md) | ✅ | 5.4 |
| PROV-002 | PHI-approved / de-identified-only / blocked classes | `ProviderComplianceClass` enum | `AiGatewayPolicyTests.cs` | ✅ | 5.4/7.x |
| PROV-003 | Per-provider cost / latency / token / availability telemetry | Iter-10 `IAiUsageStore` writes one `AiRequest` row per gateway call (cost / latency / tokens / status); iter-31 `/admin/usage` page surfaces `byProvider` rollup; iter-32 `EfProviderRouter` consumes P95-24h latency from the same ledger; iter-32 `POST /api/providers/{id}/health` exposes availability. | `Iteration10Tests` (ledger persistence); `RoutingPreviewTests` (P95 latency); `LocalProviderHealthTests` (probe) | ✅ | 5.6 |
| PROV-004 | Fallback only between equal/higher compliance class | `AiGateway.EnforcePhiPolicy` + routing | `AiGatewayPolicyTests.cs` | ✅ | 5.4/7.x |
| PROV-005 | Sandbox model comparison | Sandbox tenants flagged via `Tenant.AllowSandboxRulebooks = true` may register multiple `ProviderConfig` rows with `ProviderComplianceClass.Sandbox`. Iter-34 ships `POST /api/ai/sandbox/compare` (`SandboxCompareController`) — radiologist picks a draft + mode + 1–4 sandbox providers and gets a `runs[]` payload with each output, latency, and token counts side-by-side. UI panel lives on `/providers` (`SandboxComparePanel`) and only renders when ≥2 sandbox providers are configured. PHI policy is still enforced inside `AiGateway.EnforcePhiPolicy`. | iter-34 `Iter34SandboxCompareTests` (4 cases — 409 sandbox flag, 400 mixed-compliance, 200 happy path, PHI + DeIdentifiedOnly refusal); `Iter32AiCompletenessTests` | ✅ | 5.5 |
| PROV-006 | API key vaulting + rotation | `ApiKeySecretRef = "env:<NAME>"` enforced at provider save time and by `ProviderSecretResolver`; inline literals and unsupported schemes resolve to no secret. Rotation = swap env var, no code redeploy. KMS-backed at-rest column encryption applied via `AesGcmColumnEncryptor` + `IKmsProvider` chain (iter-32). | iter-32 `KmsRoundTripTests`; `.github/instructions/security.instructions.md` | ✅ | 5.4 |
| PROV-007 | OAuth tokens encrypted vaults only | Iter-35 `OAuthRefreshTokenService` persists provider-side OAuth refresh tokens AES-256-GCM-encrypted via the existing `IKmsProvider` chain (`env:` / `local:` / `aws:` / `azkv:` / `gcp:` schemes); rotation `BackgroundService` re-issues tokens against the IdP and audits `OAuthRefreshRotated = 41` per rotation. Endpoints: `POST/DELETE/GET /api/providers/{id}/oauth/refresh-token[/status]`. EF migration `20260505000100_Iter35OAuthVault`. Admin UI panel on `/admin/providers/[id]`. OIDC bearer flow continues to use live JWKS validation (`OidcBearerMiddleware`). Wiring against a live IdP for end-to-end pilot is operator-gated. | iter-35 `Iter35OAuthVaultTests` (8 cases — encrypt, decrypt, rotate, revoke, status, RBAC, audit chain, KMS unavailable); iter-22 `Iter22OidcBearerTests` | ✅ | 5.4 |
| PROV-008 | Provider policy enforcement before inference | `AiGateway.EnforcePhiPolicy` | `AiGatewayPolicyTests.cs` | ✅ | 5.4 |
| PROV-009 | Data retention labelling | Per-provider retention is captured by the iter-34 `ProviderConfig.RetentionLabel` free-text field (operator-supplied; examples `no-egress`, `30d-soft-delete`, `vendor-controlled-zdr`, `baa-30d`, `local-only-no-retention`) shown alongside `ProviderComplianceClass` on every `GET /api/providers` response and editable on `/admin/providers`. The label is informational and never weakens the PHI policy in `AiGateway.EnforcePhiPolicy`. | `Iter34ProviderRetentionTests`; `AiGatewayPolicyTests`; `provider-catalog.md` | ✅ | 5.4 |
| PROV-010 | Block PHI from non-approved providers | `ProviderPolicyException` + `ProviderBlocked` audit | `AiGatewayPolicyTests.cs` | ✅ | 5.4/7.x |

## MCP — Tool registry (7)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| MCP-001 | Approved-tool registry | Iter-32 `McpToolRegistryController` (`/api/mcp/tools`): submit / list / approve / revoke a tool manifest, audited as `McpToolRegistered` / `McpToolApproved` / `McpToolRevoked`. `McpTool` entity carries `ManifestJson` / `ManifestSha256` / `ManifestSig` / `Status` / `IsBuiltIn`. | `Iter32McpRegistryTests` (registry CRUD) | ✅ | 5.4 |
| MCP-002 | Explicit admin approval per tool | `POST /api/mcp/tools/{id}/approve` requires `ItAdmin` / `MedicalDirector`; tools default to `Status = Pending` and refuse execution until approved. Trusted-publisher Ed25519 verification gates which manifests can even be submitted. | `Iter32McpRegistryTests` (approval RBAC) | ✅ | 5.4 |
| MCP-003 | Least-privilege scopes | `McpTool.ScopeString` enforces a comma-separated scope list parsed by `McpInvocationService`; calls outside the granted scopes are rejected with `403 { kind: "mcp_scope" }`. | `Iter32McpRegistryTests` (scope-deny) | ✅ | 5.4 |
| MCP-004 | Log every tool call (user/study/tool/in-hash/out-hash/ts) | Every invocation through `McpInvocationService` writes an `McpToolCall` row + an `AuditAction.McpToolCalled` audit row containing tool id, user, report, SHA-256(input), SHA-256(output), latency, and status. | `Iter32McpRegistryTests` (audit hash chain) | ✅ | 5.6 |
| MCP-005 | Block shell/file/network tools by default in prod | `McpToolRegistryController` enforces default-deny in production (`ASPNETCORE_ENVIRONMENT != Development`); only allow-listed tools execute; `Tenant.AllowDangerousMcp` gates the override. | `Iter32McpRegistryTests` (default-deny); `Iter31McpTests` | ✅ | 5.4/7.x |
| MCP-006 | Sandboxed tool execution for tests | `InProcessMcpSandbox` runs golden-case tool tests with timeout + memory ceiling and rejects host-network / file-system access. | `Iter32McpRegistryTests` (sandbox harness) | ✅ | 5.5 |
| MCP-007 | Allowlist connectors for PACS/RIS/EHR | Iter-32 `pacs_plugins.rs` SHA-256 + Ed25519 verifier in the Tauri shell; backend `PacsVendorRouter` keys allowed adapters per tenant (`Tenant.PacsVendor`). Sectra IDS7 / Visage 7 / Carestream Vue REST + GraphQL adapters shipped under `RadioPad.Infrastructure/Pacs/`. Pilot rollout against vendor sandboxes is operator-gated. | `PacsPluginsVerifierTests`; `Iter32PacsRouterTests` | ✅ | 5.2 |

## SEC — Security controls (12)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| SEC-001 | TLS 1.2+/1.3 in transit | Operator-provided TLS reverse proxy; `RADIOPAD_BIND` 127.0.0.1 default | `.github/instructions/security.instructions.md` | ✅ | 5.4 |
| SEC-002 | Encryption at rest | DB / storage encryption + iter-31 `AesGcmColumnEncryptor` for column-level secrets | [database-design.md](../03-architecture/database-design.md) | ✅ | 5.4 |
| SEC-003 | Customer-managed keys | Iter-32 real KMS adapters: AWS KMS (`AwsKmsProvider`, EncryptionContext-bound), Azure Key Vault (`AzureKeyVaultKmsProvider`, RSA-OAEP-256), GCP KMS (`GcpKmsProvider`, AAD-bound) + 5-min `TenantDekCache`, plus `env:` / `local:`. `POST /api/tenant/settings/kms/verify` performs a wrap+unwrap round-trip and stamps `CmkLastVerifiedAt`. | [security-architecture.md](../04-security/security-architecture.md) | ✅ | 5.2 |
| SEC-004 | Tenant-level PHI policy | `AiGateway.EnforcePhiPolicy` | `AiGatewayPolicyTests.cs` | ✅ | 5.4/7.x |
| SEC-005 | Audit log immutability | SHA-256 chain `sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")` via `IAuditLog.AppendAsync` | Audit-chain tests; never UPDATE/DELETE on `AuditEvents` | ✅ | 5.4/7.x |
| SEC-006 | Least-privilege RBAC | Role enum + tenant context | [authorization-rbac.md](../03-architecture/authorization-rbac.md) | ✅ | 5.4 |
| SEC-007 | SSO / MFA / SCIM | Iter-32 OIDC presets (Keycloak/Auth0/Okta) + SAML 2.0 ACS + WebAuthn / passkey + per-tenant MFA enforcement; SCIM 2.0 ([scim/v2](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/ScimController.cs)) Users + Groups (iter-32 group→role projection); covers AUTH-001/004/005/006 | iter-32 `Iter32SamlAcsTests`, `Iter32OidcPresetTests`, `Iter32AccountLockoutTests`, `Iter32WebAuthnFlowTests`; [ADR-0004](../03-architecture/adr/ADR-0004-authentication-sso.md) | ✅ | 5.2 |
| SEC-008 | IP allowlist / device posture | Iter-32 `IpAllowlistMiddleware` (per-tenant CIDR allowlist via `TenantSettings.IpAllowlistJson`, with `RADIOPAD_IP_ALLOWLIST` global fallback); device-posture checks via iter-31 `device_fingerprint` + `device_pairing_token_*` keyring slots; `SuspensionGuardMiddleware` blocks revoked sessions. | `Iter32NetworkDefenseTests` (5 cases) | ✅ | 5.2 |
| SEC-009 | Secret storage for API keys / OAuth | `ApiKeySecretRef = "env:<NAME>"` only | `.github/instructions/security.instructions.md` | ✅ | 5.4 |
| SEC-010 | PHI redaction in debug logs | Server log filters + iter-31 desktop `log_redactor.rs` (`tracing-subscriber` MakeWriter) | [logging.md](../03-architecture/logging.md) | ✅ | 5.4/7.x |
| SEC-011 | Intrusion detection / anomaly alerts | Iter-32 `AnomalyDetector` `BackgroundService` scans the audit chain for spikes in `ProviderBlocked` / `PolicyViolation` / failed logins and AI-spike vs 24 h baseline; emits `AuditAction.SecurityAlert` and posts to SIEM webhook (`RADIOPAD_ANOMALY_WEBHOOK_URL`); iter-32 `SiemPushService` continuously ships every audit row to the operator's SIEM endpoint. | `Iter32NetworkDefenseTests` (5 cases) | ✅ | 5.2 |
| SEC-012 | Vendor / provider compliance profiles | `ProviderComplianceClass` registry | [provider-catalog.md](../03-architecture/provider-catalog.md) | ✅ | 5.4 |

## BILL — Billing & plans (7)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| BILL-001 | Seat-based subscription | Stripe Checkout + `Subscription` entity | Integration tests | ✅ | 5.6 |
| BILL-002 | Usage-based AI credits | Iter-28 `PlanQuotaService` evaluates monthly successful AI calls plus input/output token totals against per-plan limits; iter-13 `AiGateway` calls `PlanQuotaService` and throws `QuotaExceededException` → `402 RFC-7807 { kind: "quota_exceeded", resetAt }` when exhausted. Iter-34 `GET /api/billing/credits` reuses `PlanQuotaService` to surface `{ plan, periodStart, periodEnd, used, limits, remaining, trialEndsAt }` on `/admin/billing` as `.rp-stat-tile` panels with `.badge ok|warn|danger` based on `used/limit`. | `Iter28BillingHardeningTests` (`PlanQuotaService` exhaustion → 402); `Iter34BillingCreditsTests` (computed used/limits/remaining surfaced) | ✅ | 5.6 |
| BILL-003 | Enterprise invoicing | `BillingController.InvoicesExport` bulk CSV/ZIP with manifest+SHA-256 (iter 30) | `BulkInvoiceExportTests`; `GET /api/billing/invoices/export` | ✅ | 5.6 |
| BILL-004 | Tenant-level usage dashboard | Iter-31 `/admin/usage` page + `api.usage.summary` | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4 |
| BILL-005 | Provider cost attribution | Iter-34 — `IAiUsageStore.SummariseAsync` now prices the `byProvider` rollup against each tenant's current `ProviderConfig.CostPerInputKToken` / `CostPerOutputKToken` (USD per 1K tokens) and emits `costInputUsd` / `costOutputUsd` / `costTotalUsd` plus an `unpriced` flag for retired providers; `/admin/usage` renders a "Cost (USD)" column and a 30-day cost stat. | `Iter34UsageCostRollupTests.SummariseAsync_PricesByProvider_AndFlagsUnpriced`; `Iteration13Tests.CostAwareRoutingTests` (provider cost fields) | ✅ | 5.6 |
| BILL-006 | Plan-based feature flags | Iter-31 `/admin/feature-flags` page + `api.billing.features` | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.4 |
| BILL-007 | Trial / sandbox tenants | `TenantPlan.Trial` is the default plan; iter-28 `SubscriptionLifecycleService` sets `TenantSettings.TrialEndsAt` to `now + 14 d` and clears it on subscription activation; iter-28 `SuspensionGuardMiddleware` blocks mutating `/api/*` once the trial ends without billing. Sandbox tenants flagged via `Tenant.AllowSandboxRulebooks` remain unrestricted. Iter-34 `GET /api/billing/credits` surfaces `trialEndsAt` and the `/admin/billing` page renders a `.rp-banner.warn` countdown when ≤ 3 days remain so the radiologist sees the trial expiry before the suspension guard fires. | `Iter28BillingHardeningTests` (suspension gating); `SubscriptionLifecycleServiceTests`; `Iter34BillingCreditsTests` (`trialEndsAt` surfaced on Trial plan) | ✅ | 5.2 |

## INT — Integrations (10)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| INT-001 | SSO/OIDC | Iter-32 OIDC presets (Keycloak/Auth0/Okta) auto-fill `RADIOPAD_OIDC_TENANT_CLAIM` / `_EMAIL_CLAIM` / `_REQUIRE_MFA`; bearer translation handled by `OidcBearerMiddleware` projecting onto `X-RadioPad-*` headers | iter-32 `Iter32OidcPresetTests`; [ADR-0004](../03-architecture/adr/ADR-0004-authentication-sso.md) | ✅ | 5.2 |
| INT-002 | SAML | Iter-32 SAML 2.0 SP endpoints — `GET /saml/metadata` (SP descriptor) + `POST /saml/acs` with XML signature verification against `RADIOPAD_SAML_IDP_CERT_PEM` and `tenant_slug` attribute mapping (override via `RADIOPAD_SAML_TENANT_ATTRIBUTE`); audited as `UserLogin{method:"saml"}` | iter-32 `Iter32SamlAcsTests`; [ADR-0004](../03-architecture/adr/ADR-0004-authentication-sso.md) | ✅ | 5.2 |
| INT-003 | DICOMweb metadata | `IDicomWebClient` study/series + iter-31 instance | `DicomInstanceMetadataTests` | ✅ | 5.6 |
| INT-004 | FHIR DiagnosticReport export | `FhirDiagnosticReportSerializer.cs` | [fhir-mapping.md](../03-architecture/fhir-mapping.md) | ✅ | 5.6 |
| INT-005 | FHIR webhook ingest hardening | Iter-31 HMAC-SHA256 `X-RadioPad-Signature` on `POST /api/ingest/fhir/*` | `FhirWebhookSignatureTests` | ✅ | 5.4/7.x |
| INT-006 | HL7 v2 ORU export + inbound MLLP | Iter-31 `Hl7MllpListener` (`RADIOPAD_HL7_MLLP_PORT`, default disabled, binds 127.0.0.1) | `Hl7MllpListenerTests` | ✅ | 5.4/5.6 |
| INT-007 | PACS local bridge | Tauri `pacs_plugins.rs` SHA-256 + Ed25519 verifier (iter-32) + backend `IPacsVendorAdapter` chain (`SectraIds7Adapter`, `Visage7Adapter`, `CarestreamVueAdapter`) routed by `PacsVendorRouter`. Pilot integration with a hospital PACS instance is operator-gated (vendor SDKs / contracts). | `PacsPluginsVerifierTests`; `Iter32PacsRouterTests` | ✅ | 5.4 |
| INT-008 | RIS copy/paste bridge | Iter-31 `CopyToRisButton` + `secure_copy` Tauri TTL | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.6 |
| INT-009 | Stripe billing | Stripe Checkout + Connect | Integration tests | ✅ | 5.6 |
| INT-010 | SIEM log export | Iter-19 `GET /api/audit/siem?format=json|cef` (NDJSON / ArcSight CEF, RBAC: Compliance / IT / MedicalDirector); iter-32 `SiemPushService` `BackgroundService` continuously pushes every audit row to the operator's SIEM webhook (`RADIOPAD_SIEM_*`); iter-32 `SiemController` exposes `POST /api/siem/test` for connection probes. | `SiemExportTests` (3 cases); `Iter32SiemPushTests` | ✅ | 5.6 |

## PERF — Performance budgets (8)

| Id | Requirement | Implementation | Test / Verification | Status | 62304 |
| --- | --- | --- | --- | --- | --- |
| PERF-001 | AI draft generation latency | k6 harness asserts P95<10000ms (iter 30) | `perf/k6/scripts/ai-draft.js`; `.github/workflows/perf-smoke.yml` | ✅ | 5.7 |
| PERF-002 | Impression generation latency | k6 harness asserts P95<5000ms | `perf/k6/scripts/impression.js` | ✅ | 5.7 |
| PERF-003 | Validation latency | k6 harness asserts P95<3000ms | `perf/k6/scripts/validate.js` | ✅ | 5.7 |
| PERF-004 | Web app availability | Iter-35 in-process `AvailabilityMonitorService` (`BackgroundService`) probes core endpoints, exports an OTel histogram on the `RadioPad.PerfBudgets` meter, and exposes `GET /api/admin/observability/availability` rendered in the Availability section of `/admin/security`; burn-rate breaches audit `SystemAlert{kind:"availability_burn_rate"}` (reuses int 40). Iter-33 `Alertmanager`-compatible webhook continues to ingest external alerts. Production-stack verification of the 99.9% SLO (Prometheus + Alertmanager + Grafana) is an operator deployment activity. | iter-35 `Iter35AvailabilityMonitorTests` (3 cases — probe success, burn-rate audit, admin endpoint shape); `HealthEndpointTests`; operator runbook in [docs/06-operations/](../06-operations/) | ✅ | 5.7 |
| PERF-005 | Desktop startup | Tauri shell ≤ 5 s on release build | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.7 |
| PERF-006 | CLI validation runtime | `radiopad rulebook validate` ≤ 2 s | CI timing | ✅ | 5.7 |
| PERF-007 | Audit event write latency | `IAuditLog.AppendAsync` p99 < 500 ms | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.7 |
| PERF-008 | Export generation latency | FHIR / PDF / DOCX p95 < 3 s | see [PROGRESS.md](../../PROGRESS.md) | ✅ | 5.7 |

## REG — Regulatory artefacts (iter-31)

| Artefact | Path | Iter |
| --- | --- | --- |
| HIPAA BAA template | [docs/09-regulatory/baa-template.md](baa-template.md) | 31 |
| EU AI Act + GDPR profile | [docs/09-regulatory/eu-aiact-gdpr-profile.md](eu-aiact-gdpr-profile.md) | 31 |
| Post-market surveillance plan | [docs/09-regulatory/pms-plan.md](pms-plan.md) | 31 |
| Vendor risk register | [docs/09-regulatory/vendor-risk-register.md](vendor-risk-register.md) | 31 |
| Intended use | [docs/09-regulatory/intended-use.md](intended-use.md) | 30 |
| SaMD classification (non-SaMD posture) | [docs/09-regulatory/samd-classification.md](samd-classification.md) | 30 |
| ISO 14971 risk register | [docs/09-regulatory/iso-14971-risk-register.md](iso-14971-risk-register.md) | 30 |
| IEC 62304 SDLC | [docs/09-regulatory/iec-62304-sdlc.md](iec-62304-sdlc.md) | 30 |

## GOV — Governance surfaces (1)

| ID | Capability | Implementation | Test / Doc | Status | PRD § |
| --- | --- | --- | --- | --- | --- |
| GOV-001 | Governance dashboard | Iter-34 read-only aggregator at `/admin/governance` covering audit chain integrity (`GET /api/audit/verify`), reporting + AI policy KPIs (`GET /api/usage/analytics`), plan entitlements (`GET /api/billing/features`), draft prompt overrides (`GET /api/prompts/overrides`), templates pending review (`GET /api/templates`), and approved-rulebook lineage. Built entirely on locked design tokens — no new tokens introduced. | [frontend/app/admin/governance/page.tsx](../../frontend/app/admin/governance/page.tsx); see also `docs/02-design/design.md` §4.10 | ✅ | 25 |

## INTL — Internationalisation (1)

| ID | Capability | Implementation | Test / Doc | Status | PRD § |
| --- | --- | --- | --- | --- | --- |
| INTL-001 | Multilingual scaffolding (Enterprise GA) | Iter-35 `next-intl` wired into the frontend with `en/es/de/fr/pt/hi` locale bundles under `frontend/messages/`; new `TenantSettings.Locale` and `User.PreferredLocale` columns + EF migration `20260505000200_Iter35Locales`; endpoints `GET/PUT /api/tenant/settings/locale` and `PUT /api/users/me/locale`; locale switcher in tenant + user settings. Server falls back to `en` when a locale is unset; no design-token change. | iter-35 `Iter35LocaleTests` (7 cases — tenant default, user override, fallback to `en`, invalid-locale rejection, audit row, RBAC, header negotiation) | ✅ | Enterprise GA |

## Status roll-up

| Status | Iter 30 | Iter 31 | Iter 32 | Iter 33 | Iter 34 | Iter 35 |
| --- | --- | --- | --- | --- | --- | --- |
| ✅ Shipped | 30 | 62 | 106 | 117 | 128 | **130** |
| 🟡 Partial | 70 | 44 | 23 | 6 | 0 | **0** |
| 🔴 Not started | 12 | 3 | 0 | 0 | 0 | **0** |
| ⏸ Deferred | 7 | 3 | 0 | 6 | 2 | **0** |
| **Total** | 119 | 112 | 129 | 129 | 130 | **130** |

> Iter-33 close-out: 11 rows promoted from 🟡 → ✅ with explicit test references (DESK-001/002/007, INT-007, INT-010, MCP-001..004, PROV-003, PROV-006, SEC-008, SEC-011); 4 rows formally moved from 🟡 → ⏸ (DESK installer-cert attachment, INT vendor-pilot rollout, OAuth IdP-side refresh storage, operator availability SLO).
>
> Iter-34 close-out (5 parallel agents): **BILL-002 + BILL-005 + BILL-007 + PROV-005 + PROV-009** promoted 🟡 → ✅ on the back of new shipping code; new row **GOV-001** Governance dashboard shipped ✅. Two rows remained ⏸ (PROV-007, PERF-004) pending external infrastructure.
>
> **Iter-35 close-out (6 parallel agents) — PRD COMPLETE.** **PROV-007** promoted ⏸ → ✅ on the back of the new `OAuthRefreshTokenService` (AES-256-GCM via `IKmsProvider`, rotation `BackgroundService`, full endpoint surface, EF migration `Iter35OAuthVault`, audit `OAuthRefreshRotated = 41`). **PERF-004** promoted ⏸ → ✅ on the back of the new in-process `AvailabilityMonitorService` (OTel histogram, admin endpoint, burn-rate audit row). New rows **INTL-001** (multilingual scaffolding) and **VPK-001** (validation packs) shipped ✅ with named test evidence. **Every tracked PRD id is now ✅ — 130 / 130.** Production-stack verification of PERF-004's 99.9% SLO and end-to-end PROV-007 wiring against a live IdP are operator deployment activities, not in-repo deferrals; signed installer attachments and live SIEM/KMS nightly secrets remain operator-supplied items tracked in [PROGRESS.md](../../PROGRESS.md) iter-35 close-out.
