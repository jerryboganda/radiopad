**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Frontend Structure Audit

| Issue ID | File/Component | Problem Type | Severity | Problem | Recommendation |
|---|---|---|---|---|---|
| STR-001 | `frontend/app/radiopad.css`, table pages | Missing primitive | HIGH | `.rp-table` is shared but has no responsive wrapper/component. | Add canonical `ResponsiveTable`/`TableFrame` and use across app. |
| STR-002 | Multiple pages | State pattern drift | HIGH | Shared `Skeleton`, `EmptyState`, `ErrorState`, `StatusBadge` are underused. | Require these for data-driven pages. |
| STR-003 | Multiple pages | Page chrome drift | MEDIUM | Pages mix `Container`/`PageHeader` with manual `rp-container`/`rp-page-title`. | Standardize page skeleton. |
| STR-004 | `ReportClient.tsx` | Component complexity | HIGH | Main report editor is a large component with many responsibilities and actions. | Extract toolbar, validation rail, export menu, signature list, AI panels. |
| STR-005 | `prompts/page.tsx`, `analytics/quality/page.tsx` | Undefined/generic classes | HIGH | Uses `panel`, `panel-header`, `pane` classes outside canonical page patterns. | Replace with locked `rp-panel` and shell primitives. |
| STR-006 | `radiopad.css` | Token mismatch | MEDIUM | `.banner.ok` references undefined `--green-soft`. | Use `--green-border`. |
| STR-007 | Links with button classes | CSS selector mismatch | HIGH | CSS styles `button.primary-ghost`, not anchors with `className="primary-ghost"`. | Add `ButtonLink` or generic `.button-like` styles in design doc. |
| STR-008 | Admin-looking routes | Auth/IA clarity | MEDIUM | Many admin pages have no frontend route guard and are not all in sidebar. | Clarify IA and rely on backend authorization; expose proper denied states. |
| STR-009 | Route wrappers | Route consistency | MEDIUM | Query wrapper routes and dynamic-named component folders can confuse contributors. | Document static-export route strategy. |
| STR-010 | App Router | Missing boundaries | MEDIUM | No route-level `loading.tsx`, `error.tsx`, or `not-found.tsx` observed. | Add section-level boundaries. |
| STR-011 | Native bridges | Shell state visibility | MEDIUM | Tauri/backend/biometric/offline states are mostly global/passive. | Create clear native-shell status and recovery patterns. |
| STR-012 | Inline layout styles | Maintainability | MEDIUM | Fixed widths/grids live inline, bypassing responsive CSS governance. | Extract reusable locked classes and document them. |

## Root Causes

1. Responsive table pattern is missing.
2. Data-state primitives exist but are not consistently applied.
3. Several pages still use legacy or generic layout classes.
4. Large page components make interaction and accessibility behavior harder to standardize.
5. Native desktop/mobile shell states do not have a single UX pattern.
6. Route strategy is optimized for static export but not clearly documented for contributors.
