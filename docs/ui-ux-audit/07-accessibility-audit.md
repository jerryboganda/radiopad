**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Accessibility Audit

## Method

This was a manual source-level accessibility audit. Automated axe/browser checks were not available because no browser automation tool was available and pnpm scripts were blocked before execution.

## Accessibility Findings

| Issue ID | Route | Element | WCAG Area | Severity | Problem | Recommendation |
|---|---|---|---|---|---|---|
| A11Y-01 | `/login`, `/reports/view`, multiple forms | Labels + controls | 1.3.1, 3.3.2 Labels | HIGH | Visible labels are often siblings without `htmlFor`/`id`, so controls may not be programmatically named. | Add stable IDs and `htmlFor`, or wrap controls inside labels. Audit all `.section-block label` usage. |
| A11Y-02 | App shell | Mobile sidebar/drawer | Keyboard/focus management | HIGH | Closed mobile sidebar remains off-screen but focusable; no focus trap/return behavior found. | Make closed drawer inert, move focus on open, trap focus, close on Escape, return focus to menu button. |
| A11Y-03 | Global shell | Skip navigation | 2.4.1 Bypass blocks | MEDIUM | No skip link is rendered before sidebar/topbar navigation. | Add "Skip to main content" targeting `<main id="main-content">`. |
| A11Y-04 | `/providers`, `/templates` | Modals | Dialog semantics/focus | HIGH | Provider modal lacks dialog semantics and focus management; template modal has partial semantics but no trap/return. | Add shared accessible Dialog primitive. |
| A11Y-05 | Profile menu, report rewrite menu | Menus/popovers | ARIA menu pattern | MEDIUM | `role="menu"` containers include native form controls or lack full menu keyboard behavior. | Use popover semantics for mixed controls or implement full menu pattern. |
| A11Y-06 | Global buttons/placeholders | Contrast | 1.4.3 Contrast | MEDIUM | White text on `--accent: #c96442` is about 3.9:1 for normal text; placeholder/faint text is light. | Recheck contrast and adjust text-bearing button colors/tokens. |
| A11Y-07 | `/templates`, `/reports/view` | Icon/busy buttons | Accessible names | MEDIUM | Buttons named only "x" or "..." are unclear to users and assistive tech. | Add explicit `aria-label` and descriptive busy text. |
| A11Y-08 | Report editor/admin pages | Status/error messages | 4.1.3 Status messages | MEDIUM | Save, validation, loading, success, and errors are not consistently live-announced. | Add polite `role=status` and assertive alert regions. |
| A11Y-09 | `/prompts` | Tabs | ARIA tabs | MEDIUM | `role=tab` buttons lack `id`/`aria-controls`; panels lack `tabpanel` semantics. | Complete tab pattern or use plain buttons. |
| A11Y-10 | Tables | Action columns | Table semantics | LOW | Some action columns use empty `<th>`. | Add visible or `aria-label="Actions"` header and `scope="col"`. |
| A11Y-11 | Shell/buttons | Touch targets | Mobile usability | LOW | Some controls appear below 44x44 guidance. | Increase mobile hit areas while keeping desktop compact. |

## Positive Patterns

- Shell renders a main landmark.
- Sidebar has `aria-label` and active links use `aria-current="page"`.
- Many SVG icons are decorative with `aria-hidden`.
- Focus-visible styles exist for buttons, sidebar links, and menu items.
- Reduced-motion rules exist for drawer/backdrop and skeleton animation.
- `ErrorState` and `EmptyState` expose `role="alert"` / `role="status"`.
- Copy-to-RIS exposes success/error through live roles.

## Recommended Accessibility Validation

Run browser-based validation when tooling is available:

- Keyboard walkthrough: shell, drawer, profile menu, rewrite menu, report editor, provider/template modals.
- axe/Playwright scans on `/`, `/login`, `/reports/view?id=...`, `/validation`, `/providers`, `/templates`, `/prompts`, `/admin/settings`.
- Automated contrast scan against rendered CSS variables.
- Mobile viewport keyboard/focus testing at 320px and 390px.
