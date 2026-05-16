# 03 — Route Inventory

**Discovered routes:** 38 (one `page.tsx` per row). **No dynamic
segments** (`[id]`/`[...slug]`) are present at the route level; the
`[id]/` directories under `reports/`, `rulebooks/`, and
`admin/providers/` hold *client components* (e.g. `ReportClient.tsx`)
that the sibling `page.tsx` files import — they are not routable in
their own right. Detail pages take ids via query string
(e.g. `/reports/view?id=...`).

The shared shell (`AppShell` → `Sidebar` + `Topbar` + `MobileDrawerBackdrop` +
banners) wraps every route via the single `frontend/app/layout.tsx`. There
are no nested `layout.tsx`, `not-found.tsx`, `error.tsx`, or `loading.tsx`
files anywhere under `frontend/app/`.

**Auth model:** the typed `api` client (`frontend/lib/api.ts`) decides
visibility per request. There is no Next.js `middleware`-level redirect
for unauthenticated users; `/login` is the user-facing sign-in surface.
Admin pages are reachable by URL even when the API will refuse — they
should rely on their own `ErrorState` (this audit flags any that don't).

**Sidebar IA** (defined in `frontend/components/shell/nav.config.tsx`):

| Group | Items |
|---|---|
| **workspace** | Reports (`/`), Validation (`/validation`), Audit (`/audit`), Analytics (`/analytics`) |
| **library** | Rulebooks (`/rulebooks`), Templates (`/templates`), Prompts (`/prompts`), Marketplace (`/marketplace`), Terminology (`/terminology`) |
| **integrations** | Providers (`/providers`), PACS (`/admin/pacs`), FHIR import (`/admin/fhir-import`), Offline (`/offline`) |
| **admin** | Governance (`/admin/governance`), Model eval (`/admin/model-eval`), Security (`/admin/security`), Feature flags (`/admin/feature-flags`), Billing (`/admin/billing`), Usage (`/admin/usage`), Settings (`/admin/settings`) |

**20 of the 37 routes are linked from the sidebar; 17 are reachable only
by direct URL or in-page links** — see "Discoverability" column below.

## Route table

| # | Route | Type | Source File | Auth Reqd | Query Params | Linked from sidebar? | Discoverability | Audit Status |
|--:|---|---|---|---|---|---|---|---|
| 1 | `/` | Reports list (workspace home) | `frontend/app/page.tsx` | Yes (`api.me`) | — | ✅ Workspace › Reports | High | Audited |
| 2 | `/login` | Sign-in | `frontend/app/login/page.tsx` | No | `?return=` (?) | ❌ (Profile menu only) | Medium | Audited |
| 3 | `/offline` | Offline drafts | `frontend/app/offline/page.tsx` | Yes | — | ✅ Integrations › Offline | Medium | Audited |
| 4 | `/copilot` | AI Copilot workspace | `frontend/app/copilot/page.tsx` | Yes | — | ❌ | **Low** | Audited |
| 5 | `/pair` | Desktop pairing | `frontend/app/pair/page.tsx` | Yes | `?code=` | ❌ | **Low** | Audited |
| 6 | `/marketplace` | Rulebook/template marketplace | `frontend/app/marketplace/page.tsx` | Yes | — | ✅ Library › Marketplace | High | Audited |
| 7 | `/governance` | Public governance summary | `frontend/app/governance/page.tsx` | Yes (?) | — | ❌ (admin/governance is in nav) | **Low** | Audited |
| 8 | `/prompts` | Prompt library | `frontend/app/prompts/page.tsx` | Yes | — | ✅ Library › Prompts | High | Audited |
| 9 | `/providers` | AI providers list | `frontend/app/providers/page.tsx` | Yes | — | ✅ Integrations › Providers | High | Audited |
| 10 | `/terminology` | Terminology browser | `frontend/app/terminology/page.tsx` | Yes | — | ✅ Library › Terminology | High | Audited |
| 11 | `/templates` | Report templates | `frontend/app/templates/page.tsx` | Yes | — | ✅ Library › Templates | High | Audited |
| 12 | `/validation` | Validation runner | `frontend/app/validation/page.tsx` | Yes | — | ✅ Workspace › Validation | High | Audited |
| 13 | `/reports` | Reports list (duplicate of `/`?) | `frontend/app/reports/page.tsx` | Yes | — | ✅ implicitly (sidebar `/` matches `/reports*`) | High | Audited |
| 14 | `/reports/view` | Report detail | `frontend/app/reports/view/page.tsx` | Yes | `?id=` | ❌ (row click) | Medium | Audited |
| 15 | `/rulebooks` | Rulebook list | `frontend/app/rulebooks/page.tsx` | Yes | — | ✅ Library › Rulebooks | High | Audited |
| 16 | `/rulebooks/view` | Rulebook detail | `frontend/app/rulebooks/view/page.tsx` | Yes | `?id=` | ❌ (row click) | Medium | Audited |
| 17 | `/rulebooks/editor` | Rulebook YAML editor | `frontend/app/rulebooks/editor/page.tsx` | Yes (admin?) | `?id=` | ❌ (action from detail) | Medium | Audited |
| 18 | `/audit` | Audit log | `frontend/app/audit/page.tsx` | Yes | — | ✅ Workspace › Audit | High | Audited |
| 19 | `/audit/verify` | Audit chain verifier | `frontend/app/audit/verify/page.tsx` | Yes | — | ❌ | **Low** | Audited |
| 20 | `/analytics` | Analytics dashboard | `frontend/app/analytics/page.tsx` | Yes | — | ✅ Workspace › Analytics | High | Audited |
| 21 | `/analytics/quality` | Quality metrics | `frontend/app/analytics/quality/page.tsx` | Yes | — | ❌ | **Low** | Audited |
| 22 | `/mobile/dictate` | Mobile dictation | `frontend/app/mobile/dictate/page.tsx` | Yes | — | ❌ | **Low** (mobile-only) | Audited |
| 23 | `/mobile/reports/edit` | Mobile edit report | `frontend/app/mobile/reports/edit/page.tsx` | Yes | `?id=` | ❌ | **Low** (mobile-only) | Audited |
| 24 | `/mobile/reports/sign` | Mobile sign report | `frontend/app/mobile/reports/sign/page.tsx` | Yes | `?id=` | ❌ | **Low** (mobile-only) | Audited |
| 25 | `/admin/billing` | Billing & plan | `frontend/app/admin/billing/page.tsx` | Admin | — | ✅ Admin › Billing | High | Audited |
| 26 | `/admin/copilot` | Copilot admin | `frontend/app/admin/copilot/page.tsx` | Admin | — | ❌ | **Low** | Audited |
| 27 | `/admin/feature-flags` | Feature flags | `frontend/app/admin/feature-flags/page.tsx` | Admin | — | ✅ Admin › Feature flags | High | Audited |
| 28 | `/admin/fhir-import` | FHIR import | `frontend/app/admin/fhir-import/page.tsx` | Admin | — | ✅ Integrations › FHIR import | High | Audited |
| 29 | `/admin/governance` | Governance admin | `frontend/app/admin/governance/page.tsx` | Admin | — | ✅ Admin › Governance | High | Audited |
| 30 | `/admin/mcp` | MCP connectors | `frontend/app/admin/mcp/page.tsx` | Admin | — | ❌ | **Low** | Audited |
| 31 | `/admin/model-eval` | Model evaluation | `frontend/app/admin/model-eval/page.tsx` | Admin | — | ✅ Admin › Model eval | High | Audited |
| 32 | `/admin/pacs` | PACS config | `frontend/app/admin/pacs/page.tsx` | Admin | — | ✅ Integrations › PACS | High | Audited |
| 33 | `/admin/providers/oauth` | Provider OAuth callbacks | `frontend/app/admin/providers/oauth/page.tsx` | Admin | `?code=&state=` | ❌ (OAuth redirect target) | **System** | Audited |
| 34 | `/admin/security` | Security settings | `frontend/app/admin/security/page.tsx` | Admin | — | ✅ Admin › Security | High | Audited |
| 35 | `/admin/settings` | Tenant settings | `frontend/app/admin/settings/page.tsx` | Admin | — | ✅ Admin › Settings | High | Audited |
| 36 | `/admin/sso` | SSO config | `frontend/app/admin/sso/page.tsx` | Admin | — | ❌ | **Low** | Audited |
| 37 | `/admin/usage` | Usage metering | `frontend/app/admin/usage/page.tsx` | Admin | — | ✅ Admin › Usage | High | Audited |

## Coverage summary

- **Routes discovered:** 37
- **Routes audited (static review):** 37 ✅
- **Routes linked from sidebar:** 20
- **Routes reachable only by direct URL or in-page link:** 17
  - `copilot`, `pair`, `governance`, `reports/view`, `rulebooks/view`,
    `rulebooks/editor`, `audit/verify`, `analytics/quality`,
    `mobile/*` (3), `admin/copilot`, `admin/mcp`,
    `admin/providers/oauth`, `admin/sso`, plus the `/login` page
    (accessible via profile menu / unauthenticated redirect).
- **Pages missing from the App Router that you'd typically expect:**
  - `app/not-found.tsx` (custom 404)
  - `app/error.tsx` and `app/global-error.tsx`
  - `app/loading.tsx` (route-level suspense fallback)
  - No `error/`, `not-found/`, or `unauthorized/` route placeholders.
  → Flagged as `UIUX-STR-MISSING-PAGES-001`.

## Notes on the IA

- The sidebar has only 20 destinations; the other 17 pages are part of
  the product but invisible to first-time users. Several are clearly
  intentional (OAuth callback, mobile screens, in-app drilldowns), but
  `/copilot`, `/governance`, `/audit/verify`, `/analytics/quality`,
  `/admin/copilot`, `/admin/mcp`, and `/admin/sso` look like
  navigation gaps. Flagged in `08-interaction-flow-audit.md` as
  navigation-discoverability findings.
- `/` and `/reports` both render a reports list. Confirm intent — if they
  are the same view, one should redirect to the other to avoid stale
  bookmarks.
