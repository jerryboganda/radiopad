**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Responsive Audit

## Method

Responsive results are source-level risk findings because rendered viewport testing and screenshots were blocked. Breakpoints were assessed against CSS media queries, fixed widths, grid definitions, table structure, and mobile route layouts.

## Responsive Findings

| Issue ID | Route | Breakpoint | Problem | Severity | Recommendation |
|---|---|---:|---|---|---|
| RSP-001 | Tables across app | 320, 360, 390, 430, 768 | `.rp-table` has no responsive wrapper/card behavior; many pages render dense tables directly. | HIGH | Add canonical responsive table wrapper or mobile card component. |
| RSP-002 | `/reports`, `/validation`, `/audit`, `/templates`, `/providers`, `/admin/security` | 320-430 | Multi-column tables likely overflow phone widths. | HIGH | Add horizontal scroll with visible affordance and/or card rows. |
| RSP-003 | `/` | 320-430 | Search `minWidth:280` and six-column table leave little room after shell padding. | MEDIUM | Full-width filters and card/list rows on mobile. |
| RSP-004 | `/reports/view` | 768, 1024 | Three-pane report workflow stacks below 1100px, creating long tablet workflow. | HIGH | Use tablet two-column layout or sticky right rail. |
| RSP-005 | `/rulebooks` | 320-430 | Inline grid columns can bypass mobile media rule. | HIGH | Move grid sizing to responsive CSS class. |
| RSP-006 | `/rulebooks/editor` | 320, 360, 390, 430, 768 | `.split` fixed 380-460px first column can overflow. | HIGH | Add mobile stacking media query. |
| RSP-007 | `/templates` modal | 320-430 | Fixed 140px/180px inputs in flex row can overflow. | HIGH | Stack form fields or use responsive grid. |
| RSP-008 | `/providers` modal | 320-430 | Modal padding leaves cramped content width. | MEDIUM | Use mobile sheet and reduced gutter. |
| RSP-009 | `/mobile/reports/edit` | 320-430 | Bottom actions can crowd. | MEDIUM | Stack and make primary action full width. |
| RSP-010 | `/mobile/reports/sign` | 320-430 | Export select and long CTA share one row. | MEDIUM | Stack controls and pin primary action. |
| RSP-011 | `/offline` | 320-430 | Draft rows mix long metadata and actions in one flex row. | MEDIUM | Stack metadata/actions. |
| RSP-012 | `/pair` | 320 | Large letter-spaced code can overflow. | MEDIUM | Wrap/segment/reduce code size. |
| RSP-013 | Tauri desktop shell | <900 | Desktop min width is 980px, so desktop cannot exercise mobile drawer behavior. | MEDIUM | Decide whether desktop should test drawer or keep desktop-only min width. |
| RSP-014 | Capacitor shell | 320-430 | No safe-area inset handling found in inspected CSS. | MEDIUM | Add safe-area acceptance tests and CSS where needed. |

## Breakpoint Review

| Breakpoint | Status | Main Risks |
|---:|---|---|
| 320 | High risk | Tables, modals, mobile action rows, pair code, safe-area. |
| 360 | High risk | Same as 320 with slightly more room. |
| 390 | High risk | Tables and mobile action rows remain risky. |
| 430 | Medium/high risk | Tables and modal widths remain risky. |
| 768 | Medium/high risk | Rulebook editor split and dense admin tables. |
| 1024 | Medium | Report editor becomes one-column despite tablet space. |
| 1280 | Lower risk | Canonical shell and report workspace generally fit. |
| 1440 | Lower risk | Dense toolbars remain visually noisy. |
| 1920 | Medium polish | Some dashboards may feel constrained by 1280px container while dense admin views could benefit from `fluid`. |
