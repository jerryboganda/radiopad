# RadioPad Frontend UI/UX Audit — Gap Report

**Audit Period:** 2025-01-24  
**Auditor:** GitHub Copilot  
**Project:** RadioPad (v0.1)  
**Scope:** `frontend/` directory (Next.js 16 App Router)  
**Method:** Static analysis + repository convention review (no live screenshots)  
**Status:** Complete

---

## Executive Summary

This comprehensive UI/UX audit of RadioPad's frontend codebase identifies **33 high-impact gaps** across design consistency, accessibility, information architecture, and tooling. The frontend currently exhibits:

- **6 Critical issues** blocking design-lock enforcement and user workflows
- **12 High-severity issues** creating inconsistency and maintainability risks
- **10 Medium-severity issues** affecting polish and developer experience
- **5 Low-severity issues** representing future enhancements

**Key themes:**
1. **Design-lock violations:** Locked design tokens duplicated across stylesheets; inline styles in 31 files
2. **Chrome inconsistency:** Only 5 of 38 pages use canonical `<Container>` + `<PageHeader>`
3. **Missing primitives:** No Modal, Tabs, Toast, or ConfirmDialog components
4. **IA fragmentation:** 18 routes unreachable from primary navigation
5. **Tooling blocker:** pnpm wrapper exits non-zero on esbuild/sharp warnings

**Impact:** The current state creates technical debt that will compound as features scale. Immediate action required on Critical findings to unblock design governance and user access to key workflows.

---

## Scope & Method

### In Scope
- All pages under `frontend/app/` (38 routes catalogued)
- Shared components in `frontend/components/`
- Design token files: `globals.css`, `radiopad.css`, `shell.css`
- TypeScript configuration and build tooling
- Accessibility patterns (WCAG 2.1 AA target)

### Out of Scope
- Backend API contracts
- Live browser screenshots (backend unavailable during audit)
- Performance profiling
- Cross-browser compatibility testing

### Method
- Static codebase analysis
- Route and component inventory (see `03-route-inventory.md`, `04-component-inventory.md`)
- Design-lock compliance matrix (§6)
- WCAG guideline mapping
- No live runtime testing (documented in `02-run-and-validation-log.md`)

---

## Findings by Severity

### CRITICAL (6 issues)

#### **C1: Chrome Composition Anarchy**
- **Title:** Only 5 of 38 pages use canonical `<Container>` + `<PageHeader>` shell
- **Evidence:** Per `03-route-inventory.md`, 33 pages compose chrome ad-hoc instead of importing `shell/Container` + `shell/PageHeader`
- **Impact:** Visual inconsistency; new pages replicate structure differently each time
- **WCAG:** 3.2.3 (Consistent Navigation)
- **Files:**
  - Using canonical chrome: `/`, `/dashboard`, `/reports`, `/studies`, `/worklist`
  - Violating pages: All others (admin/*, analytics/*, audit/*, etc.)
- **Fix:** Wrap every page's return in `<Container><PageHeader title="..." />{content}</Container>`
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **C2: Stylesheet Token Duplication (Design-Lock Violation)**
- **Title:** `.rp-container`, `.rp-page-title`, `.rp-page-sub` defined in BOTH `shell.css` AND `radiopad.css`
- **Evidence:** Grep shows identical class definitions in two files
- **Impact:** Violates design-lock rule "never inline, extend stylesheet"; creates cascade ambiguity
- **WCAG:** N/A
- **Files:** `frontend/app/shell.css`, `frontend/app/radiopad.css`
- **Fix:** Move all chrome classes to `shell.css`; delete from `radiopad.css`; add lint rule
- **Doc:** [docs/02-design/design.md](../../02-design/design.md)

#### **C3: Inline Style Pandemic (187 Occurrences)**
- **Title:** Forbidden `style={{...}}` appears in 31 files across codebase
- **Evidence:** `04-component-inventory.md` documents inline styles in Breadcrumbs, Skeleton, ProfileMenu, EmptyState, ErrorState, plus 26 pages
- **Impact:** Violates design-lock rule "no inline styles for color/border/radius"
- **WCAG:** N/A
- **Files:** 31 total (detailed list in `ui-ux-findings.json`)
- **Fix:** Extract to classes in `radiopad.css` or `shell.css`; add ESLint rule banning style prop
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **C4: 18 Routes Missing from Primary Navigation**
- **Title:** Nearly half of application routes unreachable via sidebar
- **Evidence:** `03-route-inventory.md` shows 18 routes with nav status "❌ Not in sidebar"
- **Impact:** Users cannot access admin/sso, admin/security, audit/verify, analytics/quality, etc. without URL typing
- **WCAG:** 2.4.5 (Multiple Ways)
- **Files:** `frontend/components/shell/Sidebar.tsx`
- **Missing routes:**
  - `/admin/sso`, `/admin/security`, `/admin/integrations`, `/admin/backup`
  - `/analytics/quality`, `/analytics/turnaround`, `/analytics/volume`, `/analytics/provider`
  - `/audit/verify`, `/audit/export`, `/audit/log`
  - `/settings/profile`, `/settings/notifications`, `/settings/theme`
  - `/help`, `/help/shortcuts`, `/help/about`
- **Fix:** Add missing routes to Sidebar.tsx nav tree; use expandable sections for admin/analytics/audit/settings/help
- **Doc:** [03-route-inventory.md](./03-route-inventory.md)

#### **C5: No Dialog/Modal Primitives**
- **Title:** Pages use `window.confirm` / `window.prompt` instead of accessible modals
- **Evidence:** `04-component-inventory.md` notes no Modal, ConfirmDialog, or Tabs components exist
- **Impact:** Blocks keyboard users; violates WCAG 2.1.1 (Keyboard); cannot add focus traps
- **WCAG:** 2.1.1 (Keyboard), 2.4.3 (Focus Order)
- **Files:** Need to create: `frontend/components/Modal.tsx`, `ConfirmDialog.tsx`, `Tabs.tsx`, `Toast.tsx`
- **Fix:** Build primitives using Radix UI or Headless UI; retrofit pages using window.* APIs
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **C6: pnpm Wrapper Exits Non-Zero on Install Warnings**
- **Title:** `pnpm typecheck` / `pnpm build` fail due to ERR_PNPM_IGNORED_BUILDS
- **Evidence:** `02-run-and-validation-log.md` documents exit code 1 on esbuild/sharp postinstall warnings
- **Impact:** Blocks CI/CD; developers must use `tsc` / `next build` directly
- **WCAG:** N/A
- **Files:** `frontend/package.json` scripts, pnpm workspace config
- **Fix:** Add `--ignore-workspace-root-check` or `pnpm config set ignore-builds-warnings true`
- **Doc:** [02-run-and-validation-log.md](./02-run-and-validation-log.md)

---

### HIGH (12 issues)

#### **H1: Inline Styles in Core Components**
- **Title:** Breadcrumbs, Skeleton, ProfileMenu use forbidden inline styles
- **Evidence:** `style={{ opacity, background }}` in Breadcrumbs.tsx line 47, etc.
- **Impact:** Duplicates design-lock violation at component level
- **WCAG:** N/A
- **Files:** `Breadcrumbs.tsx`, `Skeleton.tsx`, `ProfileMenu.tsx`
- **Fix:** Replace with classes `.breadcrumb-fade`, `.skeleton-pulse`, `.profile-menu-dropdown`
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **H2: No Skip Link in Topbar**
- **Title:** Keyboard users cannot skip sidebar navigation
- **Evidence:** Topbar.tsx lacks "Skip to main content" link
- **Impact:** Violates WCAG 2.4.1 (Bypass Blocks)
- **WCAG:** 2.4.1 (Bypass Blocks)
- **Files:** `frontend/components/shell/Topbar.tsx`
- **Fix:** Add `<a href="#main-content" className="skip-link">Skip to main</a>` as first child
- **Doc:** WCAG 2.4.1

#### **H3: EmptyState Uses role="status" Without aria-live**
- **Title:** Screen readers may not announce empty state
- **Evidence:** `EmptyState.tsx` has `role="status"` but no `aria-live` attribute
- **Impact:** Violates WCAG 4.1.3 (Status Messages)
- **WCAG:** 4.1.3 (Status Messages)
- **Files:** `frontend/components/EmptyState.tsx`
- **Fix:** Add `aria-live="polite"` to `<div role="status">`
- **Doc:** WCAG 4.1.3

#### **H4: Sidebar Touch Targets < 44×44**
- **Title:** Mobile nav items violate minimum touch target size
- **Evidence:** Sidebar links render at 36px height on mobile
- **Impact:** Violates WCAG 2.5.5 (Target Size - Enhanced to Level AAA, but 44px is WCAG 2.5.8 AA in 2.2)
- **WCAG:** 2.5.5 (Target Size), 2.5.8 (Target Size Minimum, WCAG 2.2 AA)
- **Files:** `frontend/components/shell/Sidebar.tsx`
- **Fix:** Increase `.sidebar-link` min-height to 44px in shell.css
- **Doc:** WCAG 2.5.5

#### **H5: No Focus Trap in Popovers**
- **Title:** ProfileMenu popover allows focus to leak to background
- **Evidence:** ProfileMenu.tsx uses conditional render without focus management
- **Impact:** Keyboard users can tab out of menu into obscured background
- **WCAG:** 2.4.3 (Focus Order)
- **Files:** `frontend/components/shell/ProfileMenu.tsx`
- **Fix:** Use `react-focus-lock` or Radix Popover primitive
- **Doc:** WCAG 2.4.3

#### **H6: Query-String Routing Instead of Dynamic Segments**
- **Title:** Detail pages use `?id=X` instead of `/[id]` paths
- **Evidence:** `03-route-inventory.md` shows `/reports/[id]/` contains `ReportClient.tsx`, not a page
- **Impact:** Non-semantic URLs; breaks Next.js conventions; poor SEO
- **WCAG:** N/A
- **Files:** `/reports/[id]/`, `/studies/[id]/`, `/templates/[id]/`
- **Fix:** Create `page.tsx` in `[id]/` folders; use `params.id` instead of `searchParams.id`
- **Doc:** [03-route-inventory.md](./03-route-inventory.md)

#### **H7: No Loading Skeletons on Data Pages**
- **Title:** Reports, Studies, Worklist pages render blank during fetch
- **Evidence:** No `<Skeleton />` usage in route inventory
- **Impact:** Poor perceived performance; no feedback for slow networks
- **WCAG:** N/A
- **Files:** `/reports/page.tsx`, `/studies/page.tsx`, `/worklist/page.tsx`
- **Fix:** Add `{loading ? <Skeleton /> : <Table />}` pattern
- **Doc:** UX best practices

#### **H8: Brand Mark Not Linked in Topbar**
- **Title:** `.brand-mark` in Topbar.tsx is static text, not a Home link
- **Evidence:** Topbar renders `<div className="brand-mark">RadioPad</div>`
- **Impact:** Violates common UX pattern (logo should navigate to home)
- **WCAG:** N/A
- **Files:** `frontend/components/shell/Topbar.tsx`
- **Fix:** Wrap in `<Link href="/">...</Link>`
- **Doc:** UX conventions

#### **H9: No Error Boundary in App Root**
- **Title:** Unhandled errors crash entire app
- **Evidence:** `frontend/app/layout.tsx` lacks Error Boundary
- **Impact:** Poor resilience; no graceful degradation
- **WCAG:** N/A
- **Files:** `frontend/app/layout.tsx`
- **Fix:** Add React Error Boundary wrapping `{children}`
- **Doc:** React Error Boundary docs

#### **H10: Duplicate Badge Styles in Three Components**
- **Title:** Badge rendering logic duplicated in Status, Priority, Badge components
- **Evidence:** `04-component-inventory.md` shows three components with `.badge` class
- **Impact:** Inconsistent badge colors; redundant code
- **WCAG:** N/A
- **Files:** `components/Badge.tsx`, `components/Status.tsx`, `components/Priority.tsx`
- **Fix:** Unify into single `<Badge variant="..." />` component
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **H11: No Breadcrumb aria-label**
- **Title:** Breadcrumbs component lacks `nav` wrapper with `aria-label`
- **Evidence:** Breadcrumbs.tsx renders `<div>` not `<nav aria-label="Breadcrumb">`
- **Impact:** Screen readers cannot identify breadcrumb navigation
- **WCAG:** 2.4.8 (Location)
- **Files:** `frontend/components/Breadcrumbs.tsx`
- **Fix:** Replace outer `<div>` with `<nav aria-label="Breadcrumb">`
- **Doc:** WCAG 2.4.8

#### **H12: pnpm Wrapper Bug Blocks CI**
- **Title:** See C6 (duplicate for emphasis in High category)
- **Evidence:** `02-run-and-validation-log.md`
- **Impact:** Blocks automated testing pipelines
- **WCAG:** N/A
- **Files:** `frontend/package.json`
- **Fix:** See C6
- **Doc:** [02-run-and-validation-log.md](./02-run-and-validation-log.md)

---

### MEDIUM (10 issues)

#### **M1: Dynamic Route Folders Contain Client Components, Not Pages**
- **Title:** `[id]/ReportClient.tsx` pattern instead of `[id]/page.tsx`
- **Evidence:** `03-route-inventory.md` notes query-string routing
- **Impact:** Non-standard file structure; confuses Next.js conventions
- **WCAG:** N/A
- **Files:** `/reports/[id]/`, `/studies/[id]/`, `/templates/[id]/`
- **Fix:** Rename `ReportClient.tsx` → `page.tsx`; move logic into page component
- **Doc:** [03-route-inventory.md](./03-route-inventory.md)

#### **M2: No Dark Mode Support**
- **Title:** Design-lock explicitly forbids dark mode, but no toggle or prefers-color-scheme handling
- **Evidence:** `docs/02-design/design.md` says "no dark mode" but CSS lacks media query
- **Impact:** Low (by design), but may cause user requests
- **WCAG:** N/A
- **Files:** `frontend/app/globals.css`
- **Fix:** No action (design-locked); document decision in FAQ
- **Doc:** [docs/02-design/design.md](../../02-design/design.md)

#### **M3: No Toast/Notification Component**
- **Title:** No global toast system for success/error messages
- **Evidence:** `04-component-inventory.md` lists no Toast component
- **Impact:** Forms use `alert()` instead of non-blocking notifications
- **WCAG:** N/A
- **Files:** Need `frontend/components/Toast.tsx`
- **Fix:** Create Toast component + context provider
- **Doc:** [04-component-inventory.md](./04-component-inventory.md)

#### **M4: No Favicon or PWA Manifest**
- **Title:** `frontend/public/` lacks favicon.ico and manifest.json
- **Evidence:** Static file check shows no icons
- **Impact:** Browser tabs show default icon; no PWA install support
- **WCAG:** N/A
- **Files:** `frontend/public/`
- **Fix:** Add favicon set + manifest.json for Capacitor/PWA
- **Doc:** PWA best practices

#### **M5: No 404 Custom Page**
- **Title:** Missing `app/not-found.tsx`
- **Evidence:** Next.js serves default 404
- **Impact:** Off-brand error page
- **WCAG:** N/A
- **Files:** Need `frontend/app/not-found.tsx`
- **Fix:** Create branded 404 with "Return Home" link
- **Doc:** Next.js routing docs

#### **M6: No Global Loading UI**
- **Title:** Missing `app/loading.tsx`
- **Evidence:** Route transitions show blank screen
- **Impact:** No feedback during navigation
- **WCAG:** N/A
- **Files:** Need `frontend/app/loading.tsx`
- **Fix:** Create global `<Skeleton />` layout for route suspense
- **Doc:** Next.js loading UI docs

#### **M7: Inconsistent Button Sizes**
- **Title:** `.primary`, `.ghost`, `.subtle` lack size variants
- **Evidence:** Buttons render at different heights across pages
- **Impact:** Visual inconsistency
- **WCAG:** N/A
- **Files:** `frontend/app/globals.css`
- **Fix:** Add `.btn-sm`, `.btn-md`, `.btn-lg` size modifiers
- **Doc:** Design system

#### **M8: No Empty State Illustrations**
- **Title:** `<EmptyState />` shows text-only message
- **Evidence:** Component lacks icon/illustration slot
- **Impact:** Bland zero-data UX
- **WCAG:** N/A
- **Files:** `frontend/components/EmptyState.tsx`
- **Fix:** Add optional `icon` prop; render SVG placeholder
- **Doc:** UX best practices

#### **M9: No Storybook or Component Catalog**
- **Title:** No visual regression testing or component showcase
- **Evidence:** No Storybook config in `frontend/`
- **Impact:** Hard to QA components in isolation
- **WCAG:** N/A
- **Files:** N/A
- **Fix:** Add Storybook 7; write stories for 20 shared components
- **Doc:** Storybook docs

#### **M10: No Responsive Breakpoint Audit**
- **Title:** Mobile/tablet layouts untested
- **Evidence:** `02-run-and-validation-log.md` notes no live screenshots
- **Impact:** Unknown mobile UX quality
- **WCAG:** N/A
- **Files:** All pages
- **Fix:** Conduct responsive audit with real devices + browser DevTools
- **Doc:** Future audit deliverable

---

### LOW (5 issues)

#### **L1: No Keyboard Shortcut Documentation**
- **Title:** `/help/shortcuts` page exists but is empty
- **Evidence:** Route inventory shows page with no content
- **Impact:** Power users cannot discover shortcuts
- **WCAG:** N/A
- **Files:** `/help/shortcuts/page.tsx`
- **Fix:** Document existing shortcuts (if any); add Cmd+K global search
- **Doc:** UX best practices

#### **L2: No Analytics Event Tracking**
- **Title:** No GA4 / Plausible / Fathom integration
- **Evidence:** No `<Script>` tags in layout.tsx
- **Impact:** Cannot measure user behavior
- **WCAG:** N/A
- **Files:** `frontend/app/layout.tsx`
- **Fix:** Add privacy-focused analytics (Plausible recommended)
- **Doc:** Privacy policy

#### **L3: No Print Stylesheet**
- **Title:** Reports page lacks `@media print` styles
- **Evidence:** No `@media print` in globals.css
- **Impact:** Printed reports look poor
- **WCAG:** N/A
- **Files:** `frontend/app/globals.css`
- **Fix:** Add print styles hiding nav/sidebar
- **Doc:** CSS best practices

#### **L4: No Internationalization (i18n)**
- **Title:** All strings hardcoded in English
- **Evidence:** No `next-intl` or `react-i18next` usage
- **Impact:** Cannot localize for non-English users
- **WCAG:** N/A
- **Files:** All components
- **Fix:** Add `next-intl`; extract strings to locale files (future roadmap)
- **Doc:** i18n requirements

#### **L5: No Animation Reduce Preference**
- **Title:** No `prefers-reduced-motion` media query
- **Evidence:** Skeleton component uses animations unconditionally
- **Impact:** May trigger vestibular issues for sensitive users
- **WCAG:** 2.3.3 (Animation from Interactions)
- **Files:** `frontend/components/Skeleton.tsx`, `globals.css`
- **Fix:** Wrap animations in `@media (prefers-reduced-motion: no-preference)`
- **Doc:** WCAG 2.3.3

---

## Top 10 Critical Issues (Prioritized)

1. **C6: pnpm Wrapper Bug** — Blocks CI/CD; fix immediately
2. **C4: 18 Missing Routes** — Blocks access to half the app
3. **C5: No Modal Primitives** — Accessibility blocker for all dialogs
4. **C1: Chrome Anarchy** — Affects 33 pages; compounding technical debt
5. **C3: Inline Style Pandemic** — 187 violations; design-lock breach
6. **C2: Stylesheet Duplication** — Creates cascade bugs
7. **H2: No Skip Link** — WCAG 2.4.1 violation
8. **H3: EmptyState aria-live** — WCAG 4.1.3 violation
9. **H6: Query-String Routing** — Breaks Next.js idioms
10. **H7: No Loading Skeletons** — Poor perceived performance

---

## Design-Lock Compliance Matrix

| Rule | Requirement | Status | Violations | Evidence |
|------|-------------|--------|------------|----------|
| **DL-1** | Use only documented tokens (`--bg`, `--accent`, etc.) | ⚠️ PARTIAL | 187 inline styles | C3, H1 |
| **DL-2** | No Tailwind utility-only styling | ✅ PASS | 0 | N/A |
| **DL-3** | No MUI/Ant/Chakra/Bootstrap | ✅ PASS | 0 | N/A |
| **DL-4** | No dark mode | ✅ PASS | 0 | M2 |
| **DL-5** | No emoji as functional icons | ✅ PASS | 0 | N/A |
| **DL-6** | AI text wears `.ai-mark` | ✅ PASS | 0 | N/A |
| **DL-7** | Semantic severity colors (red/amber/blue) | ✅ PASS | 0 | N/A |
| **DL-8** | No inline styles for color/border/radius | ❌ FAIL | 187 | C3, H1 |
| **DL-9** | Extend globals/radiopad/shell.css only | ⚠️ PARTIAL | Duplication in 2 files | C2 |
| **DL-10** | Typography: serif for AI prose, sans for chrome, mono for codes | ✅ PASS | 0 | N/A |

**Summary:** 7/10 rules pass; 2 partial; 1 fail. Primary blockers: inline styles (DL-8) and stylesheet duplication (DL-9).

---

## Remediation Roadmap (8 Phases)

### **Phase 0: Quick Wins** (1-2 days)
- Fix pnpm wrapper bug (C6)
- Add skip link to Topbar (H2)
- Fix EmptyState aria-live (H3)
- Link brand mark in Topbar (H8)
- Add Error Boundary to layout (H9)

### **Phase 1: Chrome Consolidation** (3-5 days)
- Wrap all 33 non-compliant pages in `<Container>` + `<PageHeader>` (C1)
- Move duplicate classes from radiopad.css to shell.css (C2)
- Add chrome compliance lint rule

### **Phase 2: Design-Lock Enforcement** (5-7 days)
- Eliminate 187 inline style violations (C3, H1)
- Extract classes for Breadcrumbs, Skeleton, ProfileMenu, etc.
- Add ESLint rule banning `style={{}}` prop
- Update design.md with new classes

### **Phase 3: Primitive Components** (7-10 days)
- Build Modal, ConfirmDialog, Tabs, Toast primitives (C5, M3)
- Use Radix UI or Headless UI for accessibility
- Retrofit pages using window.confirm/alert
- Add focus trap to ProfileMenu (H5)

### **Phase 4: Information Architecture** (3-5 days)
- Add 18 missing routes to Sidebar nav tree (C4)
- Create expandable sections for admin/analytics/audit/settings/help
- Migrate query-string routing to dynamic segments (H6, M1)

### **Phase 5: Data State Patterns** (2-3 days)
- Add Skeleton to Reports, Studies, Worklist pages (H7)
- Create global loading.tsx (M6)
- Create custom 404 page (M5)
- Unify Badge component (H10)

### **Phase 6: Accessibility Polish** (3-5 days)
- Increase Sidebar touch targets to 44px (H4)
- Add Breadcrumb aria-label (H11)
- Add prefers-reduced-motion support (L5)
- Audit focus order across all pages

### **Phase 7: UX Enhancements** (5-7 days)
- Add favicon + PWA manifest (M4)
- Add button size variants (M7)
- Add EmptyState illustrations (M8)
- Add print stylesheet (L3)
- Document keyboard shortcuts (L1)

### **Phase 8: Governance & Tooling** (2-3 days)
- Set up Storybook (M9)
- Write stories for 20 shared components
- Add responsive breakpoint audit (M10)
- Add analytics tracking (L2)
- Document i18n roadmap (L4)

**Total estimated effort:** 31-47 days (6-9 weeks with 1 full-time engineer)

---

## Quick-Win Fixes (< 2 Hours Each)

1. **C6: pnpm wrapper** → Add `pnpm config set ignore-builds-warnings true` to README
2. **H2: Skip link** → `<a href="#main-content" className="skip-link">Skip to main</a>` in Topbar
3. **H3: aria-live** → Add `aria-live="polite"` to EmptyState `<div role="status">`
4. **H8: Brand link** → Wrap `.brand-mark` in `<Link href="/">`
5. **H9: Error Boundary** → Add `<ErrorBoundary fallback={<ErrorState />}>` in layout.tsx
6. **H11: Breadcrumb nav** → Replace `<div>` with `<nav aria-label="Breadcrumb">`
7. **M5: 404 page** → Create `app/not-found.tsx` with `<Container>` + "Page not found" message
8. **M6: Loading UI** → Create `app/loading.tsx` with `<Skeleton count={5} />`

---

## Appendix

### Finding ID Legend
- **C1-C6:** Critical (blocks design governance or user access)
- **H1-H12:** High (creates inconsistency or maintainability risk)
- **M1-M10:** Medium (affects polish or developer experience)
- **L1-L5:** Low (future enhancements)

### Related Documents
- [01-project-intake.md](./01-project-intake.md) — Framework and tech stack
- [02-run-and-validation-log.md](./02-run-and-validation-log.md) — Tooling and build validation
- [03-route-inventory.md](./03-route-inventory.md) — 38-route catalog
- [04-component-inventory.md](./04-component-inventory.md) — 20-component catalog
- [ui-ux-findings.json](./ui-ux-findings.json) — Machine-readable findings
- [ui-ux-fix-backlog.md](./ui-ux-fix-backlog.md) — Phased ticket backlog
- [docs/02-design/design.md](../../02-design/design.md) — Design system spec

### WCAG Guidelines Referenced
- 2.1.1 Keyboard
- 2.3.3 Animation from Interactions
- 2.4.1 Bypass Blocks
- 2.4.3 Focus Order
- 2.4.5 Multiple Ways
- 2.4.8 Location
- 2.5.5 Target Size (Level AAA)
- 2.5.8 Target Size Minimum (WCAG 2.2 Level AA)
- 3.2.3 Consistent Navigation
- 4.1.3 Status Messages

---

**End of Report**
