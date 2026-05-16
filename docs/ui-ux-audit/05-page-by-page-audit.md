**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Page-by-Page Audit

## Coverage Summary

This was a source-level page audit. Rendered browser screenshots were blocked; see `02-run-and-validation-log.md` and `11-screenshot-index.md`.

| Route | Page Purpose | Main Audit Notes | Audit Status |
|---|---|---|---|
| `/` | Reports dashboard | Good use of canonical state primitives; mobile table/filter overflow risk. | Audited |
| `/login` | Sign-in/dev identity | Wrapped in full app shell; developer-oriented copy. | Audited |
| `/pair` | Device pairing | Pair code overflow risk; copy references route not found. | Audited |
| `/reports` | Legacy report list | Duplicates dashboard concept; raw table and missing states. | Audited |
| `/reports/view` | Report editor | Dense clinical toolbar, tablet collapse, modal/popover/a11y issues. | Audited |
| `/validation` | Validation center | Table overflow and technical design copy. | Audited |
| `/audit` | Audit log | Dense table, plain empty row, no search/filter. | Audited |
| `/audit/verify` | Audit verifier | Hash table overflow risk. | Audited |
| `/analytics` | Analytics dashboard | Active states may not style; copy uses implementation language. | Audited |
| `/analytics/quality` | Quality dashboard | Uses undefined/generic `panel` class. | Audited |
| `/rulebooks` | Rulebook library | Inline grid columns can defeat mobile stacking. | Audited |
| `/rulebooks/view` | Rulebook detail | Rollback toolbar may wrap poorly. | Audited |
| `/rulebooks/editor` | Rulebook editor | Fixed split layout likely overflows mobile/tablet widths. | Audited |
| `/templates` | Template admin | Table and modal form rows need responsive/accessibility work. | Audited |
| `/prompts` | Prompt Studio | Uses generic pane/panel classes and browser prompt. | Audited |
| `/marketplace` | Marketplace | Tab semantics and form grouping need improvement. | Audited |
| `/terminology` | Terminology browser | Result rows likely cramped on phones. | Audited |
| `/providers` | Provider admin | Eight-column table and modal ergonomics risks. | Audited |
| `/offline` | Offline drafts | Draft rows and discard flow need mobile/confirmation work. | Audited |
| `/copilot` | Copilot user page | API-driven user page; included in route/component coverage. | Audited |
| `/governance` | Legacy governance | Duplicate governance surface with admin route. | Audited |
| `/mobile/dictate` | Mobile dictation | Permission fallback and command banner state need polish. | Audited |
| `/mobile/reports/edit` | Mobile report edit | Final action row crowding risk. | Audited |
| `/mobile/reports/sign` | Mobile sign/export | CTA row and post-export next steps need work. | Audited |
| `/admin/*` | Admin surfaces | Several dense tables, orphaned routes, role-gating inconsistencies. | Audited |

## Page Visual Findings

| Issue ID | Route | Viewport | Component/Area | Severity | Category | Problem | Recommendation |
|---|---|---:|---|---|---|---|---|
| UI-001 | `/login` | All | App shell | HIGH | NAVIGATION | Login renders inside sidebar/topbar shell before authentication. | Add route group/layout exception or reduced auth shell. |
| UI-002 | `/reports` | 320-768 | Reports table | HIGH | RESPONSIVE | Raw table has no loading/error/empty primitives and likely overflows phones. | Use dashboard pattern or responsive table wrapper/cards. |
| UI-003 | `/` | 320-430 | Filters/table | MEDIUM | RESPONSIVE | Search `minWidth:280` and six-column table compress at phone widths. | Make filters full-width and provide mobile report cards or table scroll. |
| UI-004 | `/reports/view` | 768-1024 | Three-pane editor | HIGH | LAYOUT | Clinical workflow collapses to one long column below 1100px. | Add tablet two-column/sticky validation/action rail. |
| UI-005 | `/reports/view` | All | Toolbar/CTAs | HIGH | BUTTONS | Too many equal-weight actions dilute primary clinical path. | Group draft/validate/export actions and reserve one primary next step. |
| UI-006 | `/reports/view` | All | Rewrite popover | MEDIUM | MODALS | Popover z-index can sit below sticky topbar. | Use managed overlay layer above topbar. |
| UI-007 | `/reports/view` | 320-430 | Signatures list | MEDIUM | RESPONSIVE | Email/date flex rows can crowd on phones. | Stack rows with labels on mobile. |
| UI-008 | `/validation` | 320-430 | Validation table | HIGH | RESPONSIVE | Five-column table has no overflow/card fallback. | Add responsive wrapper or cards. |
| UI-009 | `/audit` | 320-430 | Audit table | HIGH | TABLES | Hash/detail log table is too dense for mobile. | Use responsive log cards and expandable details. |
| UI-010 | `/audit/verify` | 320-430 | Hash verifier | MEDIUM | TABLES | Expected/computed hash table lacks responsive wrapper. | Wrap horizontally or stack comparisons. |
| UI-011 | `/analytics/quality` | All | Panels | HIGH | DESIGN_SYSTEM | Uses `panel` class not defined in locked CSS. | Replace with `rp-panel`. |
| UI-012 | `/analytics` | All | Date tabs/buttons | MEDIUM | DESIGN_SYSTEM | `ghost active` state has no matching CSS rule found. | Use `.rp-tabs` or add documented active style. |
| UI-013 | `/rulebooks` | 320-430 | Split grid | HIGH | RESPONSIVE | Inline grid columns can override mobile stacking. | Move to responsive class. |
| UI-014 | `/rulebooks/editor` | 320-768 | `.split` editor | HIGH | RESPONSIVE | Fixed `minmax(380px,460px) 1fr` split lacks media query. | Add responsive stacking. |
| UI-015 | `/rulebooks/editor` | All | Nested scroll panes | MEDIUM | INTERACTION | Multiple viewport-height scroll regions can hide context/actions. | Add sticky actions or simplify scroll containment. |
| UI-016 | `/rulebooks/view` | 320-430 | Rollback controls | MEDIUM | SPACING | `marginLeft:auto` toolbar item can detach when wrapped. | Use separate responsive toolbar row. |
| UI-017 | `/templates` | 320-430 | Template table | HIGH | RESPONSIVE | Six-column table plus action buttons has no mobile pattern. | Add responsive wrapper/cards/action menu. |
| UI-018 | `/templates` | 320-430 | Modal section rows | HIGH | FORMS | Fixed 140/180px fields in one row likely overflow phones. | Stack fields or use responsive grid. |
| UI-019 | `/prompts` | All | Prompt Studio shell | HIGH | DESIGN_SYSTEM | Uses `pane`, `panel`, and `panel-header` rather than canonical page shell/panels. | Rebuild with `Container`, `PageHeader`, and `rp-panel`. |
| UI-020 | `/prompts` | 320-430 | Split/tables | HIGH | RESPONSIVE | Golden-case tables need mobile overflow. | Wrap tables and keep stacked layout. |
| UI-021 | `/marketplace` | All | Tabs | MEDIUM | ACCESSIBILITY | Buttons behave like tabs without tab semantics. | Use ARIA tabs and `.rp-tabs`. |
| UI-022 | `/marketplace` | 320-430 | Submit form | MEDIUM | FORMS | Form labels sit in list structure, not field pattern. | Use `.section-block`/field grouping. |
| UI-023 | `/terminology` | 320-430 | Result rows | MEDIUM | RESPONSIVE | Three-column flex rows likely cramped. | Stack Code/Term/Synonyms on mobile. |
| UI-024 | `/providers` | 320-768 | Provider table | HIGH | RESPONSIVE | Eight-column table with badges/buttons has no mobile wrapper. | Responsive table/cards. |
| UI-025 | `/providers` | 320-430 | Provider modal | MEDIUM | MODALS | Backdrop padding leaves cramped modal width on phones. | Use mobile sheet/full-height dialog. |
| UI-026 | `/admin/security` | 320-768 | Security tables | HIGH | TABLES | Multi-column tables with JSON details can overflow. | Add overflow wrapper and expandable JSON. |
| UI-027 | `/admin/billing` | 320-430 | Usage/invoice rows | MEDIUM | RESPONSIVE | Flex pseudo-table rows compress numeric columns. | Stack as mobile cards. |
| UI-028 | `/admin/mcp` | All | Success banner | MEDIUM | COLOR | `.banner.ok` references undefined `--green-soft`. | Use `--green-border`. |
| UI-029 | `/mobile/dictate` | 320-430 | Command banner | MEDIUM | COLOR | Same `.banner.ok` token problem. | Use fixed success token. |
| UI-030 | `/mobile/reports/edit` | 320-430 | Bottom actions | MEDIUM | BUTTONS | Action row can crowd at phone width. | Stack full-width buttons or sticky primary action. |
| UI-031 | `/mobile/reports/sign` | 320-430 | Format/export row | MEDIUM | BUTTONS | Select and long CTA share one row. | Stack select and primary button. |
| UI-032 | `/offline` | 320-430 | Draft rows | MEDIUM | RESPONSIVE | Metadata/actions can crowd in flex rows. | Stack metadata and actions. |
| UI-033 | `/pair` | 320 | Pair code | MEDIUM | TYPOGRAPHY | 32px monospace code with wide letter spacing can overflow. | Reduce/wrap code at <=360px. |
| UI-034 | Links as buttons | All | Anchor CTAs | HIGH | DESIGN_SYSTEM | CSS targets `button.primary-ghost`; anchors may render as plain links. | Add documented anchor button classes or `ButtonLink`. |
| UI-035 | Tables app-wide | 320-768 | `.rp-table` | HIGH | RESPONSIVE | Shared table has no built-in responsive behavior. | Introduce canonical `ResponsiveTable`/`TableFrame`. |
