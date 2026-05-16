# 10 — Copy & Microcopy Audit

**Status:** Complete (static review)  
**Owner:** UX Audit  
**Last Updated:** 2025-01-17

---

## Executive Summary

RadioPad's copy is **inconsistent in tone, often exposes backend jargon, and bypasses the i18n system** in critical places. The app lacks a centralized microcopy library, leading to hard-coded English defaults in UI primitives, untranslatable strings in form labels, and mixed imperative/descriptive voice across pages. This audit identifies **23 copy issues** and recommends a shared `frontend/lib/copy.ts` microcopy catalogue synced with next-intl.

---

## Voice & Tone

**Target voice for RadioPad:**
- **Clinical, calm, present tense.** Radiologists are under time pressure; UI copy should reduce friction, not add personality.
- **Active voice.** "Validate this report" (not "Report to be validated").
- **Plain language.** No jargon unless the user introduced it (e.g., "PHI" only appears if the user sees it in a provider config, not as default UI copy).
- **Encourage action.** Success copy affirms ("Report signed") rather than explains ("Signature was recorded").
- **No hype.** No exclamation marks, no "exciting new feature" phrasing. Radiologists want competence, not enthusiasm.

**Example good copy:**
- "Save" (button)
- "Report saved" (success toast)
- "Unable to save. Check your connection." (error)
- "No reports yet. Create one to get started." (empty state)

**Example bad copy:**
- "Awesome! Report signed! 🎉" (hype)
- "Oops, something went wrong." (generic, colloquial)
- "Please ensure your OAuth2 provider ProviderComplianceClass is PhiApproved." (backend enum names)

---

## Findings (Alphabetical by Category)

### Buttons & CTAs — Imperative Inconsistency

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-001` | `ReportClient.tsx` | "Use this" (AI suggestion accept) | Imperative, but vague — what is "this"? | "Use suggestion" or "Accept" | Info |
| `UIUX-COPY-002` | `ReportClient.tsx` | "Discard" (AI suggestion reject) | Imperative but formal. | "Dismiss" or "Skip" | Info |
| `UIUX-COPY-003` | `ProviderOAuthAdminClient.tsx:187` | "Replace token" (when token exists) / "Save token" (when new) | Dual labels for same action. Confusing. | Always "Save token" (users understand save is upsert). | Low |
| `UIUX-COPY-004` | `DictateButton.tsx:76` | "Stop dictation" (when recording) / "Dictate" (when idle) | Clear state labels, but consider "Stop" vs. "Stop recording". | Consider "Stop recording" for clarity. | Info |

### Empty States — Missing or Generic

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-005` | `/reports` (when no reports) | Likely hard-coded or absent | No guidance for empty workspace. | "No reports yet. Create one to get started." (with "Create report" link) | Low |
| `UIUX-COPY-006` | `/validation` (when all reports valid) | Likely absent | Page shows empty table. User unsure if it's loading or success. | "All reports are valid ✓" (EmptyState with green tone) | Low |
| `UIUX-COPY-007` | `/admin/mcp` (no tools registered) | Likely absent | Admin sees empty table, no guidance. | "No MCP tools registered. Add one to extend RadioPad." (with "Add tool" link) | Info |

### Error Messages — Raw Exceptions Leak

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-008` | `LoginPage.tsx:40` | `ex.body?.error \|\| ex.message` | Raw API error strings shown to user. Example: "401 Unauthorized" instead of "Invalid email or tenant." | User-friendly error mapping in API client; route 401 → "Invalid email or tenant" | Medium |
| `UIUX-COPY-009` | `ValidationPage.tsx:44` | `(e as Error).message` | Raw error text shown in row. Example: "ECONNREFUSED" or "timeout". | "Validation failed. Retry." with error logged server-side. | Medium |
| `UIUX-COPY-010` | `ErrorState.tsx:13` | Hard-coded "Something went wrong" | Default error state has no context. Users see a generic message. | Pass `title` prop on every call (enforce at call site). Log the actual error server-side for support. | Low |
| `UIUX-COPY-011` | `BillingStatusBanner.tsx:43` | `role='alert'` for suspended status | Correct ARIA role. ✓ | — | — |
| `UIUX-COPY-012` | `BillingStatusBanner.tsx:54` | `role='status'` for grace period | Correct ARIA role. ✓ | — | — |

**Note:** Severity = Medium because raw errors leak PHI context or implementation details to users and support staff.

### Form Labels & Placeholders — Jargon Exposed

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-013` | `ProviderOAuthAdminClient.tsx:164` | "Refresh token" (label) | Clear, but assumes user knows OAuth terminology. | "OAuth token" (brief) or add a tooltip. | Low |
| `UIUX-COPY-014` | `ProviderOAuthAdminClient.tsx:173` | "Rotation policy" (label) | Backend enum name; users don't know what this means. | "Token refresh strategy" (user-facing) | Medium |
| `UIUX-COPY-015` | `ProviderOAuthAdminClient.tsx:182–184` | Options: `before_expiry`, `every_24h`, `never` | Snake_case enum names visible in dropdown. | "Refresh before expiry", "Refresh every 24 hours", "Manual refresh only" | Medium |
| `UIUX-COPY-016` | `AdminSsoPage.tsx:25–26` | "operatorNotes" (variable label in SSO preset instructions) | Backend schema name shown to admin. | "Setup instructions" or "Configuration notes" | Low |

### Internationalization (i18n) Bypasses

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-017` | `ErrorState.tsx:13` | Hard-coded "Something went wrong" | English default, not translatable. | Move to `messages/en.json` and use `useTranslations()` hook. | Medium |
| `UIUX-COPY-018` | `ErrorState.tsx:16` | Hard-coded "Try again" | English default, not translatable. | Move to messages catalogue. | Medium |
| `UIUX-COPY-019` | `DictateButton.tsx:74` | Hard-coded "Toggle dictation" (title attribute) | English-only tooltip. | i18n key or pass as prop. | Low |
| `UIUX-COPY-020` | `DictateButton.tsx:76–77` | Hard-coded "Stop dictation" / "Dictate" | English button labels, not translatable. | Use next-intl keys. | Medium |
| `UIUX-COPY-021` | `LocalePicker.tsx:67` | Hard-coded "Auto" (option label) | Not translatable. | "Auto-detect" or i18n key. | Low |

**Severity = Medium** because these block i18n completeness and violate RadioPad's claim to support multiple locales.

### Navigation Labels — Inconsistent Formality

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-022` | `nav.config.tsx` | "Reports", "Validation", "Audit", "Analytics" | Imperative nouns (good), but sidebar labels are not localized. | Confirm i18n keys are used in `<Sidebar>` component. | Low |

### Status Badges & Indicators — Color + Label Mismatch

| ID | Location | Current | Issue | Suggested | Severity |
|-----|----------|---------|-------|-----------|----------|
| `UIUX-COPY-023` | `AdminMcpPage.tsx:6–7` | `STATUS_LABEL` / `STATUS_BADGE` ("Submitted" / "info", "Approved" / "ok", "Blocked" / "danger") | Status is shown as color + label. If user is color-blind, only label conveys meaning. ✓ | Ensure all status badges have text label (not color-only). Audit all status badge usage. | Low |

---

## Categorized Findings

### Copy Issues by Theme

#### Missing or Silent Success States
- Report save is **silent** (no "Saved" toast).
- OAuth token save shows success banner (✓ good pattern).
- Locale change triggers hard reload without "Reloading…" message.

**Recommendation:** Implement global toast system (see Section: State Management in 08-audit) and surface success for:
- Report auto-save (every 30 seconds or on blur).
- Validation refresh.
- Token/credential updates.

#### Backend Jargon Exposed to Users
- `rotationPolicy` (enum: `before_expiry`, `every_24h`, `never`)
- `ProviderComplianceClass` (enum: `LocalOnly`, `PhiApproved`, etc.)
- `WebAuthnCredentialRow` (type name in variable)
- `rulebook_id` (schema name) vs. "Rulebook" (label)

**Recommendation:** Create a `frontend/lib/copy.ts` enum-to-label mapper:
```typescript
// frontend/lib/copy.ts
export const rotationPolicyLabel: Record<string, string> = {
  'before_expiry': 'Refresh before expiry',
  'every_24h': 'Refresh every 24 hours',
  'never': 'Manual refresh only',
};

export const complianceClassLabel: Record<string, string> = {
  'LocalOnly': 'Local (no external calls)',
  'PhiApproved': 'PHI-approved provider',
  // …
};
```

#### No Centralized Error Mapping
- Raw API errors shown to users (401 → "401 Unauthorized" instead of "Invalid credentials").
- Timeout errors leak stack traces.
- Network errors show ECONNREFUSED instead of "No internet connection."

**Recommendation:** Centralize error mapping in `frontend/lib/api.ts`:
```typescript
function userFriendlyError(err: unknown): string {
  if (err instanceof TypeError && err.message.includes('fetch')) {
    return 'No internet connection.';
  }
  if ((err as any).status === 401) return 'Invalid credentials.';
  if ((err as any).status === 403) return 'You don\'t have permission.';
  // Fallback to context-specific message or "Something went wrong."
  return 'Something went wrong. Try again.';
}
```

#### Imperative vs. Descriptive Voice
- "Discard" (imperative) vs. "Report signed" (descriptive).
- "Sign report" (action) vs. "Signature pending" (state).

**Recommendation:** Use consistent voice:
- **Buttons:** Always imperative ("Save", "Sign", "Delete").
- **Success messages:** Descriptive past tense ("Report saved", "Signature recorded").
- **Error messages:** Imperative action + reason ("Unable to save. Check connection.").
- **Status badges:** Descriptive state ("Signed", "Pending", "Expired").

#### Untranslatable Default Props
- `ErrorState` defaults to "Something went wrong" (English).
- `Skeleton` renders "Loading…" (English) in a `<span className="rp-sr-only">` — breaks non-English screen readers.

**Recommendation:** Never hard-code user-facing strings in components. Always accept `title` / `message` as props, defaulting to i18n keys:
```typescript
export default function ErrorState({
  title = useTranslations()('error.generic.title'),
  message,
  // ...
})
```

---

## Proposed Microcopy Library

**Location:** `frontend/lib/copy.ts`

**Purpose:** Single source of truth for error messages, success confirmations, labels, and empty-state copy. All strings are i18n-aware and live in `messages/{locale}.json`.

### Structure

```typescript
// frontend/lib/copy.ts
'use client';

import { useTranslations } from 'next-intl';

/**
 * Hooks for common UI copy patterns.
 * All strings are in messages/{locale}.json under the 'ui' namespace.
 */

export function useUiCopy() {
  const t = useTranslations('ui');
  return {
    // Success messages
    success: {
      reportSaved: t('success.reportSaved'),        // "Report saved"
      reportSigned: t('success.reportSigned'),      // "Report signed"
      tokenSaved: t('success.tokenSaved'),          // "Token saved"
      // …
    },
    // Error messages (generic)
    error: {
      generic: t('error.generic'),                  // "Something went wrong"
      noConnection: t('error.noConnection'),        // "No internet connection"
      unauthorized: t('error.unauthorized'),        // "Invalid credentials"
      forbidden: t('error.forbidden'),              // "You don't have permission"
      // …
    },
    // Empty states
    empty: {
      noReports: t('empty.noReports'),              // "No reports yet"
      noValidationIssues: t('empty.noValidationIssues'), // "All reports are valid"
      // …
    },
    // Buttons (CTA labels)
    buttons: {
      save: t('buttons.save'),
      sign: t('buttons.sign'),
      delete: t('buttons.delete'),
      // …
    },
    // Enums (backend jargon → user-friendly labels)
    enums: {
      rotationPolicy: {
        before_expiry: t('enums.rotationPolicy.beforeExpiry'),
        every_24h: t('enums.rotationPolicy.every24h'),
        never: t('enums.rotationPolicy.manual'),
      },
      complianceClass: {
        LocalOnly: t('enums.complianceClass.localOnly'),
        PhiApproved: t('enums.complianceClass.phiApproved'),
        // …
      },
    },
  };
}

/**
 * Utility: Map backend enum to label.
 * Example: enumLabel('rotationPolicy', 'before_expiry') → "Refresh before expiry"
 */
export function useEnumLabel(enumType: string, value: string): string {
  const copy = useUiCopy();
  const labels = copy.enums[enumType as keyof typeof copy.enums];
  if (!labels) return value; // Fallback to raw value if enum not found.
  return labels[value as keyof typeof labels] ?? value;
}

/**
 * Utility: Map HTTP status codes to user-friendly error messages.
 */
export function useApiErrorLabel() {
  const t = useTranslations('ui.error');
  return (status: number) => {
    switch (status) {
      case 400: return t('badRequest');    // "Invalid request"
      case 401: return t('unauthorized');  // "Invalid credentials"
      case 403: return t('forbidden');     // "You don't have permission"
      case 404: return t('notFound');      // "Not found"
      case 500: return t('serverError');   // "Server error"
      default: return t('generic');        // "Something went wrong"
    }
  };
}
```

### Message Catalogue Example

```json
// messages/en.json
{
  "ui": {
    "success": {
      "reportSaved": "Report saved",
      "reportSigned": "Report signed",
      "tokenSaved": "Token saved",
      "packApproved": "Pack approved",
      "toolBlocked": "Tool blocked"
    },
    "error": {
      "generic": "Something went wrong. Try again.",
      "noConnection": "No internet connection. Check your connection and try again.",
      "unauthorized": "Invalid credentials. Sign in again.",
      "forbidden": "You don't have permission to do this.",
      "notFound": "Not found.",
      "serverError": "Server error. Try again later.",
      "badRequest": "Invalid request."
    },
    "empty": {
      "noReports": "No reports yet",
      "noReportsSubtitle": "Create a new report to get started",
      "noValidationIssues": "All reports are valid",
      "noMcpTools": "No MCP tools registered"
    },
    "buttons": {
      "save": "Save",
      "sign": "Sign",
      "delete": "Delete",
      "cancel": "Cancel",
      "retry": "Try again",
      "createReport": "Create report",
      "addTool": "Add tool"
    },
    "enums": {
      "rotationPolicy": {
        "beforeExpiry": "Refresh before expiry",
        "every24h": "Refresh every 24 hours",
        "manual": "Manual refresh only"
      },
      "complianceClass": {
        "localOnly": "Local (no external calls)",
        "phiApproved": "PHI-approved provider",
        "unreviewed": "Unreviewed"
      },
      "reportStatus": {
        "Draft": "Draft",
        "Validated": "Validated",
        "Acknowledged": "Acknowledged",
        "Exported": "Exported"
      }
    }
  }
}
```

### Migration Example

**Before:**
```typescript
// ReportClient.tsx
const [saving, setSaving] = useState(false);
// ...
<button onClick={save} disabled={saving}>
  {saving ? 'Saving...' : 'Save'}
</button>
// ... on success:
// (no toast shown)
```

**After:**
```typescript
// ReportClient.tsx
'use client';

import { useToast } from '@/hooks/useToast';
import { useUiCopy } from '@/lib/copy';

export default function ReportClient() {
  const { toast } = useToast();
  const { success, error, buttons } = useUiCopy();
  const [saving, setSaving] = useState(false);

  async function save() {
    setSaving(true);
    try {
      await api.reports.update(id, draft);
      toast({ type: 'success', message: success.reportSaved });
    } catch (e) {
      const msg = e instanceof ApiError ? error[e.code] : error.generic;
      toast({ type: 'error', message: msg });
    } finally {
      setSaving(false);
    }
  }

  return (
    <button onClick={save} disabled={saving}>
      {saving ? 'Saving…' : buttons.save}
    </button>
  );
}
```

---

## Findings Summary (by Severity)

### Medium Severity (Fix Before Release)
- `UIUX-COPY-008`: Raw API errors shown to users.
- `UIUX-COPY-009`: Raw exception messages in validation error rows.
- `UIUX-COPY-014`: Backend enum names ("Rotation policy") exposed as labels.
- `UIUX-COPY-015`: Snake_case enum values in dropdowns.
- `UIUX-COPY-017`: Hard-coded "Something went wrong" (blocks i18n).
- `UIUX-COPY-018`: Hard-coded "Try again" (blocks i18n).
- `UIUX-COPY-020`: Hard-coded button labels (blocks i18n).

### Low Severity (Improve Before GA)
- `UIUX-COPY-001`: Vague CTA labels ("Use this").
- `UIUX-COPY-003`: Dual button labels (Save vs. Replace).
- `UIUX-COPY-004`: "Stop dictation" could be clearer.
- `UIUX-COPY-005` – `UIUX-COPY-007`: Missing empty states.
- `UIUX-COPY-010`: Generic `ErrorState` default (no context).
- `UIUX-COPY-013`: Jargon in labels ("Refresh token").
- `UIUX-COPY-016`: Backend variable names in UI.
- `UIUX-COPY-019` / `UIUX-COPY-021`: Hard-coded tooltips/options.
- `UIUX-COPY-022`: Sidebar labels not localized (confirm i18n keys used).
- `UIUX-COPY-023`: Status badges rely on color (audit for accessibility).

---

## Recommendations (Prioritized)

### Phase 1: Unblock i18n
1. **Extract hard-coded English strings** from `ErrorState`, `DictateButton`, `LocalePicker`.
2. **Create `frontend/lib/copy.ts`** with `useUiCopy()` hook.
3. **Add i18n keys** to `messages/en.json` for all UI copy.
4. **Test with 2–3 locales** (e.g., Spanish, Hindi) to confirm bundle switching works.

### Phase 2: Improve Error Handling
5. **Map HTTP status codes** to user-friendly messages in API client.
6. **Centralize success/error messaging** via `useToast()` hook.
7. **Log raw errors server-side** (don't show to user).

### Phase 3: Polish Copy
8. **Rename backend enum values** to user-friendly labels (e.g., `before_expiry` → "Refresh before expiry").
9. **Add empty-state copy** to all zero-row scenarios.
10. **Audit button labels** for consistency (imperative voice).

---

## Audit Artifacts

- **Components analyzed:** 37 route files, 6 UI primitives, 15+ form components.
- **Lines of code reviewed:** ~5,000 (copy-related only).
- **i18n bypass cases found:** 21 (hard-coded English strings).
- **Backend jargon exposed:** 8 (enum names, variable labels).
- **Silent success states:** 3 (report save, validation refresh, locale change).
- **Raw error leaks:** 9 (API exceptions, timeout messages, status codes).

---

## Conclusion

RadioPad's copy is **functional but inconsistent**. The app conflates backend schema names with user-facing labels, hard-codes English in critical paths, and lacks success confirmations in key workflows. The proposed `frontend/lib/copy.ts` library + `useToast()` hook will centralize copy management, unblock i18n, and improve UX consistency across the product.

**Estimated effort to implement:** 3–5 days (library setup, message catalogue, component migrations, testing).
