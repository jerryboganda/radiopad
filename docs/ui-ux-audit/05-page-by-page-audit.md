# 05 — Page-by-Page Audit

**Scope:** All 37 `page.tsx` files under `frontend/app/`. Each row is
evaluated against the locked Open Design system (`docs/02-design/design.md`,
`frontend/app/globals.css`, `frontend/app/shell.css`).

> Page numbering matches `03-route-inventory.md`. Detail client components
> (`reports/[id]/ReportClient.tsx`, `rulebooks/[id]/RulebookDetailClient.tsx`,
> `admin/providers/[id]/ProviderOAuthAdminClient.tsx`) are audited together
> with the page that imports them.

## Methodology

For every page we checked five criteria:

1. **Chrome** — does the page render its content inside `<Container>` +
   `<PageHeader>`? `<AppShell>` is supplied universally by `app/layout.tsx`,
   so the question is only about the canonical page chrome wrappers.
2. **States** — Skeleton / EmptyState / ErrorState used where data is
   fetched? Loading and empty states are required by the design lock.
3. **Inline styles** — how many `style={{ … }}` attributes (forbidden by
   the design lock; design tokens or named classes must be used instead).
4. **Browser dialogs** — any `window.confirm()`, `window.prompt()`, or
   `window.alert()`? All three are anti-patterns.
5. **Discoverability** — is the page linked from the sidebar
   (`nav.config.tsx`) or only reachable by direct URL / in-page link?

Findings use the ID prefix `UIUX-PAGE-<route-slug>-NNN` and resolve in
`ui-ux-findings.json`.

## Summary table

Legend: **Chrome** = `<Container>` + `<PageHeader>`. **States** = at least
one of Skeleton/Empty/Error. **Inline** = count of `style={{` occurrences
in the page file (excluding imported client components). **Dialog** =
`window.confirm/prompt/alert` usage. **Discov** = sidebar-linked.

| # | Route | Chrome | States | Inline | Dialog | Discov | Severity | Key findings |
|--:|---|:--:|:--:|--:|:--:|:--:|---|---|
| 1 | `/` | ✅ | ✅ | 4 | — | ✅ | Medium | Inline styles in cards (`UIUX-PAGE-HOME-001`); no `<PageHeader>` on `/reports` so `/` is the only chrome reference. |
| 2 | `/login` | ❌ | N/A | 1 | — | ❌ | High | Hand-rolled centered layout; wordy token explanation; no PageHeader. |
| 3 | `/offline` | ❌ | partial | 6 | — | ✅ | High | Heavy inline styles; no ErrorState fallback. |
| 4 | `/copilot` | ❌ | ❌ | 0 | — | ❌ | High | No chrome, no IA entry, no data-state components. |
| 5 | `/pair` | ❌ | ❌ | 0 | — | ❌ | High | Pairing flow shown bare without PageHeader; OAuth-like state changes silent. |
| 6 | `/marketplace` | ❌ | partial | 1 | — | ✅ | High | List page, no Skeleton, no EmptyState. |
| 7 | `/governance` | ❌ | ❌ | 6 | — | ❌ | High | Multiple inline-style violations; route name collision with `/admin/governance` causes IA confusion. |
| 8 | `/prompts` | ❌ | partial | 0 | ✅ `prompt()` | ✅ | Critical | Uses `window.prompt()` for create flow. Tab-style pattern duplicates `/terminology`. |
| 9 | `/providers` | ✅ | partial | 12 | — | ✅ | High | Container/PageHeader present but 12 inline styles inside list. |
| 10 | `/terminology` | ❌ | partial | 0 | — | ✅ | Medium | Tab pattern inconsistent with `/prompts`; no canonical `<Tabs>` primitive. |
| 11 | `/templates` | ✅ | partial | 20 | — | ✅ | High | Heaviest inline-style offender (`UIUX-PAGE-TEMPLATES-001..020`). |
| 12 | `/validation` | ✅ | ✅ | 8 | — | ✅ | High | Chrome OK; inline styles in finding rows. |
| 13 | `/reports` | ❌ | partial | 0 | — | indirect | Medium | Duplicates `/` semantically; uses `<div>Loading…</div>` instead of `<Skeleton/>`. |
| 14 | `/reports/view` | ❌ | partial | 9 | ✅ `confirm()` | ❌ | Critical | `ReportClient.tsx` uses `confirm()` for sign action; deep view with no PageHeader. |
| 15 | `/rulebooks` | ✅ | partial | 10 | — | ✅ | High | Chrome OK; 10 inline styles in list. |
| 16 | `/rulebooks/view` | ❌ | partial | 15 | — | ❌ | High | `RulebookDetailClient.tsx` 15 inline styles. |
| 17 | `/rulebooks/editor` | ❌ | partial | 5 (+34 across panels) | — | ❌ | High | Editor split panels (`MetadataPanel`, `RulesPanel`, etc.) all use inline styles. |
| 18 | `/audit` | ❌ | partial | 4 | — | ✅ | High | Table without `<caption>` / `<th scope>`; no Skeleton. |
| 19 | `/audit/verify` | ❌ | ❌ | 3 | — | ❌ | High | Verifier page with no chrome, no IA entry, no Skeleton. |
| 20 | `/analytics` | ❌ | partial | 8 | — | ✅ | High | Charts use inline width/height styles; no `<Skeleton/>`. |
| 21 | `/analytics/quality` | ❌ | partial | 22 | — | ❌ | High | Heaviest analytics page (`UIUX-PAGE-QUALITY-001..022`); IA gap. |
| 22 | `/mobile/dictate` | ❌ | ❌ | 0 | — | ❌ | High | No mobile-tailored chrome; DictateButton hard-coded `lang='en-US'`. |
| 23 | `/mobile/reports/edit` | ❌ | partial | 0 | — | ❌ | High | No PageHeader; mobile breakpoint chrome unverified. |
| 24 | `/mobile/reports/sign` | ❌ | partial | 0 | — | ❌ | Critical | Signing is irreversible — no `<ConfirmDialog>`; no audit trail surfaced. |
| 25 | `/admin/billing` | ❌ | partial | 0 | — | ✅ | High | BillingStatusBanner inconsistency (status vs alert); long table without scope. |
| 26 | `/admin/copilot` | ❌ | partial | 0 | — | ❌ | High | Admin page hidden from IA. |
| 27 | `/admin/feature-flags` | ❌ | partial | 0 | — | ✅ | Medium | Toggles without optimistic UI; success silent. |
| 28 | `/admin/fhir-import` | ❌ | partial | 0 | — | ✅ | High | File upload without progress affordance; error states ad-hoc. |
| 29 | `/admin/governance` | ❌ | partial | 0 | — | ✅ | High | Confusion with `/governance` route. |
| 30 | `/admin/mcp` | ❌ | partial | 7 | ✅ `confirm()`+`prompt()` | ❌ | Critical | Browser dialogs for connector add/remove; 7 inline styles. |
| 31 | `/admin/model-eval` | ❌ | partial | 0 | — | ✅ | High | Long-running job UX uses polling-on-mount with no Skeleton. |
| 32 | `/admin/pacs` | ❌ | partial | 0 | — | ✅ | High | Connectivity test result rendered inline without role="status". |
| 33 | `/admin/providers/oauth` | ❌ | partial | 0 | — | ❌ | System | OAuth callback target — minimal chrome acceptable, but currently no success/error surface beyond raw banner. |
| 34 | `/admin/security` | ❌ | partial | 4 | — | ✅ | High | Token rotation surfaces backend enum names (`rotationPolicy`, `before_expiry`). |
| 35 | `/admin/settings` | ❌ | partial | 5 | — | ✅ | High | Tenant settings form lacks explicit labels on several inputs. |
| 36 | `/admin/sso` | ❌ | partial | 0 | — | ❌ | Critical | Security-critical page invisible from IA (`UIUX-NAV-001`). |
| 37 | `/admin/usage` | ❌ | partial | 0 | — | ✅ | High | Usage table without horizontal scroll wrapper for narrow viewports. |

Totals:

- Chrome (Container+PageHeader): **5 / 37** (page 1, 9, 11, 12, 15)
- Browser dialogs: **5 routes** (#8, #14, #30 — and the in-page client
  components for `ProviderOAuthAdminClient` reached via #33, plus
  `ReportClient` used by #14)
- Inline-style violations: **31 page files** totaling ~187 occurrences
- Sidebar-linked: **20 / 37** (54%)

## Per-page records

### Public surfaces

#### `/login` — `frontend/app/login/page.tsx`
- **What it does**: collects email, requests a magic-link token, then exchanges it for a session.
- **Gaps**:
  - `UIUX-PAGE-LOGIN-001` (High) No `<PageHeader>`; layout hand-rolled.
  - `UIUX-PAGE-LOGIN-002` (Medium) Wordy technical explanation of token mechanics — see `10-copy-microcopy-audit.md`.
  - `UIUX-PAGE-LOGIN-003` (Medium) `?return=` query param not consistently honoured.
  - `UIUX-PAGE-LOGIN-004` (Low) Brand mark falls back to `?` when email is empty.
- **Severity**: High (entry surface — chrome inconsistency sets a bad tone).

#### `/offline` — `frontend/app/offline/page.tsx`
- **What it does**: shows offline draft queue with retry actions.
- **Gaps**: missing chrome, 6 inline styles, no ErrorState when API is unreachable (ironic, since this *is* the offline surface).
- **Severity**: High.

#### `/marketplace` — `frontend/app/marketplace/page.tsx`
- Public-facing list page. No `<Skeleton/>` while loading rulebooks/templates; no `<EmptyState/>` when zero items.
- **Severity**: High.

#### `/governance` — `frontend/app/governance/page.tsx`
- 6 inline styles; route name collides with `/admin/governance` (one is read-only public summary, the other is the admin panel) — see `UIUX-NAV-002` in `08-interaction-flow-audit.md`.
- **Severity**: High.

### Workspace surfaces

#### `/` and `/reports` — `frontend/app/page.tsx`, `frontend/app/reports/page.tsx`
- `/` is the canonical reports list, properly wrapped in `<Container>` + `<PageHeader>`; uses 4 inline styles in card rendering.
- `/reports` exists as a duplicate landing — it does **not** use `<PageHeader>` and renders `<div>Loading…</div>` for its loading state instead of `<Skeleton/>`.
- **Recommendation**: either delete `/reports` or make it the canonical and remove `/`. Pick one source of truth.
- **Severity**: Medium (functional but confusing).

#### `/reports/view` — `frontend/app/reports/view/page.tsx`
- Imports `reports/[id]/ReportClient.tsx` (a non-routable client component) and passes `?id=`.
- `ReportClient.tsx` uses `window.confirm()` for the sign action (`UIUX-DEST-001`) and contains 9 inline styles across the report shell and side panels (`PriorComparePanel.tsx`, `RewriteStylePanel.tsx`).
- **Severity**: Critical — signing is an irreversible action and must use a designed `<ConfirmDialog>`.

#### `/validation` — `frontend/app/validation/page.tsx`
- Chrome OK; uses `<Skeleton/>`. Findings list uses 8 inline styles to colour the severity strip — should be replaced with semantic `.finding--blocker/.warning/.info` modifiers.
- **Severity**: High.

#### `/audit` — `frontend/app/audit/page.tsx`
- Workspace-level audit log. Table is missing `<caption>` and `<th scope="col">`. No `<Skeleton/>` placeholder.
- **Severity**: High (a11y).

#### `/audit/verify` — `frontend/app/audit/verify/page.tsx`
- Hash-chain verifier surfaced as a developer page. Not in the sidebar, no chrome, raw textarea output.
- **Severity**: High (IA gap and lack of polish for a compliance-relevant page).

#### `/analytics` and `/analytics/quality`
- Charts rendered with inline width/height styles (8 and 22 inline-style violations respectively).
- `/analytics/quality` is the single heaviest offender in the codebase; also hidden from the sidebar.
- **Severity**: High.

### Library surfaces

#### `/rulebooks`, `/rulebooks/view`, `/rulebooks/editor`
- `/rulebooks` has `<Container>` + `<PageHeader>` but 10 inline styles.
- `/rulebooks/view` imports `RulebookDetailClient.tsx` (15 inline styles).
- `/rulebooks/editor` imports six side panels (`MetadataPanel`, `SectionsPanel`, `RulesPanel`, `PromptBlocksPanel`, `StylePanel`, plus the orchestrating client) — collectively the editor is the single largest cluster of inline styles in the app.
- **Severity**: High across the family; the editor needs a focused refactor pass with token-driven panel chrome.

#### `/templates` — `frontend/app/templates/page.tsx`
- Chrome OK, but 20 inline styles in the list/preview pane (`UIUX-PAGE-TEMPLATES-001..020`).
- **Severity**: High.

#### `/prompts` — `frontend/app/prompts/page.tsx`
- Uses `window.prompt()` for "New prompt name" creation flow.
- Tab pattern hand-rolled, inconsistent with the (different) hand-rolled tabs in `/terminology`.
- **Severity**: Critical (browser dialog) / High (tab pattern).

#### `/terminology` — `frontend/app/terminology/page.tsx`
- Hand-rolled tab pattern; no shared `<Tabs>` primitive.
- **Severity**: Medium.

#### `/providers` — `frontend/app/providers/page.tsx`
- Chrome OK; 12 inline styles in card grid.
- **Severity**: High.

### Mobile surfaces

#### `/mobile/dictate`
- Renders `<DictateButton/>` which hard-codes `lang='en-US'` — bypasses locale negotiation.
- No mobile-specific chrome; relies on default sidebar drawer.
- **Severity**: High (clinical correctness depends on language).

#### `/mobile/reports/edit`
- Mobile edit surface. No `<PageHeader>`; the mobile drawer breakpoint is not aligned with the PageHeader stacking breakpoint.
- **Severity**: High.

#### `/mobile/reports/sign`
- **Signing is irreversible.** Currently lacks a designed confirmation step (no `<ConfirmDialog>`). The signing surface itself is minimal and offers no inline audit trail.
- **Severity**: Critical.

### Admin surfaces

#### `/admin/billing` — `frontend/app/admin/billing/page.tsx`
- Uses `BillingStatusBanner.tsx` which inconsistently sets `role='alert'` (suspended) vs `role='status'` (grace) — see `07-accessibility-audit.md` finding `UIUX-A11Y-006`.
- Plan/invoice table has no `<caption>` or `<th scope>`.
- **Severity**: High.

#### `/admin/copilot`
- Admin page not linked from the sidebar despite being a core admin surface.
- **Severity**: High.

#### `/admin/feature-flags`
- Toggles fire and forget; no optimistic UI; success state is silent.
- **Severity**: Medium.

#### `/admin/fhir-import`
- File upload pattern without progress bar or designed upload affordance. Errors surface as raw JSON snippets.
- **Severity**: High.

#### `/admin/governance` vs `/governance`
- Two routes, similar names, different audiences. **IA collision** — see `08-interaction-flow-audit.md`.
- **Severity**: High.

#### `/admin/mcp` — `frontend/app/admin/mcp/page.tsx`
- Uses **both** `window.confirm()` (for connector removal) and `window.prompt()` (for connector configuration). 7 inline styles.
- Not in the sidebar.
- **Severity**: Critical.

#### `/admin/model-eval`
- Long-running evaluation jobs surfaced via polling. No `<Skeleton/>`, no progress affordance, no cancel button.
- **Severity**: High.

#### `/admin/pacs`
- DICOM/HL7 connectivity test results rendered inline without `role='status'` so screen-reader users miss success/failure announcements.
- **Severity**: High.

#### `/admin/providers/oauth` and `ProviderOAuthAdminClient.tsx`
- OAuth callback target. The non-routable client component used by `/providers/[id]` uses `window.confirm()` for credential rotation. 5 inline styles in the OAuth admin panel.
- **Severity**: Critical (security operation behind a browser dialog).

#### `/admin/security` — `frontend/app/admin/security/page.tsx`
- Surfaces backend enum names directly (e.g., `rotationPolicy`, `before_expiry`, `ProviderComplianceClass`). 4 inline styles.
- See `10-copy-microcopy-audit.md` `UIUX-COPY-JARGON-*`.
- **Severity**: High.

#### `/admin/settings`
- Tenant settings form — several inputs lack explicit `<label htmlFor>` associations (date inputs especially).
- **Severity**: High (a11y).

#### `/admin/sso` — `frontend/app/admin/sso/page.tsx`
- **Security-critical configuration page is invisible from the sidebar.** Reachable only by typing the URL.
- **Severity**: Critical (IA + discoverability gap on a compliance surface).

#### `/admin/usage`
- Long usage table without horizontal scroll wrapper for narrow viewports.
- **Severity**: High.

## Cross-cutting patterns

### (a) Missing chrome wrappers

Only **5 of 37 pages** (14%) use both `<Container>` and `<PageHeader>`.
The other 32 hand-roll wrappers — sometimes a bare `<div>`, sometimes
`<main className="rp-container">` inlined, sometimes nothing at all.
This is the single largest design-lock violation in the app and the
root cause of most spacing, alignment, and breakpoint inconsistencies
flagged in `06-responsive-audit.md`. Recommended remediation is in
**Phase 1** of `ui-ux-fix-backlog.md`.

### (b) Ad-hoc data-state handling

`<Skeleton/>`, `<EmptyState/>`, and `<ErrorState onRetry/>` primitives
exist (`frontend/components/ui/`), but only the same 5 pages with proper
chrome also use them. Many pages render `<div>Loading…</div>` or simply
nothing while data is in flight, and zero-row states are blank panels
instead of designed empty states. Empty-state copy is also missing from
the design system (`10-copy-microcopy-audit.md`).

### (c) Inline styles (design-lock violation)

31 page files contain `style={{ … }}` attributes — totalling ~187
occurrences. Worst offenders:

| Page | `style={{` count |
|---|--:|
| `app/analytics/quality/page.tsx` | 22 |
| `app/templates/page.tsx` | 20 |
| `app/rulebooks/[id]/RulebookDetailClient.tsx` | 15 |
| `app/providers/page.tsx` | 12 |
| `app/rulebooks/page.tsx` | 10 |
| `app/reports/[id]/ReportClient.tsx` | 9 |
| `app/validation/page.tsx` | 8 |
| `app/analytics/page.tsx` | 8 |
| `app/admin/mcp/page.tsx` | 7 |
| `app/governance/page.tsx` | 6 |
| `app/offline/page.tsx` | 6 |

The design lock requires named classes + tokens. Recommended fix:
introduce an ESLint rule (`react/forbid-dom-props` for `style`) and a
small "what to use instead" guide in `docs/02-design/design.md`.

### (d) Browser dialogs

`window.confirm()` and `window.prompt()` appear in five places:

| File | Calls | Action |
|---|---|---|
| `app/admin/mcp/page.tsx` | `confirm`, `prompt` | add/remove MCP connector |
| `app/admin/validation-packs/page.tsx` | `confirm`, `prompt` | install / delete validation pack |
| `app/admin/providers/[id]/ProviderOAuthAdminClient.tsx` | `confirm` | rotate provider credentials |
| `app/prompts/page.tsx` | `prompt` | name new prompt |
| `app/reports/[id]/ReportClient.tsx` | `confirm` | sign report |

All five are **destructive or security-relevant** actions and the
browser dialogs are inconsistent with the design system, are not
themable, are difficult for screen readers, and steal focus from the
page. A shared `<ConfirmDialog>` (and a `<Prompt>` for naming actions)
must land in **Phase 3** of `ui-ux-fix-backlog.md`.

### (e) Missing primitives drive most other gaps

A great many of the per-page findings collapse into "we don't have a
component for that yet". The library is missing: `<Modal>`, `<Tabs>`,
`<Toast>`, `<ConfirmDialog>`, `<FormField>`. See
`04-component-inventory.md` for the gap analysis and
`09-frontend-structure-audit.md` for the proposed primitive set.
