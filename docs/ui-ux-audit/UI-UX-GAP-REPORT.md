**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# UI/UX Gap Report

## 1. Executive Summary

RadioPad has a clear locked design system and a strong canonical sidebar shell, but the frontend is not yet consistently governed by that system. The strongest areas are the token foundation, shell primitives, report-domain concepts, and existing Empty/Error/Skeleton components. The weakest areas are responsive tables, accessibility primitives for drawers/modals/popovers/forms, dense clinical/admin action hierarchy, and native mobile/desktop shell recovery states.

This audit is evidence-backed but source-level only. Rendered screenshots and browser walkthroughs were blocked; see `02-run-and-validation-log.md`.

| Metric | Count |
|---|---:|
| Pages/routes discovered | 38 |
| Pages/routes audited | 38 source-level |
| Shared/page-local components reviewed | 25+ |
| Critical issues | 0 |
| High issues | 24 |
| Medium issues | 45 |
| Low issues | 4 |

## 2. Audit Coverage

| Area | Status | Notes |
|---|---|---|
| Route discovery | Complete | Based on `frontend/app`, navigation config, route helpers, and dynamic client wrappers. |
| Page-by-page visual audit | Partial | All routes covered source-level; rendered screenshot review blocked. |
| Responsive audit | Partial | Source/CSS breakpoint risk review; no live viewport screenshots. |
| Accessibility audit | Partial | Manual source review; no axe/browser run. |
| Component audit | Complete | Shared shell/UI primitives and page-local components inventoried. |
| UX flow audit | Complete source-level | Major report, rulebook, admin, mobile, offline, and native-shell flows reviewed. |
| Copy/microcopy audit | Partial | English/source strings sampled across main surfaces. |
| Frontend structure audit | Complete source-level | Design-system, route, state, and CSS drift documented. |
| Native shell audit | Partial | Tauri/Capacitor config/source reviewed; native runtime not launched. |

## 3. Severity Definitions

| Severity | Meaning |
|---|---|
| Critical | Blocks core user flow, breaks layout badly, creates severe accessibility/usability failure |
| High | Strongly harms UX, trust, responsiveness, or accessibility |
| Medium | Noticeable quality issue that should be fixed |
| Low | Polish improvement or consistency cleanup |

## 4. Top Critical Issues

No critical issues were confirmed from source-only evidence. Rendered testing may surface critical layout or keyboard issues, especially in mobile drawer, report editor, modals, and dense tables.

## 5. High Priority Issues

| ID | Route/Component | Problem | Impact | Recommended Fix |
|---|---|---|---|---|
| UI-035 | App-wide tables | `.rp-table` lacks responsive behavior. | Many data pages likely overflow mobile/tablet. | Add canonical responsive table/card primitive. |
| A11Y-02 | Mobile sidebar | Closed drawer can remain focusable; no focus trap/return. | Keyboard and screen-reader users can get lost. | Implement accessible drawer primitive. |
| A11Y-04 | Provider/template modals | Dialog semantics/focus management incomplete. | Modal workflows are not reliably keyboard accessible. | Add shared accessible Dialog. |
| A11Y-01 | Forms | Labels not consistently programmatically associated. | Screen-reader/form usability failure. | Add IDs/htmlFor or wrapped labels. |
| UI-005 | Report editor | Dense equal-weight toolbar. | Primary clinical path is unclear. | Group actions and reserve one primary workflow step. |
| UI-004 | Report editor | Tablet workflow stacks into long single column. | Validation/actions can disappear from context. | Use tablet two-column/sticky rail. |
| UI-014 | Rulebook editor | Fixed split likely overflows small screens. | Editor unusable on mobile/tablet. | Add responsive split stacking. |
| UI-018 | Template modal | Fixed-width row fields overflow phones. | CRUD modal becomes cramped/broken. | Stack fields on mobile. |
| UI-019 | Prompt Studio | Generic/legacy classes outside canonical shell. | Visual inconsistency and missing styles. | Rebuild with locked page primitives. |
| NS-003 | Mobile offline drafts | PHI-bearing drafts can fall back to Preferences/localStorage. | Mobile offline storage trust risk. | Require encrypted storage or explicit platform controls. |

## 6. Page-by-Page Findings

See `05-page-by-page-audit.md` for full issue table.

| Route | Main Issues | Severity | Fix Priority |
|---|---|---|---|
| `/login` | Full shell before auth, developer copy. | High | P1 |
| `/` | Mobile table/filter pressure. | Medium | P2 |
| `/reports` | Legacy/raw table and missing states. | High | P1 |
| `/reports/view` | Dense toolbar, tablet collapse, accessibility issues. | High | P1 |
| `/validation` | Table overflow, technical copy. | High | P1 |
| `/audit` | Dense table, no filters, plain empty state. | High | P1 |
| `/analytics` | Active state/copy drift. | Medium | P2 |
| `/rulebooks` | Inline responsive grid risk. | High | P1 |
| `/rulebooks/editor` | Fixed split overflow. | High | P1 |
| `/templates` | Table/modal responsive and dialog issues. | High | P1 |
| `/prompts` | Design-system drift, browser prompt, tabs. | High | P1 |
| `/providers` | Eight-column table and modal accessibility. | High | P1 |
| `/mobile/*` | Action rows, permission fallbacks, native shell gaps. | High | P1 |
| `/admin/*` | Dense tables, orphaned routes, status/copy drift. | High | P1/P2 |

## 7. Component-Level Findings

| Component | Issue | Used In | Recommendation |
|---|---|---|---|
| `AppShell` | Wraps auth/mobile/pairing routes unconditionally. | All routes | Add shell variants or route group layouts. |
| `Sidebar` | Mobile focus/inert behavior incomplete. | Shell | Add accessible drawer behavior. |
| `PageHeader`/`Container` | Underused. | Many pages | Standardize page chrome. |
| `EmptyState`/`ErrorState`/`Skeleton` | Underused. | Data pages | Require for loading/empty/error states. |
| `ReportClient` | Too large/dense. | `/reports/view` | Extract toolbar, export menu, validation rail, signatures. |
| `.rp-table` | No responsive behavior. | Many pages | Add `ResponsiveTable`. |
| Modals | Local implementations vary. | Providers/templates | Add shared Dialog. |
| Mobile clients | Full shell/action rows not thumb-optimized. | `/mobile/*` | Add mobile shell/action primitives. |

## 8. Responsive Design Findings

See `06-responsive-audit.md`.

Highest-risk breakpoints are 320, 360, 390, 430, and 768. The main root cause is dense tables and fixed/flex rows without a responsive wrapper.

## 9. Accessibility Findings

See `07-accessibility-audit.md`.

Top accessibility gaps:

1. Programmatic form labels.
2. Drawer focus/inert/focus return.
3. Modal focus trap and semantics.
4. Skip navigation.
5. Button/link accessible names.
6. Live status/error announcements.
7. Contrast verification for accent/faint text.

## 10. UX Flow Findings

See `08-interaction-flow-audit.md`.

Top UX gaps:

- Report editor action hierarchy.
- Native `confirm()` and browser `prompt()` usage.
- Disabled export actions without contextual explanation.
- Weak progress feedback for validation.
- Missing post-export next steps on mobile.
- Unconfirmed offline draft discard.

## 11. Visual Design System Findings

| Area | Current Problem | Design-System Recommendation |
|---|---|---|
| Tables | No responsive app-wide pattern. | Add `ResponsiveTable`/`TableFrame` and document it. |
| Page chrome | Manual headings/containers remain. | Use `Container` + `PageHeader` everywhere. |
| States | Loading/empty/error states drift. | Use shared state components. |
| Buttons/links | Anchor CTAs do not inherit button-only CSS. | Add `ButtonLink` or generic button class. |
| Tokens | `--green-soft` undefined. | Use documented semantic tokens only. |
| Panels | `panel`/`panel-header` classes appear. | Replace with `rp-panel` patterns. |

## 12. Copy and Microcopy Findings

See `10-copy-microcopy-audit.md`.

Primary copy issue: several pages expose implementation or development language to clinical/admin users. Status terms also need centralization.

## 13. Root Cause Analysis

- Missing responsive table and mobile data-list primitive.
- Inconsistent adoption of existing shell/page/state components.
- Local modal/popover implementations rather than accessible shared primitives.
- Dense domain workflows evolved inside large page components.
- Static-export query route strategy is not documented.
- Native desktop/mobile shell states lack a unified UX model.
- Browser/visual/accessibility regression testing is not currently available.

## 14. Recommended Fix Roadmap

### Phase 1: Critical Layout and Navigation Fixes

Responsive tables, auth shell exception, route IA cleanup, report editor toolbar grouping, rulebook editor split stacking.

### Phase 2: Responsive and Mobile Fixes

Template/provider modals, mobile edit/sign actions, offline draft rows, pair code wrapping, safe-area/keyboard acceptance criteria.

### Phase 3: Accessibility Fixes

Labels, drawer, Dialog, skip link, popover/menu semantics, live regions, contrast verification, touch targets.

### Phase 4: Component Standardization

`Container` + `PageHeader` everywhere, state primitives everywhere, `StatusBadge`/status glossary, `ButtonLink`, `ResponsiveTable`.

### Phase 5: Visual Polish

Prompt Studio rebuild, analytics active states, marketplace tabs, copy cleanup, dense admin table detail drawers.

### Phase 6: Regression Testing and Governance

Add browser screenshot workflow, axe checks, contrast checks, responsive route matrix, and native-shell UX acceptance tests.

## 15. Developer Backlog

See `ui-ux-fix-backlog.md`.

## 16. Acceptance Criteria for Future UI Quality

- Every route uses the canonical shell/page chrome or a documented shell variant.
- Every data-driven page has loading, empty, error, and success states.
- Every table is usable at 320, 360, 390, 430, 768, 1024, 1280, 1440, and 1920.
- Every modal/drawer/popover has accessible focus management and keyboard behavior.
- Every form control has a programmatic label.
- Every icon-only/busy button has a descriptive accessible name.
- No user-facing copy references internal commands, PRDs, headers, or development setup unless in an explicit admin/developer help panel.
- Native desktop/mobile shell states have visible, actionable recovery UX.
- Screenshots and accessibility checks are run before UI changes merge.
