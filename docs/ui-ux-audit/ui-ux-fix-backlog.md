# UI/UX Fix Backlog

This is the actionable engineering backlog derived from the audit. Each
ticket references finding IDs in `ui-ux-findings.json` and the matching
detailed audit doc. Effort is sized as **S** (≤1 day), **M** (2–5 days),
**L** (>5 days). No dates — phases are sequenced by dependency only.

## How to use this backlog

- Tickets in a phase can run in parallel unless a `Depends on` row says
  otherwise.
- Each phase has an **Acceptance** block — that's the merge gate.
- Finding IDs cross-link back to the JSON for status tracking.
- Tickets marked ⚠️ touch security or signing flows and require a
  human review under the rules in `AGENTS.md` §5.

## Phase 0 — Quick wins (≤ 1 day each, no design changes required)

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| QW-1 | Delete duplicate `.rp-container` rule | `frontend/app/radiopad.css` | S | Critical | UIUX-STR-001 |
| QW-2 | Delete duplicate `.rp-page-title` rule | `frontend/app/radiopad.css` | S | Critical | UIUX-STR-002 |
| QW-3 | Delete duplicate `.rp-page-sub` rule | `frontend/app/radiopad.css` | S | Critical | UIUX-STR-003 |
| QW-4 | Add aria-live='polite' to EmptyState | `frontend/components/ui/EmptyState.tsx` | S | High | UIUX-A11Y-005 |
| QW-5 | Fix BillingStatusBanner role consistency | `frontend/components/BillingStatusBanner.tsx` | S | High | UIUX-A11Y-006 |
| QW-6 | Add skip link to Topbar | `frontend/components/shell/Topbar.tsx` + globals.css | S | Critical | UIUX-A11Y-001 |
| QW-7 | Document pnpm wrapper workaround in README | `frontend/README.md` | S | High | UIUX-STR-015 |
| QW-8 | Add `package.json` scripts `tsc:direct`, `build:direct` | `package.json` | S | High | UIUX-STR-015 |

**Acceptance:** All 8 land in a single PR; typecheck still passes.

## Phase 1 — Chrome consolidation

**Goal:** Every routable page renders inside `<Container>` + `<PageHeader>`.

**Acceptance:**
- 35/35 routes wrap content in `<Container>` (or `<Container fluid>` for editor-class surfaces).
- 35/35 routes render `<PageHeader title=… description=… />`.
- An ESLint rule (or lint script) flags any new page that skips chrome.

| Ticket | Title | Files | Effort | Severity | Finding IDs | Depends on |
|---|---|---|---|---|---|---|
| P1-1 | Wrap `/login` in chrome | `frontend/app/login/page.tsx` | S | High | UIUX-CHROME-001 | — |
| P1-2 | Wrap `/offline` | `frontend/app/offline/page.tsx` | S | High | UIUX-CHROME-002 | — |
| P1-4 | Wrap `/pair` | `frontend/app/pair/page.tsx` | S | High | UIUX-CHROME-004 | — |
| P1-5 | Wrap `/marketplace` | `frontend/app/marketplace/page.tsx` | S | High | UIUX-CHROME-005 | — |
| P1-6 | Wrap `/governance` + clarify vs admin | `frontend/app/governance/page.tsx` | S | High | UIUX-CHROME-006, UIUX-CHROME-024 | — |
| P1-7 | Wrap `/prompts` | `frontend/app/prompts/page.tsx` | S | High | UIUX-CHROME-007 | — |
| P1-8 | Wrap `/terminology` | `frontend/app/terminology/page.tsx` | S | Medium | UIUX-CHROME-008 | — |
| P1-9 | Resolve `/reports` vs `/` duplication | `frontend/app/reports/page.tsx` | S | Medium | UIUX-CHROME-009, UIUX-NAV-003 | — |
| P1-10 | Wrap `/reports/view` | `frontend/app/reports/view/page.tsx` | M | High | UIUX-CHROME-010 | — |
| P1-11 | Wrap `/rulebooks/view` | `frontend/app/rulebooks/view/page.tsx` | S | High | UIUX-CHROME-011 | — |
| P1-12 | Wrap `/rulebooks/editor` (fluid container) | `frontend/app/rulebooks/editor/page.tsx` | M | High | UIUX-CHROME-012 | — |
| P1-13 | Wrap `/audit` | `frontend/app/audit/page.tsx` | S | High | UIUX-CHROME-013 | — |
| P1-14 | Wrap `/audit/verify` and surface in sidebar | `frontend/app/audit/verify/page.tsx` + `frontend/components/shell/nav.config.tsx` | S | High | UIUX-CHROME-014, UIUX-NAV-001 | — |
| P1-15 | Wrap `/analytics` | `frontend/app/analytics/page.tsx` | S | High | UIUX-CHROME-015 | — |
| P1-16 | Wrap `/analytics/quality` and surface in sidebar | `frontend/app/analytics/quality/page.tsx` + nav.config.tsx | S | High | UIUX-CHROME-016, UIUX-NAV-001 | — |
| P1-17 | Wrap `/mobile/dictate` | `frontend/app/mobile/dictate/page.tsx` | S | High | UIUX-CHROME-017 | — |
| P1-18 | Wrap `/mobile/reports/edit` | `frontend/app/mobile/reports/edit/page.tsx` | S | High | UIUX-CHROME-018 | — |
| P1-19 ⚠️ | Wrap `/mobile/reports/sign` (chrome + confirm) | `frontend/app/mobile/reports/sign/page.tsx` | M | Critical | UIUX-CHROME-019 | P3-1 (ConfirmDialog) |
| P1-20 | Wrap `/admin/billing` | `frontend/app/admin/billing/page.tsx` | S | High | UIUX-CHROME-020 | — |
| P1-22 | Wrap `/admin/feature-flags` | `frontend/app/admin/feature-flags/page.tsx` | S | Medium | UIUX-CHROME-022 | — |
| P1-23 | Wrap `/admin/fhir-import` | `frontend/app/admin/fhir-import/page.tsx` | S | High | UIUX-CHROME-023 | — |
| P1-24 ⚠️ | Wrap `/admin/mcp` + surface in sidebar | `frontend/app/admin/mcp/page.tsx` + nav | M | Critical | UIUX-CHROME-025, UIUX-NAV-001 | P3-1 |
| P1-25 | Wrap `/admin/security` | `frontend/app/admin/security/page.tsx` | S | High | UIUX-CHROME-026 | — |
| P1-26 ⚠️ | Wrap `/admin/sso` + surface in sidebar | `frontend/app/admin/sso/page.tsx` + nav | M | Critical | UIUX-CHROME-027, UIUX-NAV-001 | — |
| P1-27 | Codemod: remove inline header markup in remaining pages | `frontend/app/**` | M | Medium | UIUX-CHROME-* | — |

## Phase 2 — Data-state coverage

**Goal:** Every data-driven page uses `<Skeleton/>` + `<EmptyState/>` + `<ErrorState onRetry/>`.

**Acceptance:**
- No more `<div>Loading…</div>` strings.
- Zero-row states show designed empty state with copy from i18n catalogue.
- Fetch failures show `<ErrorState onRetry>`.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P2-1 | Audit + retrofit Skeleton across pages | `frontend/app/**` | L | High | UIUX-A11Y-005 (root cause), UIUX-COPY-006 |
| P2-2 | Retrofit EmptyState w/ catalogue copy | `frontend/app/**` | M | High | UIUX-COPY-006 |
| P2-3 | Retrofit ErrorState w/ i18n + retry | `frontend/app/**` | M | High | UIUX-A11Y-008, UIUX-COPY-001 |
| P2-4 | Add app/loading.tsx, app/error.tsx, app/not-found.tsx | `frontend/app/` | M | High | UIUX-STR-017 |

## Phase 3 — Component primitives

**Goal:** Ship the missing primitives. Eliminate browser dialogs.

**Acceptance:**
- `<Modal>`, `<Tabs>`, `<Toast>`, `<ConfirmDialog>`, `<FormField>` shipped under `frontend/components/ui/`.
- All 5 `window.confirm()/prompt()` call sites replaced.
- Hand-rolled tabs in `/prompts` and `/terminology` use shared `<Tabs>`.

| Ticket | Title | Files | Effort | Severity | Finding IDs | Depends on |
|---|---|---|---|---|---|---|
| P3-1 ⚠️ | Build `<ConfirmDialog>` (with typed-confirmation variant) | `frontend/components/ui/ConfirmDialog.tsx` | M | Critical | UIUX-STR-013 | — |
| P3-2 | Build `<Modal>` primitive | `frontend/components/ui/Modal.tsx` | M | High | UIUX-STR-010 | — |
| P3-3 | Build `<Tabs>` primitive | `frontend/components/ui/Tabs.tsx` | M | High | UIUX-STR-011 | — |
| P3-4 | Build `<Toast>` + `<ToastProvider>` | `frontend/components/ui/Toast.tsx`, `ToastProvider.tsx` | M | High | UIUX-STR-012, UIUX-COPY-005 |
| P3-5 | Build `<FormField>` wrapper | `frontend/components/ui/FormField.tsx` | M | Medium | UIUX-STR-014, UIUX-A11Y-011 | — |
| P3-6 ⚠️ | Replace `confirm()` in ReportClient sign flow | `frontend/app/reports/[id]/ReportClient.tsx` | S | Critical | UIUX-DEST-001 | P3-1 |
| P3-7 ⚠️ | Replace dialogs in `/admin/mcp` | `frontend/app/admin/mcp/page.tsx` | S | Critical | UIUX-DEST-002 | P3-1, P3-2 |
| P3-8 ⚠️ | Replace dialogs in `/admin/validation-packs` | `frontend/app/admin/validation-packs/page.tsx` | S | Critical | UIUX-DEST-003 | P3-1, P3-2 |
| P3-9 ⚠️ | Replace confirm in ProviderOAuthAdminClient | `frontend/app/admin/providers/[id]/ProviderOAuthAdminClient.tsx` | S | Critical | UIUX-DEST-004 | P3-1 |
| P3-10 | Replace prompt in `/prompts` | `frontend/app/prompts/page.tsx` | S | High | UIUX-DEST-005 | P3-2 |
| P3-11 | Migrate `/prompts` + `/terminology` to `<Tabs>` | as named | S | Medium | UIUX-STR-011 | P3-3 |

## Phase 4 — Accessibility hardening

**Goal:** Pass WCAG 2.1 AA static review.

**Acceptance:**
- Skip link present; focus traps verified on modal/menu/drawer.
- All form controls have explicit labels.
- All data tables have `<caption>` + `<th scope>`.
- All status surfaces have appropriate `role` + `aria-live`.
- Sidebar items meet 44×44 touch target.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P4-1 | Focus-trap util + apply to ProfileMenu | `frontend/components/shell/ProfileMenu.tsx` | M | Critical | UIUX-A11Y-002 |
| P4-2 | Focus-trap on MobileDrawer + return on close | `frontend/components/shell/MobileDrawerBackdrop.tsx` + ShellContext | M | Critical | UIUX-A11Y-003 |
| P4-3 | DictateButton reads locale from next-intl | `frontend/components/DictateButton.tsx` | S | Critical | UIUX-A11Y-004 |
| P4-4 | LocalePicker uses router refresh, not reload | `frontend/components/LocalePicker.tsx` | S | High | UIUX-A11Y-007, UIUX-FLOW-F8-001 |
| P4-5 | Add icon/text affordance to severity badges | `frontend/app/globals.css` + badge usages | M | High | UIUX-A11Y-009, UIUX-COPY-008 |
| P4-6 | Audit + fix table semantics | `frontend/app/audit/page.tsx` + analytics + admin/billing + providers + templates | M | High | UIUX-A11Y-010, UIUX-A11Y-015 |
| P4-7 | Audit + fix form labels (date inputs especially) | `frontend/app/admin/settings/page.tsx` + workspace | M | High | UIUX-A11Y-011 |
| P4-8 | Add `:focus-visible` rules to button variants | `frontend/app/globals.css` | S | Medium | UIUX-A11Y-012 |
| P4-9 | Wrap sidebar in `<nav aria-label='Primary'>` | `frontend/components/shell/Sidebar.tsx` | S | Medium | UIUX-A11Y-013 |
| P4-10 | Increase sidebar item min-height to 44px | `frontend/app/shell.css` | S | High | UIUX-STR-016 |

## Phase 5 — Token scale & responsive

**Goal:** All spacing/typography/breakpoint/z-index values map to tokens.

**Acceptance:**
- No magic numbers in stylesheets for spacing/typography.
- All media queries reference `--bp-*` tokens.
- Tokens documented in `docs/02-design/design.md`.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P5-1 | Define spacing scale `--space-1..8` | `frontend/app/globals.css` + design.md | S | High | UIUX-STR-005 |
| P5-2 | Define typography scale `--text-xs..xl` | `frontend/app/globals.css` + design.md | S | High | UIUX-STR-006 |
| P5-3 | Define breakpoint tokens `--bp-sm..xl` | `frontend/app/globals.css` + design.md | S | High | UIUX-STR-007, UIUX-RESP-001 |
| P5-4 | Define z-index scale | `frontend/app/globals.css` + design.md | S | Medium | UIUX-STR-008 |
| P5-5 | Codemod magic numbers → tokens (CSS) | `frontend/app/*.css` | L | Medium | UIUX-STR-005..007 |
| P5-6 | Unify sidebar/PageHeader breakpoints | `Sidebar.tsx`, `PageHeader.tsx`, `shell.css` | M | Medium | UIUX-RESP-002 |
| P5-7 | Reduce `.rp-container` horizontal padding on `<bp-sm` | `frontend/app/shell.css` | S | Medium | UIUX-RESP-003 |
| P5-8 | PageHeader actions wrap below `--bp-sm` | `frontend/components/shell/PageHeader.tsx` | S | Medium | UIUX-A11Y-014 |
| P5-9 | Add horizontal scroll wrappers around long tables | `frontend/app/admin/usage/page.tsx` + others | M | Medium | UIUX-A11Y-015 |

## Phase 6 — Inline-style elimination

**Goal:** Zero `style={{...}}` in app code (excluding measured/dynamic cases under a documented allowlist).

**Acceptance:**
- `style={{` grep returns 0 in `frontend/app/**` and `frontend/components/**` except for a documented allowlist.
- ESLint rule prevents regressions.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P6-1 | Codemod `/templates` (20) | `frontend/app/templates/page.tsx` | M | High | UIUX-STR-009 |
| P6-2 | Codemod `/analytics/quality` (22) | `frontend/app/analytics/quality/page.tsx` | M | High | UIUX-STR-009 |
| P6-3 | Codemod `RulebookDetailClient` (15) | `frontend/app/rulebooks/[id]/RulebookDetailClient.tsx` | M | High | UIUX-STR-009 |
| P6-4 | Codemod `/providers` (12) | `frontend/app/providers/page.tsx` | M | High | UIUX-STR-009 |
| P6-5 | Codemod rulebook editor panels | `frontend/app/rulebooks/editor/*Panel.tsx` | M | High | UIUX-STR-009 |
| P6-6 | Codemod remaining ~20 files | various | M | Medium | UIUX-STR-009 |
| P6-7 | Move Skeleton/ProfileMenu/Breadcrumbs inline styles to classes | as named | S | Medium | UIUX-CMP-001..003 |

## Phase 7 — Copy, microcopy & i18n

**Goal:** Single source of truth for UI copy under next-intl.

**Acceptance:**
- `frontend/lib/copy.ts` (or `messages/<locale>.json`) holds all UI strings.
- No hard-coded English strings in components.
- Backend enum values pass through a label mapper.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P7-1 | Migrate ErrorState defaults to catalogue | `frontend/components/ui/ErrorState.tsx` | S | High | UIUX-COPY-001 |
| P7-2 | Rewrite login token explanation | `frontend/app/login/page.tsx` | S | Medium | UIUX-COPY-002 |
| P7-3 | Enum → label mapper for security/billing | `frontend/lib/labels.ts` (new) | M | High | UIUX-COPY-003 |
| P7-4 | API error → user copy map | `frontend/lib/api.ts` + `lib/errors.ts` | M | High | UIUX-COPY-004 |
| P7-5 | Add success toasts to save/update flows | various | M | High | UIUX-COPY-005 |
| P7-6 | Empty-state copy catalogue per surface | `messages/*.json` | M | Medium | UIUX-COPY-006 |
| P7-7 | Voice guide + CTA codemod | `docs/02-design/copy-voice.md` (new) + various | M | Medium | UIUX-COPY-007 |
| P7-8 | Always-show severity word on badges | various | S | Medium | UIUX-COPY-008 |

## Phase 8 — Governance & tooling

**Goal:** Make regressions impossible.

**Acceptance:**
- ESLint design-lock rule fails CI on inline styles / off-allowlist classes.
- Stylelint runs on every PR and blocks duplicate selectors.
- Storybook + visual regression for shell + primitives.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P8-1 | ESLint: forbid inline `style` prop | `frontend/.eslintrc` | S | High | UIUX-STR-018 |
| P8-2 | ESLint: enforce Container+PageHeader on `app/**/page.tsx` (custom rule) | `frontend/.eslintrc` | M | High | UIUX-CHROME-* |
| P8-3 | Stylelint w/ no-duplicate-selectors + allowed value lists | `frontend/.stylelintrc` | M | Medium | UIUX-STR-001..003, STR-019 |
| P8-4 | Storybook scaffold | `frontend/.storybook/` | M | Medium | UIUX-STR-020 |
| P8-5 | Vitest component tests for primitives | `frontend/components/**/*.test.tsx` | M | Medium | UIUX-STR-020 |
| P8-6 | Playwright visual regression baseline | `frontend/tests/visual/` | L | Medium | UIUX-STR-020, UIUX-SCREEN-001 |
| P8-7 | Fix pnpm wrapper (approve-builds or onlyBuiltDependencies) | `package.json` | S | High | UIUX-STR-015 |
| P8-8 | Document design-lock contract in CONTRIBUTING | `CONTRIBUTING.md` + `docs/02-design/design.md` | S | Medium | UIUX-CHROME-* |

## Phase 9 — IA & flow

**Goal:** Every product page is discoverable and every flow has a confirmation contract.

**Acceptance:**
- All 15 currently-hidden routes are either surfaced in sidebar or intentionally documented as system/utility routes.
- Detail surfaces use `[id]` dynamic segments.
- Locale change preserves state.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P9-1 | Sidebar IA pass: add admin/sso, audit/verify, admin/mcp, analytics/quality | `frontend/components/shell/nav.config.tsx` | S | Critical | UIUX-NAV-001, UIUX-CHROME-014,016,025,027 |
| P9-2 | Migrate detail surfaces from `?id=` to `[id]/page.tsx` | reports, rulebooks | L | Medium | UIUX-NAV-002 |
| P9-3 | Resolve `/reports` vs `/` (redirect chosen non-canonical) | `frontend/app/` | S | Medium | UIUX-NAV-003 |
| P9-4 | Login `?return=` redirect | `frontend/app/login/page.tsx` | S | Medium | UIUX-FLOW-F1-001 |
| P9-5 | Status stepper in PageHeader for report lifecycle | `frontend/components/shell/PageHeader.tsx` + reports | M | Medium | UIUX-FLOW-F2-001 |
| P9-6 | Define + implement AI-mark acknowledge gesture | `frontend/app/reports/[id]/ReportClient.tsx` + globals.css | M | Medium | UIUX-FLOW-F3-001 |
| P9-7 | Undo toast for non-irreversible deletes | toast provider + admin actions | M | High | UIUX-FLOW-F4-001 |

## Phase 10 — Screenshots & visual baseline

**Goal:** Capture the audit baseline for future regression detection.

**Acceptance:**
- 37 routes captured at desktop/tablet/mobile in four states (loading/populated/empty/error) where applicable.
- Baseline stored in `docs/ui-ux-audit/screenshots/`.
- Percy/Chromatic (or local Playwright pixel-diff) wired to CI.

| Ticket | Title | Files | Effort | Severity | Finding IDs |
|---|---|---|---|---|---|
| P10-1 | Stand up backend + frontend with seeded `it` tenant | docs + scripts | M | Low | UIUX-SCREEN-001 |
| P10-2 | Playwright capture script (3 viewports × 4 states) | `frontend/tests/visual/capture.ts` | M | Low | UIUX-SCREEN-001 |
| P10-3 | Commit baseline screenshots | `docs/ui-ux-audit/screenshots/` | S | Low | UIUX-SCREEN-001 |
| P10-4 | Wire visual regression to CI | `.github/workflows/visual.yml` | M | Low | UIUX-STR-020 |

## Cross-cutting tickets

| Ticket | Title | Files | Effort | Severity |
|---|---|---|---|---|
| X-1 | Add a `frontend/docs/CONTRIBUTING-UI.md` mini-guide pointing at design.md | docs | S | Medium |
| X-2 | Add a `pnpm audit-ui` script that runs lint + stylelint + typecheck + a11y smoke | `package.json` | S | Medium |
| X-3 | Add review checklist to `.github/PULL_REQUEST_TEMPLATE.md` for UI changes | template | S | Medium |
| X-4 | Audit `next.config.ts` for `output: 'export'` constraints vs new component primitives | `next.config.ts` | S | Low |

## Suggested merge sequencing

```
Phase 0 (quick wins)
        ↓
Phase 8 (governance scaffolding — lint, scripts) ─┐
        ↓                                          │
Phase 5 (tokens) ──→ Phase 1 (chrome) ──→ Phase 2 (states)
                                                     ↓
                            Phase 3 (primitives) ───┘
                                                     ↓
                                          Phase 4 (a11y) ←┐
                                                     ↓    │
                                          Phase 7 (copy) ─┘
                                                     ↓
                                          Phase 9 (IA & flow)
                                                     ↓
                                          Phase 6 (inline-style elim, enforced by Phase 8)
                                                     ↓
                                          Phase 10 (visual baseline)
```

The diagram is dependency-only; phases shown side-by-side can be staffed
in parallel.
