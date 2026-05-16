**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Route Inventory

| Route | Type | Source File | Auth Required | Dynamic Params | Sample Data Needed | Status |
|---|---|---|---|---|---|---|
| `/` | Dashboard/report list | `frontend/app/page.tsx` | API-backed only | `?new=1` | Tenant, user, reports | Audited |
| `/login` | Auth/dev identity | `frontend/app/login/page.tsx` | Public | none | Auth sign-in API | Audited |
| `/pair` | Desktop pairing | `frontend/app/pair/page.tsx` | Public-looking | device code flow | Device auth endpoints | Audited |
| `/reports` | Legacy report list | `frontend/app/reports/page.tsx` | API-backed only | none | Reports | Audited |
| `/reports/view` | Report editor detail | `frontend/app/reports/view/page.tsx`, `frontend/app/reports/[id]/ReportClient.tsx` | API-backed only | `?id=` | Report, providers, rulebooks, templates, signatures | Audited |
| `/validation` | Validation center | `frontend/app/validation/page.tsx` | API-backed only | none | Reports, validation endpoint | Audited |
| `/audit` | Audit log | `frontend/app/audit/page.tsx` | API-backed only | none | Audit events | Audited |
| `/audit/verify` | Audit chain verifier | `frontend/app/audit/verify/page.tsx` | API-backed only | none | Audit verification endpoint | Audited |
| `/analytics` | Analytics dashboard | `frontend/app/analytics/page.tsx` | API-backed only | local range state | Analytics summary | Audited |
| `/analytics/quality` | Quality dashboard | `frontend/app/analytics/quality/page.tsx` | API-backed only | local range/group state | Quality trends | Audited |
| `/rulebooks` | Rulebook list/YAML import | `frontend/app/rulebooks/page.tsx` | Admin-looking, no route guard found | none | Rulebooks | Audited |
| `/rulebooks/view` | Rulebook detail | `frontend/app/rulebooks/view/page.tsx`, `frontend/app/rulebooks/[id]/RulebookDetailClient.tsx` | Admin-looking, no route guard found | `?id=` | Rulebook YAML, versions | Audited |
| `/rulebooks/editor` | Rulebook editor | `frontend/app/rulebooks/editor/page.tsx` | Admin-looking, no route guard found | optional `?id=` | Rulebook YAML | Audited |
| `/templates` | Template CRUD | `frontend/app/templates/page.tsx` | Admin-looking, no route guard found | modal state | Templates, preview, usage | Audited |
| `/prompts` | Prompt Studio | `frontend/app/prompts/page.tsx` | Role affordance via API only | tab/local state | Rulebooks, overrides, reports | Audited |
| `/marketplace` | Rulebook marketplace | `frontend/app/marketplace/page.tsx` | Admin/reviewer-looking | tab/local state | Listings, submissions | Audited |
| `/terminology` | Terminology browser | `frontend/app/terminology/page.tsx` | API-backed only | query/local state | RadLex/RADS endpoints | Audited |
| `/providers` | Provider admin | `frontend/app/providers/page.tsx` | Admin-looking, no route guard found | modal/local state | Providers, draft reports | Audited |
| `/offline` | Offline draft buffer | `frontend/app/offline/page.tsx` | Local/native storage | local draft IDs | Offline drafts | Audited |
| `/copilot` | Copilot user page | `frontend/app/copilot/page.tsx` | Account API only | none | Copilot status/account | Audited |
| `/governance` | Legacy governance summary | `frontend/app/governance/page.tsx` | API-backed only | none | Audit events | Audited |
| `/mobile/dictate` | Mobile dictation wrapper | `frontend/app/mobile/dictate/page.tsx`, `frontend/app/mobile/dictate/[reportId]/MobileDictateClient.tsx` | API-backed only | `?reportId=` | Report, speech permission | Audited |
| `/mobile/reports/edit` | Mobile edit wrapper | `frontend/app/mobile/reports/edit/page.tsx`, `frontend/app/mobile/reports/[reportId]/edit/MobileEditClient.tsx` | API-backed only | `?reportId=` | Report | Audited |
| `/mobile/reports/sign` | Mobile sign/export wrapper | `frontend/app/mobile/reports/sign/page.tsx`, `frontend/app/mobile/reports/[reportId]/sign/MobileSignClient.tsx` | API-backed only | `?reportId=`, export format | Report, validation, export endpoints | Audited |
| `/admin/billing` | Billing dashboard | `frontend/app/admin/billing/page.tsx` | Admin-looking, no route guard found | billing callback URLs | Billing, invoices, analytics | Audited |
| `/admin/copilot` | Copilot admin | `frontend/app/admin/copilot/page.tsx` | Admin-looking, no route guard found | none | Copilot settings/quotas | Audited |
| `/admin/feature-flags` | Feature flags | `frontend/app/admin/feature-flags/page.tsx` | Admin-looking, no route guard found | none | Billing features | Audited |
| `/admin/fhir-import` | FHIR import | `frontend/app/admin/fhir-import/page.tsx` | Admin-looking, no route guard found | pasted JSON | FHIR DiagnosticReport | Audited |
| `/admin/governance` | Governance dashboard | `frontend/app/admin/governance/page.tsx` | Frontend role gating | none | me, providers, rulebooks, prompts, usage, analytics, audit | Audited |
| `/admin/mcp` | MCP admin | `frontend/app/admin/mcp/page.tsx` | Admin-looking, no route guard found | tool state | MCP tools | Audited |
| `/admin/model-eval` | Model evaluation | `frontend/app/admin/model-eval/page.tsx` | Frontend role gating | selections | me, providers, rulebooks, validation packs, reports | Audited |
| `/admin/pacs` | PACS admin | `frontend/app/admin/pacs/page.tsx` | Admin-looking, no route guard found | none | Tenant settings, PACS health/plugins | Audited |
| `/admin/providers/oauth` | Provider OAuth detail | `frontend/app/admin/providers/oauth/page.tsx`, `frontend/app/admin/providers/[id]/ProviderOAuthAdminClient.tsx` | Admin-looking, no route guard found | `?id=` | Provider list, OAuth status | Audited |
| `/admin/security` | Security/SIEM admin | `frontend/app/admin/security/page.tsx` | Admin-looking, no route guard found | none | SIEM, settings, audit, observability | Audited |
| `/admin/settings` | Tenant settings | `frontend/app/admin/settings/page.tsx` | Admin-looking, no route guard found | billing callback URLs | Tenant settings, billing | Audited |
| `/admin/sso` | SSO guidance | `frontend/app/admin/sso/page.tsx` | Admin-looking, no route guard found | none | WebAuthn credentials | Audited |
| `/admin/usage` | Usage dashboard | `frontend/app/admin/usage/page.tsx` | Admin-looking, no route guard found | derived months | Usage and analytics | Audited |
| `/admin/validation-packs` | Validation packs | `frontend/app/admin/validation-packs/page.tsx` | Admin-looking, no route guard found | rulebook filter | Validation packs | Audited |
| Error/loading/not-found | Not present | No `error.tsx`, `loading.tsx`, or `not-found.tsx` found in inspected app dirs | n/a | n/a | n/a | Gap |

## Navigation Coverage Notes

Sidebar navigation covers primary workspace, library, integrations, and selected admin pages. Orphaned or non-sidebar routes include `/admin/copilot`, `/admin/mcp`, `/admin/sso`, `/admin/validation-packs`, `/admin/providers/oauth`, `/governance`, `/login`, `/pair`, and all mobile routes.

## Dynamic Route Note

Folders such as `reports/[id]`, `rulebooks/[id]`, `mobile/dictate/[reportId]`, and `admin/providers/[id]` contain client components but are not directly reachable route files without corresponding `page.tsx` files. The reachable routes use query parameters.
