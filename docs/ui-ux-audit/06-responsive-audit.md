# 06 — Responsive Design Audit

**Status:** Complete static review (no browser testing)  
**Scope:** Breakpoint consistency, mobile layout collapse, touch targets, viewport stacking  
**Key finding:** 3+ breakpoint values used inconsistently across stylesheets with no token scale

---

## Breakpoints in use

The codebase uses viewport-based media queries across three CSS files without a unified breakpoint token system. Listed below are all observed breakpoints and their usages:

| Breakpoint | Files | Purpose | Issue |
|---|---|---|---|
| `≤640px` | `shell.css` (implicit via no rule shown) | Not explicitly used | — |
| `≤720px` | `radiopad.css` (`.rp-workspace`, `.rp-grid-3`, `.rp-mobile`) | Mobile workspace stacking, stat grid to single column | Mobile-specific but not coordinate with shell drawer |
| `≤760px` | `radiopad.css` (`.rp-grid-3` *alternate rule*) | Billing stat grid stacking | Duplicate rule for same component; different from 720px rule |
| `≤900px` | `shell.css` (topbar menu, drawer transform) | Sidebar → mobile drawer switch, show hamburger button | PRIMARY breakpoint — but not documented |
| `≤1100px` | `shell.css` (collapse button), `radiopad.css` (`.rp-workspace`, `.rp-rewrite-diff`) | Sidebar collapse, workspace 3-pane → 1-pane, editor diff stacking | No coordination with other breakpoints |
| `≥1100px` | `shell.css` (`.rp-sidebar-collapse-btn`) | Show sidebar collapse button (desktop-only) | Inverse query; asymmetric with mobile rules |

**Finding:** Breakpoints are scattered across files with no central token. The primary navigation collapse happens at `≤900px` (via `@media (max-width: 900px)`), but workspace and form layouts use `≤1100px` or `≤720px`, causing **inconsistent stacking order and touch target sizing across viewports**.

---

## Responsive findings

### UIUX-RESP-001: No breakpoint token scale
**Where:** `frontend/app/globals.css` (root tokens)  
**What:** The Open Design token layer defines spacing, colour, and radius tokens but **no breakpoint tokens** (`--bp-xs`, `--bp-sm`, `--bp-md`, `--bp-lg`, `--bp-xl`).  
**Impact:** Hard-coded pixel values in 6+ `@media` queries make it impossible to adjust mobile/tablet/desktop transitions globally. Maintaining responsive layouts requires grep + multiple edits.  
**Fix hint:** Add a breakpoint token scale to `:root` (e.g. `--bp-xs: 480px`, `--bp-sm: 640px`, `--bp-md: 900px`, `--bp-lg: 1100px`, `--bp-xl: 1280px`), then replace all magic numbers.  
**Severity:** HIGH (architectural debt)

### UIUX-RESP-002: Sidebar drawer breakpoint vs page layout breakpoint mismatch
**Where:** `frontend/app/shell.css` (lines 293–412), `frontend/app/radiopad.css` (lines 100–102, 216–218)  
**What:** Sidebar drawer transforms at `@media (max-width: 900px)`, but workspace and stat grids stack at `≤720px` or `≤760px`. A tablet at 800px width **shows the desktop sidebar drawer AND a single-column workspace** — confusing layout.  
**Impact:** At 800px, sidebar takes 248px (width) leaving only ~550px for content, which is tighter than the 720px threshold where workspace collapses. Users see a narrow workspace next to a full sidebar.  
**Fix hint:** Unify on a single mobile breakpoint (recommend `--bp-md: 900px` across shell + page layouts). Update `.rp-workspace` and `.rp-grid-3` to match.  
**Severity:** HIGH (UX inconsistency)

### UIUX-RESP-003: PageHeader action row does not stack on <480px
**Where:** `frontend/components/shell/PageHeader.tsx` (lines 10–25), `frontend/app/shell.css` (lines 346–376)  
**What:** `.rp-page-actions` uses `flex` with `gap: 8px` and `flex: 0 0 auto`. On very narrow viewports (<480px), title + actions row does **not flex-wrap**; actions squash horizontally or overflow.  
**Impact:** On a 320px phone, page title + 2–3 action buttons overflow the viewport width. Buttons become unclickable or text truncates.  
**Fix hint:** Add `@media (max-width: 480px)` to stack `.rp-page-header { flex-direction: column; gap: 16px; }` and `.rp-page-actions { width: 100%; flex-wrap: wrap; }`.  
**Severity:** HIGH (mobile UX)

### UIUX-RESP-004: Container padding too wide on 320px screens
**Where:** `frontend/app/radiopad.css` (lines 15–19), `frontend/app/shell.css` (lines 40–41)  
**What:** Two `.rp-container` rules exist:  
- `radiopad.css`: `padding: 24px 28px 40px` (56px total horizontal)  
- `shell.css`: Padding applied via parent `.rp-shell-content { padding: var(--rp-page-pad-x) ... }` where `--rp-page-pad-x: 32px` (64px total)  

On a 320px device, 56–64px padding leaves only 256–264px for content.  
**Impact:** Text, tables, and inputs are cramped. Line length is too narrow for comfortable reading.  
**Fix hint:** Add `@media (max-width: 480px) { .rp-container { padding: 12px 14px 24px; } .rp-shell-content { padding: var(--rp-page-pad-y) 16px; } }`. Audit also why `.rp-container` rule appears twice (radiopad.css + shell.css).  
**Severity:** MEDIUM (mobile usability)

### UIUX-RESP-005: Tables lack horizontal scroll on narrow viewports
**Where:** `frontend/app/analytics/page.tsx`, `frontend/app/audit/page.tsx`, `frontend/app/providers/page.tsx`, `frontend/app/templates/page.tsx` (all use `.rp-table`)  
**What:** Tables render with `width: 100%` and `border-collapse: collapse` (via `.rp-table` in radiopad.css line 182) but no horizontal scroll wrapper. On <640px, table columns are invisible or text wraps excessively.  
**Impact:** Analytics, audit log, provider list, and template list are unreadable on phones. Users cannot see key columns (status, created, last updated).  
**Fix hint:** Wrap each table in a `<div style={{ overflowX: 'auto', WebkitOverflowScrolling: 'touch' }}>` or add a responsive helper class (e.g. `.rp-table-wrapper { overflow-x: auto; }` with `@media (max-width: 640px)` visible only).  
**Severity:** HIGH (mobile accessibility)

### UIUX-RESP-006: Status banners stick at top without offset on mobile
**Where:** `frontend/components/DesktopStatusBanner.tsx`, `frontend/components/BillingStatusBanner.tsx`, rendered in `frontend/components/shell/AppShell.tsx`  
**What:** Billing and Desktop status banners are rendered above the `<Topbar>` with no mobile-specific positioning. On <900px (mobile drawer mode), banners may overlap or be covered by the fixed drawer when it opens.  
**Impact:** On mobile, opening the sidebar drawer can cover the billing warning (e.g. "Payment overdue").  
**Fix hint:** Add `z-index` management: ensure banners are above `--rp-shell-z-backdrop` (50) but below `--rp-shell-z-drawer` (60) when drawer is open. Or reposition banners inside `.rp-topbar` on mobile.  
**Severity:** MEDIUM (mobile interaction)

### UIUX-RESP-007: Touch targets in Sidebar nav items < 44×44
**Where:** `frontend/app/shell.css` (lines 126–164)  
**What:** Sidebar nav items (`.rp-sidebar-item`) have `padding: 7px 12px` with `font: 500 13px/1.3`, yielding ~27px height. WCAG 2.5.5 (Target Size) and Apple HID recommend **≥44×44px** for mobile touch targets.  
**Impact:** On phones, nav items are too small. Fingers may miss or hit adjacent items. Users accidentally tap wrong sections.  
**Fix hint:** `@media (max-width: 900px) { .rp-sidebar-item { padding: 10px 14px; min-height: 44px; } }`.  
**Severity:** HIGH (accessibility / WCAG 2.5.5)

### UIUX-RESP-008: Sidebar profile trigger also < 44×44
**Where:** `frontend/app/shell.css` (lines 183–197)  
**What:** `.rp-profile-trigger` has `padding: 8px 10px` with `font: 500 12.5px/1.2`. Avatar is 28px + text = ~38px height.  
**Impact:** Same as UIUX-RESP-007 — touch target too small on mobile.  
**Fix hint:** `@media (max-width: 900px) { .rp-profile-trigger { padding: 10px 12px; min-height: 44px; } }`.  
**Severity:** HIGH (accessibility)

### UIUX-RESP-009: No dedicated mobile chrome for `/mobile/*` routes
**Where:** `frontend/app/mobile/dictate/page.tsx`, `frontend/app/mobile/reports/edit/page.tsx`, `frontend/app/mobile/reports/sign/page.tsx`  
**What:** Mobile routes render inside `<AppShell>`, which includes the full sidebar + topbar. The sidebar collapse breakpoint is 900px, so on phones (<900px), the sidebar becomes a drawer. But `/mobile/dictate` needs a simplified, phone-first shell without the drawer chrome.  
**Impact:** Mobile workflows are not truly optimized for phones. Users see the drawer button and topbar breadcrumbs, which clutter a narrow mobile viewport.  
**Fix hint:** Create a `<MobileOnlyShell>` wrapper that renders only a minimal topbar (no breadcrumbs, no drawer button) and stacks content full-width. Detect `pathname.startsWith('/mobile')` in `AppShell` and swap shells.  
**Severity:** MEDIUM (mobile UX polish)

### UIUX-RESP-010: No CSS container queries
**Where:** All responsive rules use `@media (max-width: ...)` viewport queries  
**What:** The codebase does not use CSS Container Queries (level 3). Every layout decision is viewport-based, not component-container-based. If a component needs to know its own width (e.g. a card in a 2-column or 1-column context), it cannot adapt independently.  
**Impact:** Components like tables, cards, and panels cannot adjust their internal layout based on their actual allocated width. This limits reusability and forces page-level media queries.  
**Fix hint:** Not urgent for v0.1, but document as a *future* enhancement: "Use `@supports (container-type: inline-size)` to allow cards to stack internally on narrow parents without page-level queries."  
**Severity:** LOW (future enhancement)

---

## Recommended breakpoint token scale

Add to `frontend/app/globals.css` `:root` block:

```css
/* Mobile-first responsive breakpoints (coordinated scale) */
--bp-xs: 320px;   /* Extra small: phones in portrait */
--bp-sm: 480px;   /* Small: large phones, landscape */
--bp-md: 640px;   /* Medium: tablets in portrait */
--bp-lg: 900px;   /* Large: tablets in landscape / desktop */
--bp-xl: 1100px;  /* Extra large: wide desktop */
```

Then, systematically replace magic numbers:
- `@media (max-width: 900px)` → `@media (max-width: var(--bp-lg))`
- `@media (max-width: 1100px)` → `@media (max-width: var(--bp-xl))`
- `@media (min-width: 1100px)` → `@media (min-width: var(--bp-xl))`
- etc.

Update `docs/02-design/design.md` §"Responsive Design Scale" to document the scale and which breakpoint applies to which shell component.

---

## Per-component responsive review

### Sidebar & Navigation

**Status:** Partially responsive; breakpoint and touch targets need adjustment.

| Component | Mobile (<900px) | Tablet (900–1100px) | Desktop (>1100px) | Issues |
|---|---|---|---|---|
| `.rp-sidebar` | Fixed drawer (slide-in) | Fixed drawer | Sticky column | OK; see UIUX-RESP-007 (touch target) |
| `.rp-sidebar-item` | 27px height | 27px height | 27px height | Too small; needs 44px minimum on mobile (UIUX-RESP-007) |
| `.rp-profile-trigger` | ~38px height | ~38px height | ~38px height | Too small; needs 44px minimum (UIUX-RESP-008) |
| `.rp-sidebar-collapse-btn` | Hidden | Hidden | Visible at ≥1100px | Correct; no issue |

### Topbar

**Status:** Responsive but gaps uncovered.

| Component | Mobile (<900px) | Tablet (900–1100px) | Desktop (>1100px) | Issues |
|---|---|---|---|---|
| `.rp-topbar-menu` (hamburger) | Visible | Visible | Hidden (via `@media (max-width: 900px)`) | Correct; shows drawer button below 900px |
| `.rp-breadcrumbs` | Flex; can truncate | Flex; can truncate | Full width | OK but no skip-to-main link (UIUX-A11Y-001) |
| `.rp-topbar-actions` | Flex | Flex | Flex | OK; no stack behavior needed (actions are icon-only) |

### PageHeader

**Status:** Does not stack on <480px.

| Component | <480px | 480–900px | >900px | Issues |
|---|---|---|---|---|
| `.rp-page-header` | Wraps but actions squash | Wraps; OK | Wraps; OK | Need forced stack at <480px (UIUX-RESP-003) |
| `.rp-page-actions` | Flex, no wrap | Flex, wraps | Flex, wraps | No wrap on smallest phones (UIUX-RESP-003) |

### Containers & Page Padding

**Status:** Padding too wide on 320px.

| Context | Padding | Viewport | Usable width | Issue |
|---|---|---|---|---|
| `.rp-shell-content` | 32px L/R | 320px | 256px | Too narrow; needs 16px on <480px (UIUX-RESP-004) |
| `.rp-container` | 28px L/R | 320px | 264px | Duplicate rules; inconsistent (UIUX-RESP-004) |
| `.rp-mobile` | 16px L/R | 720px | 688px | OK; mobile-optimized |

### Tables

**Status:** No horizontal scroll; unreadable on <640px.

| Page | Table | Columns | Mobile (<640px) | Issue |
|---|---|---|---|---|
| `/analytics` | `.rp-table` | Date, status, count, trend | Invisible/wrapped | Needs scroll wrapper (UIUX-RESP-005) |
| `/audit` | `.rp-table` | Timestamp, action, user, details | Invisible/wrapped | Needs scroll wrapper (UIUX-RESP-005) |
| `/providers` | `.rp-table` | Name, status, last called, actions | Invisible/wrapped | Needs scroll wrapper (UIUX-RESP-005) |
| `/templates` | `.rp-table` | Name, created, status, actions | Invisible/wrapped | Needs scroll wrapper (UIUX-RESP-005) |

### Forms (workspace, editor)

**Status:** OK; section blocks are full-width and wrap naturally.

| Component | Mobile (<900px) | Desktop (>900px) | Issues |
|---|---|---|---|
| `.section-block` | Full width | Full width | None; labels + inputs stack naturally |
| `.rp-workspace` | Single column (via `≤720px`) | 3-pane grid | See UIUX-RESP-002 (misaligned stacking) |
| `.rp-rewrite-diff` | Single column | 2-pane | Stacks correctly at ≤1100px |

### Modals (Not yet implemented)

**Status:** N/A — no modal primitive exists. Browser `window.confirm()` / `window.prompt()` used instead (flagged separately in a11y audit).

### Composer / Editor

**Status:** Not responsive; width-constrained.

| Component | Behavior | Issue |
|---|---|---|
| Report editor (`.split` layout) | Full viewport width | No responsive stacking between editor panes. On <640px, panes are very narrow. Not flagged as urgent (editor is desktop-first workflow). |
| Rulebook editor | Full viewport width | Same as above. |

---

## Summary of recommendations

| Priority | Action | Effort |
|---|---|---|
| **CRITICAL** | Add breakpoint token scale (--bp-xs through --bp-xl) to globals.css | 1–2 hrs |
| **HIGH** | Replace all `@media (max-width: ...)` hard-coded pixels with token variables | 1–2 hrs |
| **HIGH** | Unify sidebar/workspace/form breakpoints on `--bp-lg: 900px` | 1–2 hrs |
| **HIGH** | Increase touch targets to 44×44px on mobile (sidebar, profile menu) | 1 hr |
| **HIGH** | Stack PageHeader actions on <480px | 30 min |
| **HIGH** | Add horizontal scroll wrapper to tables; handle on <640px | 1–2 hrs |
| **MEDIUM** | Reduce container padding on 320px screens (24px → 12px) | 30 min |
| **MEDIUM** | Add mobile-specific banner z-index management | 30 min |
| **MEDIUM** | Create `<MobileOnlyShell>` for `/mobile/*` routes | 2–3 hrs |
| **LOW** | Document CSS Container Queries as future enhancement | 30 min |

---

## Files requiring updates

- `frontend/app/globals.css` — add breakpoint tokens
- `frontend/app/shell.css` — replace magic numbers with tokens
- `frontend/app/radiopad.css` — replace magic numbers; audit duplicate `.rp-container` rule
- `frontend/components/shell/*.tsx` — may need `@media` queries in component styles (if any)
- `docs/02-design/design.md` — document the responsive scale and breakpoint semantics
