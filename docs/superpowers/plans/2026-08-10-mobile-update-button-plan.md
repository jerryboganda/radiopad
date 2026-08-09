# Fix Mobile Bottom Navigation Overlap & Move Check for Updates to Topbar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate the mobile "Check for updates" button to the topbar (`CompanionTopbar`) and fix the page layout bottom padding so content is never hidden behind the fixed bottom navigation bar.

**Architecture:** Render `<MobileUpdateCheck />` inside `CompanionTopbar` alongside `ThemeToggle`. Add bottom padding (`pb-24` / `100px`) to mobile layout containers (`.rp-comp-body` and `.rp-mobile-layout`) to prevent UI elements from being obscured by the fixed bottom navigation bar (`<nav>`).

**Tech Stack:** React, Next.js (App Router), Tailwind CSS / Vanilla CSS.

## Global Constraints
- Do not break existing desktop or mobile features.
- Maintain dark/light theme compatibility.
- Ensure `<MobileUpdateCheck />` continues to return null on non-mobile surfaces.

---

### Task 1: Relocate MobileUpdateCheck into CompanionTopbar and remove from scrollable footers

**Files:**
- Modify: [e:/Projects/RadioPad/RADIOPAD/frontend/app/(mobile)/companion/page.tsx](file:///e:/Projects/RadioPad/RADIOPAD/frontend/app/%28mobile%29/companion/page.tsx#L99-L112)
- Modify: [e:/Projects/RadioPad/RADIOPAD/frontend/app/radiopad.css](file:///e:/Projects/RadioPad/RADIOPAD/frontend/app/radiopad.css#L1722-L1728)

**Interfaces:**
- Consumes: `MobileUpdateCheck` from `@/components/companion/MobileUpdateCheck`
- Produces: Topbar with embedded check-for-updates component

- [ ] **Step 1: Update `CompanionTopbar` component to include `MobileUpdateCheck`**

In [page.tsx](file:///e:/Projects/RadioPad/RADIOPAD/frontend/app/%28mobile%29/companion/page.tsx#L99-L112), update `CompanionTopbar`:
```tsx
function CompanionTopbar() {
  return (
    <div className="rp-comp-topbar">
      <div className="rp-comp-brand">
        <span className="brand-mark" aria-hidden><span className="brand-mark-letter">R</span></span>
        <span className="rp-comp-brand-text">
          <span className="rp-comp-brand-name">RadioPad</span>
          <span className="rp-comp-brand-kicker">AI-assisted radiology reporting</span>
        </span>
      </div>
      <div className="flex items-center gap-2">
        <MobileUpdateCheck />
        <ThemeToggle />
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Remove redundant `<MobileUpdateCheck />` calls from footer containers in `page.tsx`**

Remove `<div className="rp-comp-footer"><MobileUpdateCheck /></div>` lines from `page.tsx` (lines ~588 and ~617).

- [ ] **Step 3: Update CSS styling for `.rp-comp-topbar` in `radiopad.css`**

Ensure `.rp-comp-topbar` handles flex items cleanly:
```css
.rp-comp-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 18px 20px 0;
}
```

- [ ] **Step 4: Verify Next.js build / typecheck**

Run: `pnpm --filter frontend run typecheck`
Expected: PASS with 0 errors.

---

### Task 2: Add bottom padding to mobile page layout to fix fixed bottom nav overlap

**Files:**
- Modify: [e:/Projects/RadioPad/RADIOPAD/frontend/app/(mobile)/layout.tsx](file:///e:/Projects/RadioPad/RADIOPAD/frontend/app/%28mobile%29/layout.tsx#L26-L32)
- Modify: [e:/Projects/RadioPad/RADIOPAD/frontend/app/radiopad.css](file:///e:/Projects/RadioPad/RADIOPAD/frontend/app/radiopad.css#L1746-L1754)

**Interfaces:**
- Consumes: Mobile layout container styles
- Produces: Non-overlapping mobile layout with bottom navigation bar space reserved

- [ ] **Step 1: Update `MobileLayout` content container in `layout.tsx`**

Add bottom padding (`pb-24`) to the main content container:
```tsx
<div className="flex-1 w-full relative pb-24">
  {children}
</div>
```

- [ ] **Step 2: Update `.rp-comp-body` padding in `radiopad.css`**

Update `.rp-comp-body` padding-bottom:
```css
.rp-comp-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 26px 20px 96px;
}
```

- [ ] **Step 3: Verify frontend build & typecheck**

Run: `pnpm --filter frontend run build` or `pnpm --filter frontend run typecheck`
Expected: PASS cleanly.
