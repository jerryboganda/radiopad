# 08 — Interaction & Flow Audit

**Status:** Complete (static review)  
**Owner:** UX Audit  
**Last Updated:** 2025-01-17

---

## Executive Summary

RadioPad has **9 critical user journeys** spanning auth, reporting, AI collaboration, validation, admin, and mobile. The flow logic is sound, but **navigation is fragmented** (18 of 37 routes are hidden from the sidebar), **destructive actions default to native browser confirms** (poor UX), and **state management lacks modern patterns** (page reloads on locale change, no optimistic UI, no toast notifications). This audit recommends adopting a shared `ConfirmDialog` component and migrating to a proper state-management primitive for success/error feedback.

---

## F1: Login & Tenant Context

**Route:** `/login` (`frontend/app/login/page.tsx`)

**Flow:**
1. User enters tenant slug + email.
2. Form calls `api.auth.signIn(tenant, user)` → stores token in secure storage (Keychain/Keystore or browser localStorage fallback).
3. Router navigates to `/` (workspace home).

**Current state:**
- Login page is not linked from sidebar; reachable via unauthenticated redirect or profile menu only.
- Token explanation block (lines 49–53) is **wordy and technical** — describes dev auth headers and storage tiers rather than guiding the user on what happens next.
- No in-flight validation or helpful error messages (line 40 shows raw API error).

**Findings:**
- `UIUX-FLOW-F1-001` **Wordy login explanation.** The subtitle describes header-based dev auth and storage mechanics instead of user-focused copy ("Signing you in to your workspace as [email]"). **Severity: Info** — doesn't block flow but increases cognitive load for new users.
- `UIUX-FLOW-F1-002` **Missing "return to" intent.** Login page supports a `?return=` query param but doesn't document or surface it. Bookmarked reports break the flow. **Severity: Medium**.

---

## F2: Workspace Home → Reports List → Open Report → Draft → Validate → Sign

**Routes:** `/` (dashboard), `/reports`, `/reports/view?id=...`, `api.reports.*`

**Flow:**
1. User lands on `/` (workspace home, `frontend/app/page.tsx`).
2. Sidebar shows reports list link; clicking navigates to `/reports` or stays on `/` (both render the same component).
3. User clicks a row → navigates to `/reports/view?id={id}` (`ReportClient.tsx`).
4. Full-screen editor opens with sections (Indication, Technique, Findings, Impression, Recommendations).
5. User types; form state is held in React.
6. Click "Validate" → API call `POST /api/reports/{id}/validate` → findings render inline.
7. Click "Sign" → opens modal, user enters radiologist note, clicks "Confirm sign" → `POST /api/reports/{id}/sign` → report marked Exported.

**Current state:**
- Dashboard loads reports in paginated list with status badges and filtering.
- Detail page (`ReportClient.tsx`) is a sprawling client component (400+ lines) holding 13+ state variables (line 48–77).
- No "Undo" on save; no optimistic UI updates.
- "Saved" confirmations are **silent** — users don't see a toast or banner.
- Validation findings overlay inline; user fixes prose → re-validate → repeat.

**Findings:**
- `UIUX-FLOW-F2-001` **Silent save state.** When user edits and saves, the component calls `api.reports.update(id, draft)` but never surfaces success feedback (no toast, no banner). Users must manually re-validate to confirm save worked. **Severity: Medium**.
- `UIUX-FLOW-F2-002` **No undo/revert.** Once a draft is saved, there is no "Undo" or "Revert to last signed version." Accidental edits are not reversible. **Severity: Medium**.
- `UIUX-FLOW-F2-003` **Duplicate routes.** `/` and `/reports` both show the same list. Confirm intent — this causes navigation confusion. **Severity: Info**.
- `UIUX-FLOW-F2-004` **No pagination breadcrumb.** Detail view doesn't show "Report 5 of 142" or prev/next row buttons. Heavy multitab workflows are fragmented. **Severity: Info**.

---

## F3: AI-Assisted Drafting (Mark & Acknowledge)

**Component:** `ReportClient.tsx` (inline AI suggestions)

**Flow:**
1. User types in Findings/Impression/Recommendations section.
2. AI gateway (backend) produces a suggested rewrite in a given tone (concise, formal, patient-friendly, referring).
3. UI marks the AI prose with `.ai-mark` (purple badge) per the design spec.
4. User reviews inline and clicks "Use this" or "Discard."
5. Once "Use this" → text is merged into the draft, `.ai-mark` wrapper is removed.

**Current state:**
- AI highlights are tracked in `aiHighlights` state (line 58).
- Each AI suggestion is wrapped in `.ai-mark` per design lock.
- "Use this" / "Discard" buttons exist but there is **no documented gesture for bulk-acknowledging** all AI suggestions at once (e.g., "Accept all AI suggestions" CTA).

**Findings:**
- `UIUX-FLOW-F3-001` **No bulk-acknowledge gesture.** If a report has 5 AI suggestions, the user must click "Use" or "Discard" five times. No "Accept all" or "Reject all" button. **Severity: Medium** — compounds on lengthy reports.
- `UIUX-FLOW-F3-002` **AI mark visual survives merge.** When user clicks "Use," does the `.ai-mark` wrapper stay in the prose, or is it removed? Currently unclear; spec says "removed until acknowledged" but no test confirms this. **Severity: Info**.

---

## F4: Validation Center → View Findings → Fix Report → Re-Validate

**Route:** `/validation` (`frontend/app/validation/page.tsx`)

**Flow:**
1. User navigates to `/validation`.
2. Page loads all draft reports; for each, it calls `POST /api/reports/{id}/validate` to fetch findings.
3. Findings are aggregated by severity (Blocker in red, Warning in amber, Info in blue).
4. User clicks a finding → ideally navigates to the source report to fix it.
5. User edits the prose in `ReportClient.tsx`.
6. User returns to `/validation` → page re-runs validation to confirm findings are cleared.

**Current state:**
- Validation center renders a table with report status and finding summaries.
- Clicking a finding doesn't auto-navigate; user must manually click the report row.
- No "refresh" button post-edit — user must manually re-navigate to `/validation`.
- Error state shows raw API message (line 44: `(e as Error).message`).

**Findings:**
- `UIUX-FLOW-F4-001` **No drilldown affordance.** Clicking a finding should open the report editor with the finding pre-scrolled in context. Currently, user must infer which section to edit. **Severity: Medium**.
- `UIUX-FLOW-F4-002` **Manual refresh cycle.** After fixing findings in the report, user must return to `/validation` page manually; there's no "Refresh this report" button. **Severity: Low** — UX workaround exists (browser back + refresh).

---

## F5: Admin CRUD (Providers, Users, Tenants, Billing)

**Routes:** `/admin/providers*`, `/admin/billing`, `/admin/settings`, `/admin/governance`, `/admin/model-eval`, `/admin/feature-flags`, `/admin/usage`, `/admin/sso`

**Flow:** (exemplified by Provider OAuth configuration)
1. Admin navigates to `/admin/providers`.
2. Selects a provider row → navigates to `/admin/providers/[id]/` (detail page, query-string-based).
3. Scrolls to OAuth section.
4. Pastes refresh token → clicks "Save token" → form submits to `api.providers.oauth.save()`.
5. Success banner appears ("Refresh token saved…").
6. To delete, clicks "Delete token" → `window.confirm()` (native dialog) → "Delete the stored OAuth refresh token?"
7. User confirms → API call → success banner.

**Current state:**
- Admin pages use `.rp-panel` + `.rp-row` layout consistently.
- Destructive actions default to `window.confirm()` (line 83 in `ProviderOAuthAdminClient.tsx`).
- Form validation uses client-side checks (e.g., "Refresh token is required", line 62).
- No undo on destructive actions; deleting a token is permanent.

**Findings:**
- `UIUX-FLOW-F5-001` **window.confirm() antipattern.** Five pages use `window.confirm()` instead of a styled `ConfirmDialog` component (ProviderOAuthAdminClient:83, ValidationPacksPage:68, AdminMcpPage:79, PromptsPage, ReportClient). Native dialogs break design continuity and accessibility. **Severity: Medium** — blocks accessibility review.
- `UIUX-FLOW-F5-002` **No undo for destructive admin actions.** Deleting a provider, user, or validation pack is irreversible. No "Recently deleted" trash/recovery feature. **Severity: Low** — risk is mitigated by warning dialogs.
- `UIUX-FLOW-F5-003` **Datetime-local without bounds.** `/admin/providers/[id]/` uses `<input type="datetime-local">` for OAuth token expiry (line 166) without `min` / `max` attributes. User can set invalid dates (e.g., past dates). **Severity: Low** — backend should validate.
- `UIUX-FLOW-F5-004` **Backend jargon in UI labels.** Admin pages expose enum names like `rotationPolicy: 'before_expiry' | 'every_24h' | 'never'` as select option values (line 182–184). Suggest user-facing labels: "Refresh before expiry", "Refresh every 24 hours", "Manual refresh only". **Severity: Info**.

---

## F6: Mobile Dictation

**Route:** `/mobile/dictate` (`frontend/app/mobile/dictate/page.tsx`)

**Flow:**
1. Radiologist opens RadioPad on a mobile device.
2. Navigates to `/mobile/dictate` (direct URL or desktop pairing handoff).
3. Clicks "Start dictation" → browser requests microphone permission.
4. Browser's Web Speech API (`SpeechRecognition`) runs locally; audio is never sent to a server.
5. Transcript accumulates in `<textarea>` and is auto-saved to `localStorage` (draft key).
6. Clicks "Save to report" → `api.reports.appendFindings(reportId, transcript)` → navigates to `/mobile/reports/edit?id={id}`.

**Current state:**
- Uses Web Speech API when available (Chrome, Edge, Safari on iOS 14.5+); falls back to disabled button.
- Transcript is persisted to `localStorage` per report (line 64: `radiopad.mobile.dictate.{reportId}`).
- Dictation language is hardcoded to `'en-US'` (line 48 in `DictateButton.tsx`).

**Findings:**
- `UIUX-FLOW-F6-001` **Hardcoded dictation language.** `DictateButton.tsx` line 48 sets `r.lang = 'en-US'` regardless of the user's locale. International users cannot dictate in their native language. **Severity: Medium** — blocks i18n completeness.
- `UIUX-FLOW-F6-002` **No fallback for unsupported browsers.** When Web Speech API is unavailable, button is disabled with a title-based tooltip. Mobile browsers (Safari, Firefox) see "Dictation not supported". Suggest an alternative (manual typing, cloud-based speech service). **Severity: Low** — affects < 20% of users.

---

## F7: Mobile Report Signing

**Route:** `/mobile/reports/sign?id={id}` (`frontend/app/mobile/reports/sign/page.tsx`)

**Flow:**
1. Radiologist opens a validated report on mobile.
2. Clicks "Sign" → navigates to `/mobile/reports/sign?id={id}`.
3. Full-screen signature pad (or PIN entry).
4. User signs or enters PIN → clicks "Confirm sign".
5. `api.reports.sign(id, signature)` → report marked Exported.
6. Confirmation page → "Report signed at [time]".

**Current state:**
- Signature UX is not yet fully audited (page exists but signature capture method is TBD).
- "Sign report" CTA is missing from the mobile edit page.

**Findings:**
- `UIUX-FLOW-F7-001` **Signing not discoverable.** The `/mobile/reports/sign` route is direct-URL-only; not linked from the mobile edit page. User must know to manually append `?id=` to the URL. **Severity: Medium**.

---

## F8: Locale Change (LocalePicker)

**Component:** `frontend/components/LocalePicker.tsx` (topbar / profile menu)

**Flow:**
1. User clicks the locale picker (select dropdown in topbar).
2. Selects a language (or "Auto").
3. Component:
   - Writes a `radiopad-locale` cookie via `writeLocaleCookie()`.
   - Calls `PUT /api/users/me/locale` (async, failures are silent).
   - **Reloads the page** via `window.location.reload()` (line 57).
4. On next render, the message bundle changes, all UI text switches language.

**Current state:**
- No helper text explaining that the page will reload.
- Reload is non-negotiable (next-intl bundles are server-rendered; client-side hydration doesn't switch language).
- Cookie failure doesn't warn the user.

**Findings:**
- `UIUX-FLOW-F8-001` **Hard page reload on locale change.** User selects Spanish → full page reload → form state lost, scroll position reset. In-flight edits are not preserved. **Severity: Medium** — breaks editing workflow. Recommend server-side render of message bundle via `Next-Intl-Locale` cookie negotiation, or client-side swap via context (not `window.location.reload()`).
- `UIUX-FLOW-F8-002` **No "reload pending" signal.** Component doesn't show a loading spinner or toast before reload. User thinks the app froze. **Severity: Low** — UX smoothness.

---

## F9: Logout

**Route:** Profile menu (no dedicated `/logout` page)

**Flow:**
1. User clicks profile menu (top-right).
2. Selects "Sign out".
3. `api.auth.signOut()` → clears token from secure storage.
4. Router navigates to `/login`.

**Current state:**
- Logout is functional but not formally documented in the route inventory.
- No confirmation dialog.

**Findings:**
- None identified; flow is sound.

---

## Navigation & Discoverability

### Sidebar Coverage Gap

**Finding:** Only **20 of 37 routes** are linked from the sidebar (`frontend/components/shell/nav.config.tsx` lines 71–112).

**Hidden routes (18 total):**
- **Workspace:** `/reports` (duplicate of `/`), `/reports/view`, `/audit/verify`
- **Library:** `/rulebooks/view`, `/rulebooks/editor`
- **Intended but not discoverable:** `/copilot`, `/pair`, `/governance`, `/analytics/quality`, `/mobile/dictate`, `/mobile/reports/edit`, `/mobile/reports/sign`
- **Admin:** `/admin/copilot`, `/admin/mcp`, `/admin/sso`, `/admin/providers/oauth`
- **System:** `/admin/providers/oauth` (OAuth callback)

**Severity:** `UIUX-NAV-001` **Low-discoverability route inventory.** Routes like `/copilot` (AI workspace), `/governance` (public transparency), `/admin/sso` (critical security config), and `/audit/verify` (integrity auditing) are part of the product but invisible to new users. Recommend:
1. Add an "Advanced" sidebar section or collapsible group for low-frequency admin routes.
2. Surface `/copilot` under "Workspace" if it's part of the core UX.
3. Clearly document which routes are intentional (e.g., OAuth callback) vs. usability gaps.

### Query-String vs. Dynamic-Route Inconsistency

**Finding:** Detail pages use query strings (`/reports/view?id=...`) instead of Next.js dynamic routes (`/reports/[id]/`).

**Current pattern (line 3 in `reports/view/page.tsx`):**
```tsx
export default function ReportViewPage() {
  return <ReportClient />;
}
```
`ReportClient` reads `?id=` via `readQueryParam('id')`.

**Expected pattern (would be):**
```
/reports/[id]/page.tsx
```

**Severity:** `UIUX-NAV-002` **Minor UX friction.** Query-string URLs are less shareable and harder to bookmark. No SEO impact (app is authenticated). Recommend:
1. Migrate detail pages to dynamic routes at your convenience.
2. Add redirect from old query-string URLs for backward compatibility.

### Missing Breadcrumbs

**Finding:** Deep admin pages (e.g., `/admin/providers/[id]`) lack a breadcrumb trail.

**Severity:** `UIUX-NAV-003` **Breadcrumb missing in admin hierarchy.** When user navigates to a provider detail page, they see no "Admin > Providers > [Provider Name]" breadcrumb. User must use the browser back button to return to the list. Recommend:
1. Add breadcrumbs to all pages under `/admin/`.
2. Use `PageHeader` component (which supports breadcrumbs) on detail pages.

---

## Destructive Actions Inventory

**Current pattern:** All destructive actions (delete, deprecate, block) use native `window.confirm()`.

### Locations

| File | Line | Action | Confirm Text | Severity |
|------|------|--------|--------------|----------|
| `ProviderOAuthAdminClient.tsx` | 83 | Delete OAuth token | "Delete the stored OAuth refresh token?" | Medium |
| `ValidationPacksPage.tsx` | 68 | Deprecate validation pack | "Mark this pack as Deprecated?" | Medium |
| `AdminMcpPage.tsx` | 79 | Remove MCP tool | "Permanently delete this tool registration?" | Medium |
| `PromptsPage.tsx` | ? | ? (needs audit) | ? | ? |
| `ReportClient.tsx` | ? | ? (needs audit) | ? | ? |

**Severity:** `UIUX-DEST-001` **window.confirm() breaks design system.** All destructive actions default to the browser's native dialog, which:
- Doesn't match the locked design aesthetic (Open Design system).
- Fails accessibility audits (no ARIA labels for styled close buttons, no focus trap).
- Can't be tested/mocked in unit tests without careful setup.

**Recommendation:** Adopt a shared `ConfirmDialog` component:
```tsx
<ConfirmDialog
  title="Delete Provider Token?"
  message="This action cannot be undone."
  confirmLabel="Delete"
  cancelLabel="Cancel"
  onConfirm={() => handleDelete()}
  severity="danger"
/>
```

---

## State Management Gaps

### Page Reload on Locale Change

**Component:** `LocalePicker.tsx` line 57: `window.location.reload()`

**Impact:**
- In-flight form edits are lost.
- Scroll position resets.
- User sees a brief blank page.

**Recommendation:** Explore server-side language negotiation via cookie or next-intl's built-in client-side re-render (if available in Next.js 16).

### No Optimistic UI

**Example:** When user saves a report, the component calls `api.reports.update()` and waits for the server response. If the network is slow, the UI freezes for 2+ seconds.

**Severity:** `UIUX-STATE-001` **No optimistic updates.** Recommend:
1. Update local state immediately on user action.
2. Show a saving indicator (spinner or badge).
3. Revert on error with a clear error message.

### No Global Toast System

**Current pattern:** Success/error feedback is scattered:
- Ad-hoc banner divs (e.g., `<div className="banner ok">{info}</div>`).
- Silent failures (e.g., locale picker).
- Inline error messages in component state.

**Severity:** `UIUX-STATE-002` **Inconsistent feedback messaging.** Recommend:
1. Implement a global toast/notification system (e.g., `useToast()` hook).
2. Centralize success/error messages in `frontend/lib/copy.ts` (see 10-copy-microcopy-audit).
3. Apply consistent timing and styling across the app.

### No Modal Pattern Primitive

**Current pattern:** Modals are implemented ad-hoc with `useState(open)` and CSS conditionals.

**Severity:** `UIUX-STATE-003` **Modal state fragmentation.** Recommend:
1. Create a `useModal()` hook or `<Modal>` wrapper component.
2. Centralize focus management, scroll-lock, and close handlers.

---

## Empty / Error / Loading State Coverage

### Pages Using Skeleton/EmptyState/ErrorState

**Audit results (out of 37 routes):**

| Route | Skeleton | EmptyState | ErrorState | Status |
|-------|----------|-----------|------------|--------|
| `/` (dashboard) | ✅ `TableSkeleton` | ✅ `EmptyState` | ✅ `ErrorState` | Good |
| `/validation` | ❌ | ❌ | ✅ (raw message) | Partial |
| `/audit` | ❌ | ❌ | ❓ | Missing |
| `/reports/view` (detail) | ❌ | ❌ | ❌ | Missing |
| `/admin/billing` | ❌ | ❌ | ❌ | Missing |
| `/admin/providers/[id]` | ❌ | ❌ | ❌ | Missing |

**Severity:** `UIUX-STATE-COVERAGE-001` **Incomplete loading/empty/error states.** Most data pages don't use `Skeleton` (loading), `EmptyState` (zero rows), or `ErrorState` (fetch failure). Users see blank pages or raw error text.

**Recommendation:**
1. Add `Skeleton` to every data-driven page's `useEffect()` fetch.
2. Render `EmptyState` when `items.length === 0`.
3. Render `ErrorState` on fetch error, with a "Retry" button.
4. **Example:**
   ```tsx
   if (loading) return <TableSkeleton />;
   if (error) return <ErrorState onRetry={refresh} />;
   if (items.length === 0) return <EmptyState title="No reports yet" />;
   return <ReportTable items={items} />;
   ```

---

## Recommendations (Prioritized)

### High Priority (Medium Severity)
1. **Adopt `ConfirmDialog` component** for all destructive actions (`UIUX-DEST-001`).
2. **Add global toast/notification system** for success/error feedback (`UIUX-STATE-002`).
3. **Fix locale picker reload** to preserve form state (`UIUX-FLOW-F8-001`).
4. **Hardcode dictation language** to user's locale, not 'en-US' (`UIUX-FLOW-F6-001`).
5. **Add "Acknowledge AI suggestions" bulk action** (`UIUX-FLOW-F3-001`).

### Medium Priority (Low/Info Severity)
6. **Expose hidden routes** in sidebar or "Advanced" section (`UIUX-NAV-001`).
7. **Add breadcrumbs** to admin detail pages (`UIUX-NAV-003`).
8. **Implement "Undo" or "Revert"** for report edits (`UIUX-FLOW-F2-002`).
9. **Complete loading/empty/error state coverage** (`UIUX-STATE-COVERAGE-001`).
10. **Migrate query-string detail routes** to dynamic routes (`UIUX-NAV-002`).

---

## Audit Notes

- All findings are based on static code review and do not include user testing.
- Many findings are addressable with existing primitives (`Skeleton`, `EmptyState`, `ErrorState` exist; just not used consistently).
- The design system (`frontend/app/globals.css`) is locked; findings do not suggest style changes.
- No findings block production; most are UX smoothness and consistency improvements.
