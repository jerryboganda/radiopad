**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Component Inventory

| Component | File | Used In | Design Role | Variants | Issues Found |
|---|---|---|---|---|---|
| `AppShell` | `frontend/components/shell/AppShell.tsx` | Root layout | Canonical sidebar shell | Collapsed desktop, mobile drawer | Wraps login, mobile, and pairing routes where a reduced shell may be preferable. |
| `Sidebar` | `frontend/components/shell/Sidebar.tsx` | Shell | Primary navigation | Grouped nav, active state | Mobile drawer needs inert/focus-trap behavior. |
| `Topbar` | `frontend/components/shell/Topbar.tsx` | Shell | Context/actions bar | Mobile menu button, breadcrumbs/actions | No skip link target integration found. |
| `Breadcrumbs` | `frontend/components/shell/Breadcrumbs.tsx` | Shell | Location context | Generated breadcrumbs | Sparse page-level integration. |
| `PageActionsSlot` | `frontend/components/shell/PageActionsSlot.tsx` | Shell | Header/topbar actions | Provider/slot | Underused across manual pages. |
| `Container` | `frontend/components/shell/Container.tsx` | Dashboard, validation, providers, rulebooks, templates | Page width wrapper | `fluid` | Many pages still use raw `div.rp-container`. |
| `PageHeader` | `frontend/components/shell/PageHeader.tsx` | Dashboard, validation, providers, rulebooks, templates | Canonical page heading/action layout | actions/kicker | Many pages use manual headings/actions. |
| `ProfileMenu` | `frontend/components/shell/ProfileMenu.tsx` | Shell | Profile and locale controls | Popover/menu | Uses menu role with form controls. |
| `LocalePicker` | `frontend/components/LocalePicker.tsx` | Shell | Locale selector | select | Needs review inside popover semantics. |
| `BillingStatusBanner` | `frontend/components/BillingStatusBanner.tsx` | Shell | Billing state warning | Global banner | Global on all routes, including auth/mobile. |
| `DesktopStatusBanner` | `frontend/components/DesktopStatusBanner.tsx` | Shell | Desktop backend state | Tauri event states | Passive failure recovery UX. |
| `EmptyState` | `frontend/components/ui/EmptyState.tsx` | Dashboard | Empty-state primitive | icon/action | Underused. |
| `ErrorState` | `frontend/components/ui/ErrorState.tsx` | Dashboard | Error-state primitive | retry | Underused. |
| `Skeleton` / `TableSkeleton` | `frontend/components/ui/Skeleton.tsx` | Dashboard | Loading primitive | block/text/row/table | Underused. |
| `StatusBadge` | `frontend/components/ui/StatusBadge.tsx` | Dashboard | Badge primitive | neutral/info/success/warning/danger/ai | Most pages use raw `.badge`. |
| `ReportClient` | `frontend/app/reports/[id]/ReportClient.tsx` | `/reports/view` | Main clinical editor | AI, rewrite, prior compare, validation, sign/export | Large monolith with dense actions and accessibility issues. |
| `CopyToRisButton` | `frontend/app/reports/[id]/CopyToRisButton.tsx` | Report editor | Secure clipboard action | Tauri/browser | Good domain component; preserve pattern. |
| `PriorComparePanel` | `frontend/app/reports/[id]/PriorComparePanel.tsx` | Report editor | Prior comparison | diff/fallback | Useful domain panel; needs responsive review. |
| `RewriteStylePanel` | `frontend/app/reports/[id]/RewriteStylePanel.tsx` | Report editor | AI rewrite/style panel | samples/diff | Page-local AI panel. |
| Rulebook editor panels | `frontend/app/rulebooks/editor/*Panel.tsx` | `/rulebooks/editor` | Rulebook visual editor sections | metadata/style/sections/rules/prompts | Modular but inherits split responsiveness issue. |
| `RulebookDetailClient` | `frontend/app/rulebooks/[id]/RulebookDetailClient.tsx` | `/rulebooks/view` | Rulebook YAML/detail view | YAML/visual tabs | Toolbar grouping needs mobile review. |
| `ProviderOAuthAdminClient` | `frontend/app/admin/providers/[id]/ProviderOAuthAdminClient.tsx` | `/admin/providers/oauth` | OAuth token admin | status/save/delete | Query route wrapper. |
| Mobile clients | `frontend/app/mobile/**/Mobile*Client.tsx` | Mobile dictate/edit/sign | Touch mobile workflows | dictate/edit/sign | Full app shell wrapper and action-row crowding risks. |
| Provider sandbox compare panel | `frontend/app/providers/page.tsx` | `/providers` | Provider comparison | 2-4 providers | Repeated with model-eval logic. |
| Local KPI/stat components | Multiple pages | Analytics, governance, usage, billing | Metric tiles | varied | Repeated patterns; inconsistent semantics. |
| CSS primitives | `globals.css`, `radiopad.css`, `shell.css` | All pages | Tokens, panels, badges, tables, mobile, tabs | many | Some legacy/undefined classes and token drift remain. |

## Design-System Consistency Findings

| Issue ID | Severity | Problem | Evidence | Recommendation |
|---|---|---|---|---|
| DS-01 | Medium | Query-param detail routes coexist with component folders named like dynamic routes, which can confuse routing ownership. | `frontend/lib/routes.ts`, component folders under `[id]` and `[reportId]`. | Document route pattern or convert to direct dynamic routes if static export constraints allow. |
| DS-02 | High | Shared `EmptyState`, `ErrorState`, `Skeleton`, and `StatusBadge` are underused. | Dashboard uses primitives; audit/templates/providers often use plain rows/text/banners. | Make these required page-state primitives for data-driven pages. |
| DS-03 | Medium | Header/container drift creates inconsistent page spacing and action placement. | Some pages use `Container` + `PageHeader`; others use raw `rp-container` and manual headings. | Standardize all pages on `Container` + `PageHeader`. |
| DS-04 | High | Inline-style drift bypasses locked responsive CSS. | Report editor, analytics, governance, rulebooks, templates, prompts contain many inline layout values. | Move recurring layout needs into locked classes and docs. |
| DS-05 | Medium | Undefined token `--green-soft` is referenced. | `.banner.ok` in `radiopad.css`; tokens define `--green-border`, not `--green-soft`. | Replace with `--green-border` or add documented token if truly needed. |
| DS-06 | Medium | Orphaned routes are not present in primary navigation. | `/admin/copilot`, `/admin/mcp`, `/admin/sso`, `/admin/validation-packs`, `/pair`, mobile routes. | Define intended IA: nav item, contextual entry point, or hidden/internal route label. |
| DS-07 | Medium | Duplicate governance surfaces exist. | `/governance` and `/admin/governance`. | Keep one canonical governance route or clearly separate roles. |
| DS-08 | Medium | No route-level loading/error/not-found boundaries were found. | No `loading.tsx`, `error.tsx`, `not-found.tsx` in inspected app routes. | Add App Router boundaries for major sections. |
