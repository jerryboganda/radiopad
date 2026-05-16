# 04 — Component Inventory

**Scope:** All components under `frontend/components/**`. Inline page-level
client components (e.g. `frontend/app/reports/[id]/ReportClient.tsx`,
`frontend/app/rulebooks/editor/*Panel.tsx`,
`frontend/app/admin/providers/[id]/ProviderOAuthAdminClient.tsx`) are
audited in `05-page-by-page-audit.md` next to their parent pages.

There is **no generic primitive library** — no `<Button>`, `<Input>`,
`<Card>`, `<Modal>`, `<Tabs>`, `<Tooltip>` component. Pages compose the
locked CSS classes from `globals.css` directly. This is intentional given
the design-lock, but it does push consistency enforcement entirely into
review (see `09-frontend-structure-audit.md`).

## Shell components (`frontend/components/shell/`)

| Component | File | Used In | Design Role | Variants | Issues found (see audit IDs) |
|---|---|---|---|---|---|
| `AppShell` | `AppShell.tsx` | `app/layout.tsx` (single root) | Root layout — sidebar + topbar + page body. Wraps everything in `ShellProvider` + `PageActionsProvider`. | — | None at this layer; downstream pages must compose `<Container>` + `<PageHeader>` (many don't — see `UIUX-TOP-001..006`). |
| `Sidebar` | `Sidebar.tsx` | `AppShell` | Sticky left rail, brand, nav groups, collapse, profile footer. | desktop (full / collapsed) + mobile drawer | Touch targets too small (`UIUX-STR-016`); only 20 of 38 routes linked (`UIUX-FLOW-DISCOVER`). |
| `Topbar` | `Topbar.tsx` | `AppShell` | Mobile menu button + breadcrumbs + page-actions slot. | — | No skip link (`UIUX-CMP-001`). |
| `PageHeader` | `PageHeader.tsx` | Some pages only | Page title + description + actions row. | with/without `primaryAction`/`secondaryActions` | Action row doesn't stack on <480px (`UIUX-CMP-020`); only ~17 of 38 pages use it (`UIUX-TOP-001..006`, `UIUX-WS-*`, `UIUX-ADMIN-*`). |
| `Breadcrumbs` | `Breadcrumbs.tsx` | `Topbar` | Crumb trail with `aria-current="page"` on last item. | — | Inline `style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}` (line 16) — flagged `UIUX-CMP-INLINE`. |
| `Container` | `Container.tsx` | Some pages only | Constrains content width (max 1280px); `fluid` opt-out for `.split` editors. | default / `fluid` | Padding too wide on 320px (`UIUX-CMP-021`); duplicate `.rp-container` rule in `radiopad.css` (`UIUX-STR-001`). |
| `ProfileMenu` | `ProfileMenu.tsx` | `Sidebar` footer | Avatar trigger + popover with account/settings/billing/language/sign-out. | — | Popover has no focus trap (`UIUX-MOB-007`); inline `style={{ padding: '4px 6px' }}` (line 71); avatar derived only from first letter of email — falls back to `?`. |
| `MobileDrawerBackdrop` | `MobileDrawerBackdrop.tsx` | `AppShell` | Dim layer behind mobile sidebar drawer. | — | `aria-hidden` toggled but the dim layer remains in tab order — verify pointer-events off when closed. |
| `PageActionsSlot` | `PageActionsSlot.tsx` | `Topbar` + page bodies | Portal pattern (`<PageActions>` on a page → renders into topbar). | — | No type constraint on injected children → pages can inject anything (`UIUX-CMP-026`). |
| `ShellContext` | `ShellContext.tsx` | `AppShell` | Holds `collapsed` (persisted in localStorage `rp-shell-collapsed`) + `drawerOpen`; closes drawer on Escape. | — | Drawer state change not announced to AT (`UIUX-CMP-023`). |
| `nav.config` | `nav.config.tsx` | `Sidebar` | Single source of truth for primary IA — 4 groups, 20 items, hand-rolled SVG icons. | — | 18 product routes have no sidebar entry (`UIUX-FLOW-DISCOVER`). |

## UI primitives (`frontend/components/ui/`)

| Component | File | Used In | Design Role | Variants | Issues found |
|---|---|---|---|---|---|
| `StatusBadge` | `StatusBadge.tsx` | Reports & lists | Coloured pill mapping status enum → `.rp-status.{tone}`. Exports `reportStatusTone()`. | `neutral` / `info` / `success` / `warning` / `danger` / `ai` | Tone-class mapping not type-narrowed (`UIUX-CMP-014`); inconsistent use vs ad-hoc `<span className="badge ok">` patterns across pages (`UIUX-TOP-050`, `UIUX-TOP-051`). |
| `Skeleton` | `Skeleton.tsx` | Lists | Animated placeholder. `TableSkeleton` helper renders N rows × M cols. | `text` / `block` / `row` | Inline `style={{ display:'flex', gap:12, padding:'8px 0', borderBottom:'1px solid var(--border-soft)' }}` (line 22) — flagged `UIUX-STR-004`; missing `aria-busy` on the parent (`UIUX-CMP-022`); only ~30% of data pages actually use it. |
| `EmptyState` | `EmptyState.tsx` | Lists | Centered icon + title + description + action. `role="status"` (no `aria-live`). | with/without `icon`, `description`, `action` | `role="status"` should pair with `aria-live="polite"` (`UIUX-CMP-010`); only used by a minority of empty paths — most pages render `<p>No data</p>` inline (`UIUX-TOP-EMPTY-*`). |
| `ErrorState` | `ErrorState.tsx` | Lists | Centered icon + title + message + retry button. `role="alert"`. | with/without `onRetry`, custom `title` / `retryLabel` | Hard-coded English defaults (`'Something went wrong'`, `'Try again'`) — bypass next-intl (`UIUX-CMP-COPY`); retry button has no `focus-visible` ring (`UIUX-CMP-012`); icon fixed 18px (`UIUX-CMP-013`). |

## Feature components (`frontend/components/`)

| Component | File | Used In | Design Role | Variants | Issues found |
|---|---|---|---|---|---|
| `LocalePicker` | `LocalePicker.tsx` | `ProfileMenu` | Inline `<select>` (`.subtle`) for 6 locales + `auto`. Triggers `window.location.reload()` on change. | — | Hard reload destroys form state (`UIUX-CMP-024`). |
| `IntlBoundary` | `IntlBoundary.tsx` | `app/layout.tsx` | Resolves locale once on mount, wraps children in `NextIntlClientProvider` with fixed `UTC` timezone. | — | Locale fixed at first render; relies on hard reload to switch (coupled with `LocalePicker` above). |
| `DictateButton` | `DictateButton.tsx` | Mobile dictation, report editor | Web Speech API toggle; falls back to disabled if unsupported. | `idle` (`.subtle`) / `listening` (`.primary`) | No spoken indicator (only text + colour change) — fails WCAG 1.4.1; `lang` hard-coded `'en-US'` regardless of negotiated locale (`UIUX-MOB-DICTATE-LANG`). |
| `DesktopStatusBanner` | `DesktopStatusBanner.tsx` | `AppShell` (under billing banner) | Listens to `radiopad:backend-status` custom event from the Tauri shell; shows banner. | states: `starting` / `restarting` / `degraded` / `failed` (others hidden) | Dynamic className not type-narrowed (`UIUX-CMP-015`); no min-height for mobile touch (`UIUX-CMP-016`). |
| `BillingStatusBanner` | `BillingStatusBanner.tsx` | `AppShell` (top of every page) | Polls `api.billing.status()` every 5 min; shows grace-period or suspended notice. | `suspended` (alert, danger) / `gracePeriod` (status, warn) / hidden | Suspended uses `role="alert"` (correct) but grace uses `role="status"` — inconsistent (`UIUX-CMP-019`); no refresh indicator (`UIUX-CMP-017`); `<code>` date may overflow at 320px (`UIUX-CMP-018`). |

## Page-local client components (cited here for completeness)

These live under `frontend/app/**/[id]/` or `frontend/app/**/editor/` but
are imported as plain client components by sibling `page.tsx` files —
they are **not Next.js routes**.

| Component | File | Role |
|---|---|---|
| `RulebookDetailClient` | `app/rulebooks/[id]/RulebookDetailClient.tsx` | Read-only rulebook detail panel embedded by `rulebooks/view/page.tsx`. |
| `RulebookEditorClient` | `app/rulebooks/editor/RulebookEditorClient.tsx` | YAML editor shell embedded by `rulebooks/editor/page.tsx`. |
| `MetadataPanel` / `PromptBlocksPanel` / `StylePanel` / `RulesPanel` / `SectionsPanel` | `app/rulebooks/editor/*.tsx` | Editor sub-panels. Each has 5–9 inline `style={{…}}` occurrences (`UIUX-STR-018`). |
| `ReportClient` / `RewriteStylePanel` / `PriorComparePanel` / `CopyToRisButton` | `app/reports/[id]/*.tsx` | Report detail and side panels embedded by `reports/view/page.tsx`. |
| `ProviderOAuthAdminClient` | `app/admin/providers/[id]/ProviderOAuthAdminClient.tsx` | OAuth callback handler embedded by `admin/providers/oauth/page.tsx`. |

## Inventory health summary

- **20 shared components**, all locked to the design system except for
  the inline-style violations called out above.
- **No `<Modal>` primitive** despite multiple pages opening modal-like
  panels (rulebook editor save dialog, provider oauth confirmation,
  prompt-block creation via `window.prompt()`). This is the single
  biggest gap in the component library and the root cause of several
  HIGH-severity a11y findings (focus traps, escape handling, backdrop
  semantics).
- **No `<Tabs>` primitive** — `/prompts` (line ~405) and `/terminology`
  (lines 72–81) ship two different tab implementations
  (`UIUX-TOP-045`).
- **No `<Toast>` / `<SuccessState>`** — pages reuse `setError()` for
  success messaging (`UIUX-TOP-055`).
- **No `<ConfirmDialog>`** — destructive actions (e.g. delete
  rulebook, revoke pairing) rely on `window.confirm()` browser dialogs,
  which break the design language and screen-reader UX.
