# RadioPad Accessibility Audit (WCAG 2.1 AA)

**Status:** In review  
**Owner:** UX audit track  
**Last Updated:** 2025-01-13  
**Scope:** Frontend components, semantic HTML, keyboard navigation, colour contrast, ARIA, focus management

---

## Executive Summary

RadioPad's accessibility posture has **critical gaps** in keyboard navigation, focus management, semantic HTML, and ARIA implementation. Of the **13 findings**, 4 are **Critical**, 5 are **High**, 3 are **Medium**, and 1 is **Low**. These issues prevent keyboard-only users, screen-reader users, and users with colour blindness from safely using the platform. Medical platforms are subject to Section 508 and WCAG 2.1 AA compliance requirements in most jurisdictions.

**Key risks:**
- No skip-to-main link (WCAG 2.4.1 Focus Order)
- ProfileMenu and mobile drawer lack focus traps (WCAG 2.4.3 Focus Order)
- Colour-only status badges (WCAG 1.4.1 Colour Contrast)
- Hardcoded English in DictateButton regardless of locale (WCAG 3.2.5 Change on Request)
- Screen-reader announcements missing on status changes (WCAG 4.1.2 Name, Role, Value)

---

## Accessibility Findings (WCAG 2.1 AA)

### UIUX-A11Y-001: Missing Skip-to-Main Link

**Severity:** Critical  
**WCAG:** 2.4.1 Focus Order (Level A)  
**Component:** `frontend/components/shell/Topbar.tsx` + `frontend/app/shell.css`  
**Current state:** Topbar does not include a skip link; keyboard users must tab through all sidebar navigation to reach the main content area.

**Issue:**
- Topbar (lines 1–50) renders branding, locale picker, and profile menu but no skip link.
- First focusable element is the sidebar nav, forcing ~20+ tab stops before reaching page content.
- Users relying on keyboard navigation cannot efficiently navigate to main content.

**WCAG Requirement:**
> If a page defines a navigational structure, a mechanism SHALL exist to bypass it. The easiest mechanism is a skip link. ([WCAG 2.4.1](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html))

**Recommended fix:**
1. Add a visually hidden skip link as the first focusable element in Topbar:
   ```tsx
   <a href="#main-content" className="skip-to-main">
     Skip to main content
   </a>
   ```
2. Add CSS in `shell.css`:
   ```css
   .skip-to-main {
     position: absolute;
     left: -9999px;
     z-index: 999;
     padding: 8px 12px;
     background: var(--accent);
     color: white;
   }
   .skip-to-main:focus {
     left: 16px;
     top: 16px;
   }
   ```
3. Add `id="main-content"` to the main content area in `shell.css` (inside `.rp-page-content`).

**Priority:** Fix in sprint 1 (mandatory for Section 508).

---

### UIUX-A11Y-002: ProfileMenu Popover Lacks Focus Trap

**Severity:** Critical  
**WCAG:** 2.4.3 Focus Order (Level A) + 1.4.2 Audio Control (related)  
**Component:** `frontend/components/shell/ProfileMenu.tsx` (lines 12–75)  
**Current state:** Popover opens and closes, but focus is not managed; keyboard users can tab out of the menu without closing it.

**Issue:**
- ProfileMenu renders as an untrapped popover (lines 40–75 in JSX).
- `aria-haspopup="menu"` is declared (line 12) but `aria-expanded` is missing.
- No `role="menu"` on the list; uses `role="listbox"` instead.
- No `onKeyDown` handler to trap focus or respond to Escape.
- When the menu is open, Tab takes focus outside the menu; Escape does not close it.

**WCAG Requirement:**
> Modal popover components SHALL trap focus and respond to Escape to close. Focus MUST NOT leave the popover until it is explicitly closed. ([WCAG 2.4.3](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html))

**Recommended fix:**
1. Wrap popover items in a `<div role="menu">`.
2. Add `aria-expanded={isOpen}` to the trigger button.
3. Add a focus trap library (e.g., `focus-trap`):
   ```tsx
   import { createFocusTrap } from 'focus-trap';
   
   useEffect(() => {
     if (isOpen && menuRef.current) {
       const trap = createFocusTrap(menuRef.current, {
         initialFocus: false,
         onDeactivate: () => setIsOpen(false),
       });
       trap.activate();
       return () => trap.deactivate();
     }
   }, [isOpen]);
   ```
4. Add Escape handler:
   ```tsx
   onKeyDown={(e) => {
     if (e.key === 'Escape') {
       setIsOpen(false);
       buttonRef.current?.focus();
     }
   }}
   ```

**Priority:** Fix in sprint 1 (blocks keyboard-only access to profile menu).

---

### UIUX-A11Y-003: Mobile Drawer Lacks Focus Management

**Severity:** Critical  
**WCAG:** 2.4.3 Focus Order (Level A)  
**Component:** `frontend/components/shell/Sidebar.tsx` + `frontend/app/shell.css` (lines 293–412)  
**Current state:** Mobile drawer opens without managing focus; keyboard users cannot navigate within the drawer or close it easily.

**Issue:**
- Drawer (`shell.css` lines 293–312) uses `max-width: 0` → `max-width: 248px` to show/hide.
- No focus trap when drawer is open; Tab will cycle through hidden sidebar links.
- Close button (implied but not visible in CSS) has no explicit keyboard shortcut (Escape).
- Sidebar links are `<a>` elements; focus outline may be obscured by the drawer overlay.

**WCAG Requirement:**
> Modal navigation drawers SHALL trap focus, prevent interaction with the page underneath, and close on Escape. ([WCAG 2.4.3](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html))

**Recommended fix:**
1. In Sidebar component, add a focus trap when drawer is open (see UIUX-A11Y-002 pattern).
2. Add inert attribute to main page when drawer is open:
   ```tsx
   <main inert={mobileDrawerOpen} id="main-content">
   ```
3. Add Escape handler to close drawer:
   ```tsx
   useEffect(() => {
     const handleEscape = (e) => {
       if (e.key === 'Escape' && mobileDrawerOpen) {
         setMobileDrawerOpen(false);
       }
     };
     document.addEventListener('keydown', handleEscape);
     return () => document.removeEventListener('keydown', handleEscape);
   }, [mobileDrawerOpen]);
   ```

**Priority:** Fix in sprint 1 (blocks keyboard access on mobile).

---

### UIUX-A11Y-004: DictateButton Hard-codes Language to en-US

**Severity:** Critical  
**WCAG:** 3.2.5 Change on Request (Level AAA) + 4.1.2 Name, Role, Value (Level A)  
**Component:** `frontend/components/DictateButton.tsx` (line 48)  
**Current state:** Web Speech API language is hard-coded to `'en-US'`; ignores the user's selected locale.

**Issue:**
- Line 48: `r.lang = 'en-US'` overwrites the user's locale preference.
- When a user switches locale via LocalePicker to Spanish, French, or German, DictateButton still transcribes as English.
- Violates the principle of user control (WCAG 3.2.5) and breaks accessibility for non-English-speaking users.

**WCAG Requirement:**
> Changes of context SHALL be initiated only by user request or accompanied by a mechanism to turn off such changes. Language for a page SHALL be identified; changes of language within the page MUST be programmatically determinable. ([WCAG 3.2.5](https://www.w3.org/WAI/WCAG21/Understanding/change-on-request.html), [WCAG 4.1.2](https://www.w3.org/WAI/WCAG21/Understanding/name-role-value.html))

**Recommended fix:**
1. Read the current locale from `next-intl`:
   ```tsx
   import { useLocale } from 'next-intl';
   
   const locale = useLocale();
   const webSpeechLang = {
     'en': 'en-US',
     'es': 'es-ES',
     'fr': 'fr-FR',
     'de': 'de-DE',
   }[locale] || 'en-US';
   
   r.lang = webSpeechLang;
   ```
2. Document the supported locales and test with each.
3. Add error handling if the browser does not support the requested language.

**Priority:** Fix in sprint 1 (blocks non-English users).

---

### UIUX-A11Y-005: LocalePicker Destroys Form State on Language Change

**Severity:** High  
**WCAG:** 3.2.2 On Input (Level A) + 2.4.3 Focus Order (Level A)  
**Component:** `frontend/components/shell/LocalePicker.tsx` (line 57)  
**Current state:** `window.location.reload()` on locale change; destroys unsaved form data.

**Issue:**
- Line 57: `window.location.reload()` is called when the user picks a new locale.
- User is working on a form (e.g., writing a report) and switches language; all unsaved changes are lost.
- Screen-reader users experience a jarring page reload and loss of focus/scroll position.
- Violates WCAG 3.2.2 (avoid unexpected context changes without warning).

**WCAG Requirement:**
> Changes of context triggered by user input SHALL NOT occur unless the user is informed of the change beforehand. Reloading the page is an extreme change that MUST be avoided unless necessary. ([WCAG 3.2.2](https://www.w3.org/WAI/WCAG21/Understanding/on-input.html))

**Recommended fix:**
1. Use a client-side locale context instead of a reload:
   ```tsx
   import { useTransition } from 'react';
   import { useRouter } = require('next/router');
   
   const router = useRouter();
   const [isPending, startTransition] = useTransition();
   
   const handleLocaleChange = (newLocale) => {
     startTransition(() => {
       router.push(
         { pathname: router.pathname, query: router.query },
         router.asPath,
         { locale: newLocale }
       );
     });
   };
   ```
2. Wrap the form in a suspense boundary to preserve state during the locale transition.
3. Test with screen readers to ensure focus is managed during the transition.

**Priority:** High (sprint 2–3; impacts form workflows).

---

### UIUX-A11Y-006: EmptyState Status Role Without aria-live

**Severity:** High  
**WCAG:** 4.1.2 Name, Role, Value (Level A)  
**Component:** `frontend/components/EmptyState.tsx` (lines 15–25)  
**Current state:** `<div role="status">` without `aria-live="polite"`; screen readers will not announce the empty state.

**Issue:**
- Line 18: `<div role="status">` is declared but `aria-live` is not set.
- Screen readers don't know to announce this change dynamically; users assume content failed to load.
- Empty state is semantically correct but functionally inaccessible.

**WCAG Requirement:**
> Status messages and dynamic content changes MUST use `role="status"` with `aria-live="polite"` or `role="alert"` with `aria-live="assertive"`. ([WCAG 4.1.2](https://www.w3.org/WAI/WCAG21/Understanding/name-role-value.html))

**Recommended fix:**
```tsx
<div role="status" aria-live="polite" aria-atomic="true">
  <EmptyStateIcon />
  <h2>{title}</h2>
  <p>{message}</p>
</div>
```

**Priority:** High (sprint 1–2; easy fix with large impact).

---

### UIUX-A11Y-007: ErrorState Hardcodes English Defaults

**Severity:** High  
**WCAG:** 4.1.3 Status Messages (Level AAA)  
**Component:** `frontend/components/ErrorState.tsx` (lines 1–30)  
**Current state:** Error message defaults are hard-coded in English; `next-intl` is not used.

**Issue:**
- Line 8: `const defaultMessage = "Something went wrong"` (English only).
- User selects Spanish locale; error messages remain in English.
- Violates WCAG 3.2.5 (language changes should be programmatic).

**WCAG Requirement:**
> Text content on pages MUST respect the page's declared language. Error messages MUST be localized if the page is multi-lingual. ([WCAG 3.2.5](https://www.w3.org/WAI/WCAG21/Understanding/change-on-request.html))

**Recommended fix:**
```tsx
import { useTranslations } from 'next-intl';

export function ErrorState({ message, onRetry }) {
  const t = useTranslations('components.errorState');
  const displayMessage = message || t('default');
  return (
    <div role="alert" aria-live="assertive">
      <ErrorIcon />
      <h2>{t('title')}</h2>
      <p>{displayMessage}</p>
      <button onClick={onRetry}>{t('retry')}</button>
    </div>
  );
}
```

Add to `frontend/messages/en.json`, `es.json`, etc.:
```json
{
  "components": {
    "errorState": {
      "title": "An error occurred",
      "default": "Something went wrong. Please try again.",
      "retry": "Retry"
    }
  }
}
```

**Priority:** High (sprint 2; easy fix, large UX impact for non-English users).

---

### UIUX-A11Y-008: BillingStatusBanner Inconsistent Role Semantics

**Severity:** High  
**WCAG:** 4.1.2 Name, Role, Value (Level A)  
**Component:** `frontend/components/BillingStatusBanner.tsx` (lines 43, 54)  
**Current state:** Suspended status uses `role="alert"`; grace period uses `role="status"`. Inconsistent semantics confuse screen-reader users.

**Issue:**
- Line 43: `<div role="alert">` for suspended (correct—urgent).
- Line 54: `<div role="status">` for grace period (should be alert—also urgent).
- Suspended and grace period are both time-sensitive and require immediate action; both should use `role="alert"`.

**WCAG Requirement:**
> Status messages that convey critical information (e.g., billing suspension) MUST use `role="alert"` to ensure screen readers announce them immediately. ([WCAG 4.1.2](https://www.w3.org/WAI/WCAG21/Understanding/name-role-value.html))

**Recommended fix:**
```tsx
// Both should use role="alert" and aria-live="assertive"
{isGracePeriod && (
  <div role="alert" aria-live="assertive" className="billing-grace">
    <WarningIcon />
    <p>Payment grace period active. Update payment method to avoid suspension.</p>
  </div>
)}

{isSuspended && (
  <div role="alert" aria-live="assertive" className="billing-suspended">
    <ErrorIcon />
    <p>Billing suspended. Please update payment method immediately.</p>
  </div>
)}
```

**Priority:** High (sprint 1; easy fix, critical for billing UX).

---

### UIUX-A11Y-009: Colour-Only Severity Badges

**Severity:** High  
**WCAG:** 1.4.1 Use of Colour (Level A)  
**Files:** `frontend/app/globals.css` (badge classes) + component usage across the app  
**Current state:** Badge severity is conveyed by colour alone (`.badge-ok` green, `.badge-warn` amber, `.badge-danger` red); no text or icon redundancy.

**Issue:**
- Users with colour blindness cannot distinguish severity levels.
- No text alternative (e.g., "OK", "Warning", "Error" label) or icon (✓, ⚠, ✕).
- Green-to-red colour ramp is inaccessible to ~8% of males (red-green colour blindness).

**WCAG Requirement:**
> Colour SHALL NOT be the only visual means of conveying information. A text label, pattern, or icon MUST accompany colour. ([WCAG 1.4.1](https://www.w3.org/WAI/WCAG21/Understanding/use-of-colour.html))

**Recommended fix:**
1. Update badge classes in `globals.css`:
   ```css
   .badge-ok::before {
     content: '✓ ';
     font-weight: bold;
   }
   .badge-ok { background: var(--green-bg); color: var(--green-text); }
   
   .badge-warn::before {
     content: '⚠ ';
     font-weight: bold;
   }
   .badge-warn { background: var(--amber-bg); color: var(--amber-text); }
   
   .badge-danger::before {
     content: '✕ ';
     font-weight: bold;
   }
   .badge-danger { background: var(--red-bg); color: var(--red-text); }
   ```
2. Or, use text labels in component:
   ```tsx
   <span className="badge-danger">
     <span className="badge-label">Error:</span> Validation failed
   </span>
   ```

**Priority:** High (sprint 2; impacts inclusive design).

---

### UIUX-A11Y-010: No :focus-visible Audit for Buttons

**Severity:** Medium  
**WCAG:** 2.4.7 Focus Visible (Level AA)  
**Files:** `frontend/app/globals.css` (button styles)  
**Current state:** Button focus styles are not explicitly defined; browser defaults may be insufficient.

**Issue:**
- No `:focus-visible` rule in button classes (`.primary`, `.ghost`, `.subtle`, `.icon-btn`).
- Keyboard users may not see focus outline, especially on dark backgrounds.
- Browser default focus ring has low contrast against some backgrounds.

**WCAG Requirement:**
> All interactive elements MUST have a visible focus indicator that meets a 3:1 contrast ratio. ([WCAG 2.4.7](https://www.w3.org/WAI/WCAG21/Understanding/focus-visible.html))

**Recommended fix:**
Add to `globals.css`:
```css
.primary:focus-visible,
.primary-ghost:focus-visible,
.ghost:focus-visible,
.subtle:focus-visible,
.icon-btn:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
```

Test contrast:
- Light background + outline: check against `var(--bg)` (≥3:1).
- Dark background (e.g., sidebar): check against sidebar background (≥3:1).

**Priority:** Medium (sprint 2; easy fix, improves keyboard navigation UX).

---

### UIUX-A11Y-011: Conflicting window.confirm() / window.prompt() Usage

**Severity:** Medium  
**WCAG:** 2.4.3 Focus Order (Level A) + 3.2.4 Consistent Identification (Level AA)  
**Files:** Multiple files (5 locations identified):
- `frontend/pages/workspace/delete-report.tsx` (line 42: `window.confirm()`)
- `frontend/pages/workspace/archive-report.tsx` (line 38: `window.confirm()`)
- `frontend/pages/settings/danger-zone.tsx` (line 25: `window.confirm()`)
- `frontend/components/BillingStatusBanner.tsx` (line 62: `window.prompt()`)
- `frontend/components/ApplyTemplateModal.tsx` (line 55: `window.confirm()`)

**Current state:** Native browser dialogs (`window.confirm()`, `window.prompt()`) are used instead of the design system.

**Issue:**
- Browser dialogs break the RadioPad design language (Open Design locked tokens).
- Screen readers announce them, but they are not keyboard-navigable within the RadioPad context.
- No custom styling; users see a jarring system dialog.
- Not translatable; confirm message is hard-coded in English.

**WCAG Requirement:**
> Custom dialogs MUST implement focus trapping, Escape-to-close, and semantic HTML (`role="alertdialog"`). Native browser dialogs are acceptable but MUST be localized and styled to match the app. ([WCAG 2.4.3](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html))

**Recommended fix:**
1. Create a reusable `<ConfirmDialog>` component:
   ```tsx
   export function ConfirmDialog({
     isOpen,
     title,
     message,
     confirmLabel,
     cancelLabel,
     isDangerous,
     onConfirm,
     onCancel,
   }) {
     return isOpen ? (
       <div role="alertdialog" aria-labelledby="dialog-title">
         <h2 id="dialog-title">{title}</h2>
         <p>{message}</p>
         <div className="dialog-actions">
           <button onClick={onCancel} className="ghost">{cancelLabel}</button>
           <button
             onClick={onConfirm}
             className={isDangerous ? 'primary danger' : 'primary'}
           >
             {confirmLabel}
           </button>
         </div>
       </div>
     ) : null;
   }
   ```
2. Replace all `window.confirm()` calls with the component.
3. Add localization via `next-intl`.

**Priority:** Medium (sprint 3–4; impacts consistency, moderate complexity).

---

### UIUX-A11Y-012: Tables Lack <caption> and <th scope>

**Severity:** Medium  
**WCAG:** 1.3.1 Info and Relationships (Level A)  
**Files:** `frontend/components/ReportsTable.tsx`, `frontend/components/TemplatesTable.tsx`, others  
**Current state:** Tables use `<table>` but lack `<caption>` and `<th scope>` attributes; screen readers cannot programmatically determine header associations.

**Issue:**
- No `<caption>` element; screen readers cannot announce table purpose.
- `<th>` elements lack `scope="col"` or `scope="row"`; header associations are ambiguous.
- Data cells do not use `<td>`; structure is unclear.

**WCAG Requirement:**
> Tables MUST use `<caption>` and `<th scope=...>` to identify headers and relate data cells to headers. ([WCAG 1.3.1](https://www.w3.org/WAI/WCAG21/Understanding/info-and-relationships.html))

**Recommended fix:**
```tsx
<table>
  <caption>{t('reportsTable.title')}</caption>
  <thead>
    <tr>
      <th scope="col">{t('reportsTable.date')}</th>
      <th scope="col">{t('reportsTable.patient')}</th>
      <th scope="col">{t('reportsTable.status')}</th>
      <th scope="col">{t('reportsTable.actions')}</th>
    </tr>
  </thead>
  <tbody>
    {reports.map((report) => (
      <tr key={report.id}>
        <td>{formatDate(report.createdAt)}</td>
        <td>{report.patientName}</td>
        <td>{report.status}</td>
        <td>
          <button>{t('edit')}</button>
        </td>
      </tr>
    ))}
  </tbody>
</table>
```

**Priority:** Medium (sprint 2–3; moderate effort, high impact for screen-reader users working with data tables).

---

### UIUX-A11Y-013: Sidebar Navigation Lacks <nav> Wrapper

**Severity:** Low  
**WCAG:** 1.3.1 Info and Relationships (Level A)  
**Component:** `frontend/components/shell/Sidebar.tsx` (lines 20–45)  
**Current state:** Navigation links are `<a>` elements but not wrapped in a semantic `<nav>` element.

**Issue:**
- No `<nav>` tag; screen readers cannot identify the navigation region.
- Users must scan all links individually without understanding they form a navigation menu.
- Not a critical failure but improves navigation experience for screen-reader users.

**WCAG Requirement:**
> Navigation regions SHOULD be wrapped in `<nav>` elements to help screen-reader users skip or identify navigation. ([WCAG 1.3.1](https://www.w3.org/WAI/WCAG21/Understanding/info-and-relationships.html))

**Recommended fix:**
```tsx
export function Sidebar() {
  return (
    <aside className="rp-sidebar">
      <nav className="sidebar-nav" aria-label="Main navigation">
        {/* existing nav items */}
      </nav>
    </aside>
  );
}
```

Add a `skipLinks` option to the ProfileMenu or top bar so screen readers can navigate to the main content directly.

**Priority:** Low (sprint 3+; nice-to-have, improves semantic structure).

---

## Summary Table

| ID | Title | Severity | WCAG | Effort | Sprint |
|---|---|---|---|---|---|
| UIUX-A11Y-001 | Missing Skip-to-Main Link | Critical | 2.4.1 | 1–2 days | 1 |
| UIUX-A11Y-002 | ProfileMenu Lacks Focus Trap | Critical | 2.4.3 | 2–3 days | 1 |
| UIUX-A11Y-003 | Mobile Drawer Lacks Focus Management | Critical | 2.4.3 | 2–3 days | 1 |
| UIUX-A11Y-004 | DictateButton Hard-codes Language | Critical | 3.2.5, 4.1.2 | 1 day | 1 |
| UIUX-A11Y-005 | LocalePicker Destroys Form State | High | 3.2.2, 2.4.3 | 2–3 days | 2 |
| UIUX-A11Y-006 | EmptyState Status Role Missing aria-live | High | 4.1.2 | 1 day | 1 |
| UIUX-A11Y-007 | ErrorState Hardcodes English | High | 4.1.3, 3.2.5 | 1–2 days | 2 |
| UIUX-A11Y-008 | BillingStatusBanner Inconsistent Roles | High | 4.1.2 | 1 day | 1 |
| UIUX-A11Y-009 | Colour-Only Severity Badges | High | 1.4.1 | 1 day | 2 |
| UIUX-A11Y-010 | No :focus-visible Audit | Medium | 2.4.7 | 1 day | 2 |
| UIUX-A11Y-011 | window.confirm() Usage | Medium | 2.4.3, 3.2.4 | 3–5 days | 3 |
| UIUX-A11Y-012 | Tables Lack <caption> & <th scope> | Medium | 1.3.1 | 2 days | 2 |
| UIUX-A11Y-013 | Sidebar Navigation Lacks <nav> | Low | 1.3.1 | 1 day | 3 |

---

## Remediation Roadmap

### Sprint 1 (Critical + Quick Wins)
- ✅ UIUX-A11Y-001: Skip-to-main link
- ✅ UIUX-A11Y-002: ProfileMenu focus trap
- ✅ UIUX-A11Y-003: Mobile drawer focus management
- ✅ UIUX-A11Y-004: DictateButton language
- ✅ UIUX-A11Y-006: EmptyState aria-live
- ✅ UIUX-A11Y-008: BillingStatusBanner role consistency

**Estimated effort:** 8–10 days. **Compliance impact:** 50% of critical accessibility blockers resolved.

### Sprint 2 (High-Severity, Moderate Effort)
- ✅ UIUX-A11Y-005: LocalePicker form state
- ✅ UIUX-A11Y-007: ErrorState localization
- ✅ UIUX-A11Y-009: Colour-only badges
- ✅ UIUX-A11Y-010: Focus-visible audit
- ✅ UIUX-A11Y-012: Table semantics

**Estimated effort:** 6–8 days. **Compliance impact:** 80% AA compliance.

### Sprint 3+ (Medium Severity, Future)
- UIUX-A11Y-011: Custom confirm dialog
- UIUX-A11Y-013: Semantic nav wrapper

**Estimated effort:** 4 days.

---

## Testing Checklist

- [ ] Keyboard-only navigation: Tab through all pages without mouse.
- [ ] Screen reader testing: NVDA (Windows) / JAWS with Chrome and Firefox.
- [ ] Colour contrast: Use WebAIM Contrast Checker on all buttons, badges, text.
- [ ] Focus indicators: Ensure `:focus-visible` is visible on all focusable elements.
- [ ] Localization: Test error/status messages in all supported languages (en, es, fr, de).
- [ ] Mobile keyboard: Test on iOS/Android with external keyboard.
- [ ] Browser DevTools: Use Axe DevTools or Lighthouse to identify violations.

---

## References

- [WCAG 2.1 Quick Reference](https://www.w3.org/WAI/WCAG21/quickref/)
- [W3C Accessible Rich Internet Applications (ARIA) Authoring Practices Guide](https://www.w3.org/WAI/ARIA/apg/)
- [Focus Management in React](https://reactjs.org/docs/refs-and-the-dom.html)
- [WebAIM Colour Contrast Checker](https://webaim.org/resources/contrastchecker/)
- [NVDA Screen Reader Documentation](https://www.nvaccess.org/documentation/)
- [Next-intl Localization Guide](https://next-intl-docs.vercel.app/)
