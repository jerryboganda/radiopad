# 09 — Frontend Structure & Architecture Audit

**Status:** Complete  
**Owner:** Audit  
**Last Updated:** 2026-05-16

---

## Executive Summary

The RadioPad frontend has significant **structural and architectural inconsistencies** that undermine the design-lock guarantee and create maintenance debt. While the locked design tokens and component classes are sound, **31 files violate the design lock with inline `style={{...}}` props**, **three stylesheets duplicate selector definitions**, the component library is missing critical primitives, and governance tooling (ESLint, Stylelint) is absent. This audit identifies all findings with issue codes and recommends a phased remediation strategy.

---

## 1. CSS Architecture

### 1.1 Stylesheet Inventory

Three stylesheets are imported by `frontend/app/layout.tsx` in this order:

| File | Size | Lines | Purpose |
|---|---|---|---|
| `frontend/app/globals.css` | ~116 KB | 4,556 | Token layer + component classes (`.panel`, `.section-block`, `.composer`, `.msg`, `.finding`, `.ai-mark`, `.primary`, `.ghost`, etc.). |
| `frontend/app/shell.css` | ~13 KB | 507 | Sidebar + topbar + page-header shell chrome. |
| `frontend/app/radiopad.css` | ~19 KB | 573 | Page-specific helpers and fallback styles. |

**Load order:** Correct (tokens first, page helpers last). Specificity is flat and predictable.

### 1.2 Duplicate Selector Violations — UIUX-STR-001 to UIUX-STR-003

Three selectors are defined **in both `shell.css` AND `radiopad.css`**, creating a single source of truth violation:

#### UIUX-STR-001: `.rp-container`
- **Location A:** `shell.css` line 338 (`max-width: 1280px; padding: 16px; margin: 0 auto;`)
- **Location B:** `radiopad.css` line 15 (identical rule)
- **Severity:** HIGH — future maintainers cannot tell which version is canonical.
- **Fix:** Delete the `radiopad.css` version; all `.rp-container` usage must route through `shell.css`.

#### UIUX-STR-002: `.rp-page-title`
- **Location A:** `shell.css` line 357 (`font-size: 1.75rem; font-weight: 600; …`)
- **Location B:** `radiopad.css` line 21 (identical rule)
- **Severity:** HIGH
- **Fix:** Delete `radiopad.css` version.

#### UIUX-STR-003: `.rp-page-sub`
- **Location A:** `shell.css` line 364 (`font-size: 0.875rem; color: var(--text-soft); …`)
- **Location B:** `radiopad.css` line 28 (identical rule)
- **Severity:** HIGH
- **Fix:** Delete `radiopad.css` version.

**Root cause:** When `shell.css` was added, page-specific rules were not pruned from `radiopad.css`. This pattern suggests accidental duplication during refactoring.

**Recommended governance:** Add a linting step that detects selector duplication across stylesheets (e.g. using CSS AST tooling).

### 1.3 Token Coverage Matrix

The design lock specifies "locked" tokens. This matrix assesses coverage:

| Token Family | Status | Coverage | Notes |
|---|---|---|---|
| **Palette** | ✅ COMPLETE | 100% | Primary (`--bg #faf9f7`), accent (`--accent #c96442`), semantic families (green/blue/purple/red/amber with -bright, -soft, -text variants). |
| **Typography** | ⚠️ PARTIAL | ~40% | `.serif`, `.sans`, `.mono` font-family vars defined; **missing** granular scale tokens (`--text-xs`, `--text-sm`, `--text-base`, `--text-lg`, `--text-xl`). Pages use inline `font-size` instead. |
| **Spacing** | ❌ MISSING | 0% | No `--space-1` through `--space-8` (4px, 8px, 12px, 16px, 24px, 32px, 48px, 64px) tokens. Pages use magic numbers (6, 8, 12, 16, 20, 24, 32) in inline styles. |
| **Radii** | ✅ COMPLETE | 100% | `--radius-1 (6px)`, `--radius-2 (10px)`, `--radius-3 (14px)`, `--radius-pill (9999px)`. All border-radius rules use tokens. |
| **Shadows** | ✅ COMPLETE | 100% | `--shadow-xs`, `--shadow-sm`, `--shadow-md`, `--shadow-lg`. All shadows use tokens. |
| **Breakpoints** | ❌ MISSING | 0% | No CSS custom properties for breakpoints. `next.config.ts` uses hard-coded `1440px` (desktop), `1024px` (tablet), `768px` (mobile). Media queries in stylesheets use hard-coded pixel values; pages use inline media queries. |
| **Z-Index** | ❌ MISSING | 0% | No `--z-*` tokens. Layers stack with magic numbers (1000, 9999, 10000). Difficult to manage layering hierarchy. |
| **Semantic Severities** | ✅ COMPLETE | 100% | Blocker → red, Warning → amber, Info → blue. Consistently applied via `.rp-status.*` tone classes. |

**Recommended actions:**
1. **Add spacing scale:** Define `--space-1` through `--space-8` in `globals.css` and require pages to use them instead of inline numbers.
2. **Add typography scale:** Define `--text-xs`, `--text-sm`, `--text-base`, `--text-lg`, `--text-xl` (with line-height) and migrate inline `font-size` rules.
3. **Add breakpoint tokens:** Define `--breakpoint-mobile (480px)`, `--breakpoint-tablet (768px)`, `--breakpoint-desktop (1024px)`.
4. **Add z-index scale:** Define `--z-dropdown (100)`, `--z-sticky (200)`, `--z-modal (1000)`, `--z-tooltip (1100)`.

### 1.4 Class Naming Ambiguity — UIUX-STR-004

Two class names refer to similar concepts:
- `.panel` — legacy from `globals.css` (generic container with border and shadow)
- `.rp-panel` — new from `shell.css` (sidebar panel, topbar panel)

**Severity:** MEDIUM — confusion during development; unclear which class to use in new components.

**Recommendation:** 
1. Audit all `.panel` usage in code.
2. Rename `.panel` to `.rp-legacy-panel` (or deprecate entirely if unused).
3. Document: `.rp-panel` is for shell components; all new panels use `.rp-panel`.

---

## 2. Inline-Style Violations (Design Lock Breach)

The RadioPad design lock requires: **"No inline `style={{...}}` for colours, borders, radii."** Inline styles for layout (e.g. `position: absolute`, `flex-basis`) are acceptable when no class fits.

### 2.1 Violation Summary

**31 files contain `style={{...}}` props; ~187 total occurrences detected via grep.**

Top 10 offenders by count:

| File | Count | Severity | Notable violations |
|---|---|---|---|
| `frontend/app/templates/page.tsx` | 20 | HIGH | Inline `display`, `backgroundColor`, `borderRadius`, `padding`, `color`. |
| `frontend/app/analytics/quality/page.tsx` | 22 | HIGH | Inline `background`, `border`, `borderRadius`, `color`, `padding`. |
| `frontend/app/rulebooks/page.tsx` | 10 | HIGH | `backgroundColor`, `color`, `padding`. |
| `frontend/app/providers/page.tsx` | 12 | HIGH | `color`, `backgroundColor`, `padding`, `margin`, `display`. |
| `frontend/app/rulebooks/[id]/RulebookDetailClient.tsx` | 15 | HIGH | `background`, `padding`, `margin`, `color`, `fontSize`. |
| `frontend/components/Skeleton.tsx` | 1 | MEDIUM | Layout style (`display: 'flex', gap: 12, padding: '8px 0'`) — acceptable; typography (`borderBottom: '1px solid var(--border-soft)'`) should be `.skeleton-divider`. |
| `frontend/components/shell/Breadcrumbs.tsx` | 1 | LOW | Layout: `display: 'inline-flex', alignItems: 'center', gap: 6` — no class for this pattern. |
| `frontend/components/shell/ProfileMenu.tsx` | 1 | LOW | Layout: `padding: '4px 6px'` on avatar — tight spacing, layout-only. |
| `frontend/app/reports/[id]/RewriteStylePanel.tsx` | 8 | HIGH | `backgroundColor`, `color`, `padding`, `margin`. |
| `frontend/app/rulebooks/editor/*.tsx` (5 files) | 9 | HIGH | Inline background, padding, color in editor sub-panels. |

**Total violations flagged: UIUX-STR-005 through UIUX-STR-035** (31 files).

### 2.2 Remediation Strategy

**Phase 1 (Critical):** 
- Files with `backgroundColor`, `color`, `border` inline → convert to class-based styling.
- Create ESLint rule `react/forbid-dom-props` with `forbid: ['style']` to prevent regressions.

**Phase 2 (Important):**
- Files with layout-only styles (flex, position, dimensions) → extract to new CSS classes (e.g. `.flex-row-center`, `.flex-col-gap-8`).
- Update style guide: "Inline styles are forbidden except for dynamic layout (position, left/top, width/height computed at runtime)."

**Phase 3 (Governance):**
- Pre-commit hook: `eslint --rule="react/forbid-dom-props"` on `*.tsx` files.
- PR review checklist: "No inline color, background, border, radius, or font-size."

---

## 3. Component Architecture

### 3.1 Shell & Page Composition Adoption

The design-lock specifies that **every page should be wrapped in `<AppShell> → <Container> → <PageHeader>`** for consistent chrome (breadcrumbs, page title, actions row).

**Audit result:** Only **5 of 38 routes** use this pattern correctly.

#### Compliant pages (5):
- `frontend/app/page.tsx` (dashboard)
- `frontend/app/validation/page.tsx` (validation)
- `frontend/app/providers/page.tsx` (providers list)
- `frontend/app/templates/page.tsx` (templates list)
- `frontend/app/rulebooks/page.tsx` (rulebooks list)

#### Non-compliant pages (33):
- `frontend/app/reports/**` (6 pages) — custom chrome, no `<PageHeader>`.
- `frontend/app/admin/**` (7 pages) — sparse breadcrumbs, no page header.
- `frontend/app/workspace/**` (4 pages) — inline titles, no `<Container>`.
- `frontend/app/settings/**` (3 pages) — no structure.
- `frontend/app/auth/**, frontend/app/onboarding/**, frontend/app/billing/**` (13 pages) — outside `<AppShell>` (intentional for auth flows, but worth documenting).

**Severity:** MEDIUM — lack of consistency leads to visual fragmentation and harder maintenance.

**Recommended action:**
1. Update `CONTRIBUTING.md` to mandate `<Container>` + `<PageHeader>` for all product pages (exclude auth/onboarding/billing flows).
2. Create a page template scaffold: `frontend/app/[feature]/page.tsx.template`.
3. Issue: UIUX-STR-036

### 3.2 Missing Primitives — Critical Gaps

The component library lacks **four critical primitives** that lead to reinvention and A11y regressions:

#### UIUX-STR-037: No `<Modal>` primitive
- **Impact:** Rulebook editor save dialog, provider OAuth confirmation, and prompt-block creation use `window.prompt()` or custom panels without focus trap, backdrop semantics, or Escape handling.
- **Severity:** HIGH
- **Recommendation:** Create `<Modal open title onClose onConfirm primaryAction="Save">` component with:
  - Focus trap (FocusLock or manual).
  - Backdrop with `role="presentation"` and click-to-dismiss.
  - Escape key handling.
  - Full ARIA attributes (`aria-modal="true"`, `aria-labelledby`, `aria-describedby`).

#### UIUX-STR-038: No `<Tabs>` primitive
- **Impact:** `/prompts` and `/terminology` ship **two different tab implementations** (one uses button groups, one uses radio buttons). No consistent keyboard navigation.
- **Severity:** MEDIUM
- **Recommendation:** Create `<Tabs selectedId onChange>` wrapper with:
  - ARIA `role="tablist"` / `role="tab"` / `role="tabpanel"`.
  - Arrow-key navigation.
  - Consistent styling.

#### UIUX-STR-039: No `<Toast>` / success notification
- **Impact:** Pages reuse `setError()` state for success messaging. No toast queue, auto-dismiss, or semantic distinction.
- **Severity:** LOW
- **Recommendation:** Create `<Toast variant="success" | "warning" | "error" | "info" message onDismiss />` component with portal rendering and auto-dismiss timer.

#### UIUX-STR-040: No `<ConfirmDialog>` primitive
- **Impact:** Destructive actions (delete rulebook, revoke pairing) rely on `window.confirm()` browser dialogs, which break the design language and fail screen-reader UX.
- **Severity:** HIGH
- **Recommendation:** Create `<ConfirmDialog open onConfirm onCancel severity="danger" | "warning">` with semantic styling and full A11y support.

### 3.3 Generic Primitives Status

| Primitive | Exists? | Status | Used in pages | Recommendation |
|---|---|---|---|---|
| `<Button>` | ❌ | Pages use `.primary`, `.ghost`, `.subtle` classes | ~95% of buttons use classes (correct). | No action needed. |
| `<Input>` / `<TextField>` | ❌ | Pages use `<input>` + inline styling | ~70% of forms have custom styles | Create `.input-text`, `.input-checkbox`, `.input-radio`, `.input-select` classes or a minimal `<Input>` wrapper. |
| `<Card>` | ❌ | Pages use `.section-block` or custom divs | ~80% use `.section-block` (good). | No action needed. |
| `<Modal>` | ❌ | Pages use `window.prompt()`, custom panels | ❌ Severe | **CREATE — UIUX-STR-037**. |
| `<Tabs>` | ❌ | Pages reinvent (button groups or radio buttons) | 2 pages inconsistent | **CREATE — UIUX-STR-038**. |
| `<Toast>` | ❌ | Pages reuse `setError()` | ~5 pages | **CREATE — UIUX-STR-039**. |
| `<ConfirmDialog>` | ❌ | Pages use `window.confirm()` | ~4 pages | **CREATE — UIUX-STR-040**. |
| `<Select>` / dropdown | ⚠️ | PARTIAL | `<LocalePicker>` (role="listbox" missing) | Audit for A11y; add ARIA attributes. |

---

## 4. i18n Architecture

### 4.1 next-intl Setup (Correct)

- **Locale negotiation:** `frontend/middleware.ts` via `accept-language` header and cookie.
- **Client-side:** `frontend/lib/i18n.ts` fallback locale + `IntlBoundary` wrapper in `layout.tsx`.
- **Supported locales:** English (en), Spanish (es), French (fr), German (de), Italian (it), Portuguese (pt).

### 4.2 Hardcoded English Bypasses — UIUX-STR-041 to UIUX-STR-042

Two components ignore the negotiated locale and hard-code English copy:

#### UIUX-STR-041: `ErrorState.tsx`
- **Lines 18–22:** `title = title || 'Something went wrong'`, `retryLabel = retryLabel || 'Try again'`
- **Severity:** MEDIUM
- **Fix:** Extract to `frontend/lib/copy.ts` and use `useTranslations('components.errorState')` from `next-intl`.

#### UIUX-STR-042: `DictateButton.tsx`
- **Line 15:** `lang: 'en-US'` hard-coded regardless of negotiated locale.
- **Severity:** MEDIUM
- **Fix:** Derive from `useLocale()` and map to Web Speech API language codes.

### 4.3 Missing Copy Catalog — UIUX-STR-043

RadioPad has **no centralized copy catalogue** (e.g. `frontend/lib/copy.ts`). Each page defines its own labels, error messages, and button text inline. This makes:
- Translation sweeps difficult (no single list to hand to translators).
- Consistency hard to enforce (same concept has 3–5 wordings across pages).
- i18n integration impossible without refactoring.

**Recommendation:** Create `frontend/lib/copy.ts` exporting:
```typescript
export const COPY = {
  common: { save: 'Save', cancel: 'Cancel', delete: 'Delete', … },
  pages: {
    rulebooks: { title: 'Rulebooks', newLabel: 'New Rulebook', … },
    …
  }
};
```

Then use `next-intl` JSON files (`messages/en.json`, `messages/es.json`, etc.) and `useTranslations()` to pull from the centralized messages object.

---

## 5. Tooling & Quality Gates

### 5.1 TypeScript & Build (Functional, Workaround Required)

| Tool | Status | Notes |
|---|---|---|
| `pnpm typecheck` | ⚠️ BROKEN | Wrapper script invokes pnpm which re-runs install and exits 1 due to benign `ERR_PNPM_IGNORED_BUILDS` for esbuild/sharp. Underlying `tsc` passes cleanly. **Workaround:** `.\node_modules\.bin\tsc -b --noEmit` (verified PASS). |
| `pnpm build` | ⚠️ BROKEN | Same root cause. **Workaround:** `.\node_modules\.bin\next build`. |
| `pnpm dev` | ✅ FUNCTIONAL | Starts dev server at `http://localhost:3000`. No build issues. |
| TypeScript version | ✅ v5.9.3 strict | No errors, no warnings across all page/component files. |

**Recommended action:** Document workaround in `CONTRIBUTING.md`; file issue with pnpm maintainers (or update to `pnpm@9.0+` if available).

### 5.2 Missing Linting — UIUX-STR-044

No ESLint rules enforce the design lock:
- ❌ No rule to forbid `style={{...}}` props (should be `react/forbid-dom-props`).
- ❌ No rule to enforce class-based styling (custom rule needed).
- ❌ No rule to catch hardcoded English copy (custom rule needed).

**Recommendation:**
1. Install `eslint-plugin-react` and add to `frontend/.eslintrc.json`:
   ```json
   {
     "rules": {
       "react/forbid-dom-props": ["error", { "forbid": ["style"] }],
       "no-restricted-syntax": ["error", {
         "selector": "CallExpression[callee.name='require'][arguments.0.value=/next-intl/]",
         "message": "Use 'use client' + useTranslations() instead of direct imports."
       }]
     }
   }
   ```
2. Add Stylelint for CSS:
   ```bash
   npm install --save-dev stylelint stylelint-config-standard
   ```
3. Lint on pre-commit: `husky` + `lint-staged`.

### 5.3 Missing Component Tests & Storybook — UIUX-STR-045

**Status:** No component tests, no Storybook, no Chromatic/Percy integration.

| Tool | Purpose | Status | Impact |
|---|---|---|---|
| Vitest (already installed) | Unit + component tests | Not used for components | A11y regressions go undetected until manual testing. |
| Storybook | Component catalogue & isolated UI development | Not installed | Inconsistency in page-local reinvention (buttons, forms, modals). |
| Chromatic / Percy | Visual regression testing | Not installed | CSS changes can introduce subtle layout shifts unnoticed. |

**Recommendation (phased):**
1. **Phase 1:** Set up Vitest + React Testing Library for `frontend/components/**` (all ~20 shared components must have tests).
2. **Phase 2:** Add Storybook v8 with Chromatic CI integration.
3. **Phase 3:** Create stories for all primitives (current + new: Modal, Tabs, Toast, ConfirmDialog).

### 5.4 Static Export & Dev Server Proxy

- **Build target:** `output: 'export'` in `next.config.ts` → static HTML/JS/CSS bundle (no Node server at runtime).
- **Dev proxy:** `rewrites` rule proxies `/api/*` to `http://127.0.0.1:7457` (ASP.NET backend).
- **Data-driven pages:** All data routes call `api.ts` client.

**Implication:** Screenshots can only be captured with the backend running (see `11-screenshot-index.md`).

---

## 6. File & Folder Organization

### 6.1 Route File Colocation Pattern

All routes follow the correct pattern:
```
frontend/app/
  └── feature/
      ├── page.tsx (route handler)
      ├── [id]/
      │   ├── page.tsx (detail route)
      │   └── DetailClient.tsx (client component, imported by page.tsx)
      └── editor/
          ├── page.tsx (editor route)
          └── EditorClient.tsx (client component)
```

✅ **Finding:** Correct separation of route handlers (server) and client components.

### 6.2 Component Organization

```
frontend/components/
  ├── shell/ (AppShell, Sidebar, Topbar, etc.)
  ├── ui/ (StatusBadge, Skeleton, EmptyState, ErrorState)
  └── <root> (LocalePicker, IntlBoundary, DictateButton, etc.)
```

✅ **Finding:** Clear separation of concerns.

### 6.3 Opportunities for Improvement — UIUX-STR-046

1. **Create `frontend/components/forms/`** — group `<Input>`, `<Select>`, `<Checkbox>`, `<Radio>`, `<FormField>` primitives (currently none exist).
2. **Create `frontend/components/dialogs/`** — group `<Modal>`, `<ConfirmDialog>`, `<Toast>` (missing, see §3.2).
3. **Create `frontend/lib/hooks/`** — export reusable hooks (e.g. `usePagination`, `useDebounce`, `useLocalStorage` for shell collapse state).
4. **Create `frontend/lib/copy.ts`** — centralized English copy (see §4.3).

---

## 7. Accessibility & Semantic HTML

### 7.1 Issues Requiring Attention

Several A11y issues span the structure layer:

| Issue | Severity | Count | Fix |
|---|---|---|---|
| No skip link (`role="skip"` or `#main-content` anchor) | MEDIUM | 1 | Add `<a href="#main-content" className="skip-link">Skip to content</a>` in Topbar; add `id="main-content"` to main page container. **UIUX-CMP-001** |
| Modal without focus trap (rulebook editor, OAuth, prompt dialog) | HIGH | 3 | Create `<Modal>` primitive with FocusLock. **UIUX-STR-037** |
| `window.confirm()` for destructive actions | HIGH | 4 | Create `<ConfirmDialog>` primitive. **UIUX-STR-040** |
| Missing `aria-live` on state changes (drawer open/close) | MEDIUM | 2 | Add `aria-live="polite"` to state containers. **UIUX-CMP-023** |
| `role="alert"` without `aria-live` on EmptyState | MEDIUM | 1 | Change `role="status"` + add `aria-live="polite"`. **UIUX-CMP-010** |

---

## 8. Recommendations Summary

### Critical (Must fix before GA):
1. **UIUX-STR-001 to 003:** Remove duplicate selectors from `radiopad.css`.
2. **UIUX-STR-005 to 035:** Enforce design lock via ESLint `react/forbid-dom-props` rule and remediate all 31 files.
3. **UIUX-STR-037:** Create `<Modal>` primitive.
4. **UIUX-STR-040:** Create `<ConfirmDialog>` primitive.

### Important (Before next release):
5. **UIUX-STR-038:** Create `<Tabs>` primitive.
6. **UIUX-STR-043:** Build centralized copy catalogue + i18n integration.
7. **UIUX-STR-044:** Add ESLint + Stylelint rules to CI.
8. **UIUX-STR-045:** Set up component tests + Storybook.
9. Add spacing, typography, breakpoint, and z-index token families (§1.3).
10. Mandate `<Container>` + `<PageHeader>` adoption across remaining 33 pages (§3.1).

### Nice to have:
11. **UIUX-STR-046:** Reorganize component folders (`forms/`, `dialogs/`).
12. Deprecate `.panel` class; standardize on `.rp-panel` (UIUX-STR-004).
13. Document workaround for `pnpm typecheck`/`pnpm build` in `CONTRIBUTING.md`.

---

## 9. Governance & Enforcement

**Who enforces these changes?**

1. **PR review checklist** (link to `CONTRIBUTING.md`):
   - [ ] No new inline `style={{...}}` for colours, borders, radii.
   - [ ] All new pages use `<Container>` + `<PageHeader>` (or documented exemption).
   - [ ] All new strings go to `frontend/lib/copy.ts` and use `useTranslations()`.
   - [ ] ESLint and Stylelint pass locally.

2. **CI gates:**
   - `pnpm typecheck` (with workaround documented).
   - `eslint frontend/ --rule="react/forbid-dom-props"` (new).
   - `stylelint frontend/**/*.css` (new).
   - Vitest `frontend/components/**` (new).

3. **Quarterly design-lock audit:**
   - Re-run structure audit (grep for inline styles, duplicate selectors).
   - Review new pages for shell adoption.
   - Spot-check token usage.

---

## 10. References

- **Design spec:** `docs/02-design/design.md`
- **Canonical stylesheet:** `frontend/app/globals.css` (tokens), `frontend/app/shell.css` (shell)
- **Component inventory:** `docs/ui-ux-audit/04-component-inventory.md`
- **RadioPad tech stack & conventions:** `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `CONTRIBUTING.md`
- **Design lock statement:** `docs/02-design/design.md` § "Design System Lock" + `AGENTS.md` § 0

---

**End of audit. See `11-screenshot-index.md` for visual verification protocol.**
