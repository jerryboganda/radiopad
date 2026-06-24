# RadioPad — Design System (LOCKED tokens, EXTENDED shell)

**Status:** Tokens locked · Shell modernized (sidebar)  **Owner:** Product Design  **Last Updated:** 2026-05-16

> **MISSION-CRITICAL RULE.** RadioPad's **visual tokens** (palette,
> typography, accent `#c96442`, semantic families, radii, shadows,
> `.ai-mark`, serif-for-prose / sans-for-chrome) are LOCKED. New UI work
> must reuse them verbatim — never invent a new colour, typeface, or
> dark-mode variant.
>
> The **app shell** has been modernized from the original Open Design
> topbar+split layout into an enterprise-SaaS **left-sidebar shell**
> (sidebar + slim contextual topbar + page header). The sidebar shell is
> now canonical and is documented in §3.1 below. The two-pane `.split`
> primitive is preserved as an *in-page* editor primitive (used inside
> `/reports/view` and `/rulebooks/editor`) but is no longer the app shell.

This document is the source of truth referenced by:
- `AGENTS.md`
- `.github/copilot-instructions.md`
- `CLAUDE.md`
- `/memories/repo/radiopad-design-lock.md`

If those files disagree with this document, this document wins.

---

## 1. Design philosophy

RadioPad inherits Open Design's philosophy:

- **Warm paper, not cold console.** Light cream backgrounds (`#faf9f7`),
  hairline borders, soft shadows. Avoid generic dark-grey "developer tool"
  aesthetics.
- **Serif for prose, sans for UI chrome, mono for code.** Reports and
  AI-drafted narrative use the serif stack; controls use the sans stack;
  rule IDs / accession numbers / hashes use mono.
- **One accent.** Claude rust / burnt-sienna (`#c96442`). No additional
  brand colours. Status semantics use the dedicated semantic tints below.
- **Calm, document-like surfaces.** A reporting workspace should feel like
  a bound report, not a dashboard.
- **Hairline > heavyweight.** Borders are 1px, panels separated by lines
  rather than drop shadows wherever possible.

---

## 2. Design tokens (canonical)

These are exported as CSS custom properties on `:root` in
`frontend/app/globals.css`. Do not redefine them inline.

### 2.1 Surfaces

| Token | Value | Use |
| --- | --- | --- |
| `--bg` / `--bg-app` | `#faf9f7` | App background, topbar |
| `--bg-panel` | `#ffffff` | Panels, cards, composer |
| `--bg-subtle` | `#f4f2ed` | Hover, secondary surfaces, code chips |
| `--bg-muted` | `#ece9e2` | Pressed/selected backgrounds |
| `--bg-elevated` | `#ffffff` | Popovers, modals |

### 2.2 Borders

| Token | Value |
| --- | --- |
| `--border` | `#ebe8e1` |
| `--border-strong` | `#d8d4cb` |
| `--border-soft` | `#f1eee7` |

### 2.3 Text

| Token | Value | Use |
| --- | --- | --- |
| `--text` | `#1a1916` | Body |
| `--text-strong` | `#0d0c0a` | Titles |
| `--text-muted` | `#74716b` | Secondary |
| `--text-soft` | `#989590` | Tertiary |
| `--text-faint` | `#b3b0a8` | Placeholder |

### 2.4 Accent

| Token | Value |
| --- | --- |
| `--accent` | `#c96442` |
| `--accent-strong` / `--accent-hover` | `#b45a3b` |
| `--accent-soft` | `#f5d8cb` (focus halo) |
| `--accent-tint` | `#fbeee5` (tinted hover) |

### 2.5 Semantic tints (status / severity / categories)

| Family | Foreground | Background | Border |
| --- | --- | --- | --- |
| Green (success) | `#1f7a3a` | `#e8f7ee` | `#c6ead2` |
| Blue (info) | `#2348b8` | `#e8efff` | `#c8d6ff` |
| Purple (AI) | `#6c3aa6` | `#f3ecf9` | `#e4d4f1` |
| Red (blocker) | `#9c2a25` | `#fdecea` | `#f5c6c2` |
| Amber (warning) | `#b26200` | `#fff3e0` | (auto) |

In RadioPad these map to validation severity:
- **Blocker** → Red family
- **Warning** → Amber family
- **Info** → Blue family
- **AI-generated content highlight** → Purple family

### 2.6 Radii

`--radius-sm: 6px`, `--radius: 10px`, `--radius-lg: 14px`, `--radius-pill: 999px`.

### 2.7 Shadows

`--shadow-xs`, `--shadow-sm`, `--shadow-md`, `--shadow-lg` as defined in
`globals.css`. Use the lightest one that solves the problem.

### 2.8 Typography

| Token | Stack |
| --- | --- |
| `--serif` | `Source Serif Pro, Source Serif 4, Iowan Old Style, Apple Garamond, Georgia, serif` |
| `--sans` | `-apple-system, BlinkMacSystemFont, Inter, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif` |
| `--mono` | `ui-monospace, SF Mono, SFMono-Regular, Menlo, Consolas, monospace` |

Body: **13.5px** / line-height **1.5**. Headings step up from this base.
Reports and AI prose render in `--serif`.

---

## 3. Components & class API

The canonical stylesheet ships these classes. Reuse them; do not redefine.

### 3.1 App shell (sidebar — canonical)

RadioPad uses a fixed left sidebar + slim contextual topbar + page header
shell. The sidebar carries primary navigation; the topbar is contextual
only (page actions, profile, locale).

```html
<div class="rp-shell">
  <aside class="rp-sidebar" aria-label="Primary">
    <a class="rp-sidebar-brand" href="/">
      <span class="brand-mark"><span class="brand-mark-letter">R</span></span>
      <span class="rp-sidebar-brand-text">
        <span class="rp-sidebar-brand-title">RadioPad</span>
        <span class="rp-sidebar-brand-meta">AI radiology reporting · v0.1</span>
      </span>
    </a>
    <nav class="rp-sidebar-nav">
      <div class="rp-sidebar-group">
        <div class="rp-sidebar-group-label">Workspace</div>
        <a class="rp-sidebar-item active" href="/">Reports</a>
        <a class="rp-sidebar-item" href="/validation">Validation</a>
        …
      </div>
      … more groups …
    </nav>
    <div class="rp-sidebar-footer">
      <button class="rp-profile-trigger">…profile…</button>
    </div>
  </aside>
  <div class="rp-shell-main">
    <header class="rp-topbar">
      <button class="rp-topbar-menu" aria-label="Open menu">≡</button>
      <nav class="rp-breadcrumbs">…</nav>
      <div class="rp-topbar-actions">…locale, profile, page action slot…</div>
    </header>
    <main class="rp-shell-content">
      <header class="rp-page-header">
        <h1 class="rp-page-title">Reports</h1>
        <p class="rp-page-sub">RadioPad Dev Tenant — signed in as …</p>
      </header>
      … page content …
    </main>
  </div>
</div>
```

**IA grouping (canonical 4 sections):**
- **Workspace** — Reports, Validation, Audit, Analytics
- **Library** — Rulebooks, Templates, Prompts, Marketplace, Terminology
- **Integrations** — Providers, PACS, FHIR import, Offline
- **Admin** — Governance, Model eval, Security, Feature flags, Billing, Usage, Settings

Sidebar is collapsible to icon-only on desktop (state persisted in
`localStorage`). On viewports `≤900px` the sidebar becomes a left
slide-out drawer triggered by the topbar hamburger; the drawer traps
focus, closes on `Escape` / backdrop click, and respects
`prefers-reduced-motion`.

#### 3.1.1 In-page two-pane primitive (`.split`)

`.split` defaults to `grid-template-columns: minmax(380px, 460px) 1fr`
and is preserved for **in-page** editor surfaces (study context · editor
in `/reports/view`; rulebook tree · YAML in `/rulebooks/editor`). It is
no longer the app shell. Pages that use `.split` should opt out of the
default `<PageHeader>` and request a full-bleed `<Container fluid>`.

#### 3.1.2 Legacy `.topbar` / `.app` classes

The original Open Design `.topbar` and `.app` classes still exist in
`globals.css` as **per-pane chrome** primitives (used inside the
two-pane editor surfaces above). They must not be used as the
application root layout — the `.rp-shell` sidebar shell is canonical.

### 3.2 Buttons

| Class | Use |
| --- | --- |
| (none) | Default — secondary action |
| `.primary` | Sienna fill, primary CTA (one per surface) |
| `.primary-ghost` | Sienna outline, secondary CTA |
| `.ghost` | Transparent, tertiary |
| `.subtle` | Subtle filled surface |
| `.icon-btn` | Icon-only, square padding |

Disabled state is `opacity: 0.5; cursor: not-allowed`. Focus ring is
`2px solid var(--accent)` with `2px` offset.

### 3.3 Inputs / textareas / selects

All inputs share the same shell: 1px `--border`, 6px radius, white panel,
focus → `--accent` border + 3px `--accent-soft` halo. Always render labels
as small caps muted text above the field (see `.section-block label` in
the legacy CSS).

### 3.4 Messages (chat & report sections)

`.msg`, `.msg.user`, `.msg.assistant`, `.msg.error`. AI-drafted content
wraps in a `--purple` left-rule block (see §4). Errors use the red family
(`--red-bg` + `--red-border` + `--red`).

### 3.5 Composer

`.composer` + `.composer-shell` are the canonical sticky-bottom input
pattern. RadioPad uses this for free-text dictation paste, AI prompts in
Prompt Studio, and rulebook YAML edits.

### 3.6 Pills, badges, status

Use `border-radius: var(--radius-pill)` and the semantic family tokens.
Rule IDs / accession numbers / hashes render in `code` (mono, subtle bg).

---

## 4. RadioPad-specific patterns (built on the locked tokens)

These RadioPad-only patterns extend (never replace) the Open Design system.

### 4.1 AI-draft highlight

Any text written by the AI gateway must visually distinguish itself until
acknowledged. Pattern:

```css
.ai-mark {
  background: var(--purple-bg);
  border-left: 3px solid var(--purple);
  border-radius: var(--radius-sm);
  padding: 8px 10px;
}
.ai-mark::before {
  content: "AI draft — review required";
  display: block;
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--purple);
  margin-bottom: 4px;
}
```

The marker disappears the moment the radiologist edits the section OR
clicks **Acknowledge**.

### 4.2 Validation finding rows

`.finding.blocker` (red), `.finding.warning` (amber), `.finding.info`
(blue). All three use a 3px left rule and `--bg-subtle` background. The
rule id renders in `font-family: var(--mono)` underneath the message.

### 4.3 Provider compliance pills

Provider rows display a compliance pill using the semantic family that
matches its compliance class:

| Compliance class | Pill family |
| --- | --- |
| `Blocked` | Red |
| `Sandbox` | Amber |
| `DeIdentifiedOnly` | Blue |
| `PhiApproved` | Green |
| `LocalOnly` | Purple |

### 4.4 Report narrative

The Findings, Impression, and Recommendations sections render in the
serif stack at 14.5px / 1.6 line-height. Section headings are small caps
in the muted token. This is what makes a RadioPad report feel like a
report rather than a form.

### 4.5 Admin dashboard helpers

Admin surfaces may use the lightweight `rp-` helpers in
`frontend/app/radiopad.css` for dense operational layouts: `.rp-grid-3`,
`.rp-list`, `.rp-stat-label`, `.rp-stat-value`, `.rp-divider-row`,
`.rp-actions`, `.rp-subtle-link`, `.rp-stat-tile`
(with `.rp-stat-tile-row` and `.rp-stat-sub` slots), and `.rp-banner`
(modifiers `.warn`, `.info`, `.danger`). These helpers only compose
locked tokens; they do not introduce new colours, radii, or shadow
semantics.

`.rp-stat-tile` is used by `/admin/billing` (BILL-002) to render an AI
credit dimension as a panel with a coloured `.badge ok|warn|danger`
reflecting `used / limit` (≥90% → warn, ≥100% → danger). `.rp-banner`
mirrors the `.banner` family with the `rp-` prefix so admin pages can
keep a single namespace; `/admin/billing` (BILL-007) uses
`.rp-banner.warn` for the trial-expiry countdown when a Trial tenant
has 3 days or less remaining.

### 4.6 Report editor — rewrite menu

The "Rewrite ▾" toolbar button on the report editor opens a small
locked-token popover (`.rp-rewrite-menu` / `.rp-rewrite-popover`) listing
the four rewrite modes (Concise, Formal, Patient-friendly, Referring
summary). Each option (`.rp-rewrite-option` with a `.subtle` button)
shows a label and a muted hint. The returned text appears in a side
panel inside `.ai-mark`; when "Diff" is toggled, original and proposed
prose render side-by-side via `.rp-rewrite-diff` with `.rp-rewrite-pre`
preserving whitespace in the locked serif stack.

### 4.7 Tab control (`.rp-tabs`)

A pill-segmented selector for read-only browsers (Terminology page).
`.rp-tabs` is the container; `.rp-tab` is each segment; `.rp-tab.active`
is the selected state. Composes `--bg-subtle`, `--border`,
`--radius-pill`, and `--shadow-xs` only — no new tokens.

### 4.8 Iter-31 prior-compare grid (`.rp-grid-2`, `.rp-diff-*`)

The prior-report compare panel renders a current/prior pair per section
inside `.rp-grid-2` (two equal columns, 12px gap, collapses to 1fr at
≤720px). Each section pair is wrapped in `.rp-grid-2-row`
(`display: contents`) so the two `.section-block` children sit directly
in the grid tracks and stay visually paired across wrap. Sections that
differ are highlighted with `.rp-diff-add` (green family — additive)
on the current copy or `.rp-diff-remove` (red family — superseded) on
the prior copy. Both helpers compose `--green-bg`/`--green-border` or
`--red-bg`/`--red-border` only — no new tokens.

### 4.9 Iter-31 admin nav additions

The topbar gains `Usage` (`/admin/usage`) and `Feature flags`
(`/admin/feature-flags`) immediately after `Billing`. Both use the same
`<Link>` style as the rest of the nav; no new component classes.

### 4.10 Iter-34 governance dashboard (`/admin/governance`)

The governance dashboard at `/admin/governance` aggregates audit
integrity, AI policy posture, plan entitlements, and in-flight
approvals into a single read-only surface. It is built entirely on
the existing locked classes: `.rp-container`, `.rp-page-title`,
`.rp-page-sub`, `.rp-panel`, `.rp-panel-title`, `.rp-grid-3`,
`.rp-stat-label` / `.rp-stat-value`, `.rp-list`, `.rp-divider-row`,
`.rp-row` / `.rp-row-wrap` / `.rp-gap-sm`, `.rp-subtle-link`, the
semantic `.badge ok|info|warn|danger|ai` chips, and `.banner.warn`
for partial-failure messaging. **No new tokens or component classes
were introduced.** The topbar `Governance` link sits between
`Validation` and `Audit`; routing target `/admin/governance`.

### 4.11 Iter-36 mobile workflows (`/mobile/*`)

The mobile shell (Capacitor 6) wraps the same Next.js frontend that
web/desktop use. Three pages are tuned for touch: `/mobile/dictate/[id]`,
`/mobile/reports/[id]/edit`, and `/mobile/reports/[id]/sign`. They sit
on the locked Open Design tokens and reuse the existing app shell —
the topbar collapses naturally below the breakpoint via the new
`@media (max-width: 720px)` rule that stacks `.rp-workspace` and
`.rp-grid-3` to a single column.

New locked helpers in `frontend/app/radiopad.css`:

- `.rp-mobile` — page wrapper (max-width 720, vertical stack, 12–16 px
  padding). Composes `--bg`, no new tokens.
- `.rp-mic-btn` — large square mic tile used on the dictation page.
  Default state composes `--bg-panel` + `--border` + `--shadow-xs`;
  the `.recording` modifier swaps the surface to the locked red
  family (`--red-bg`, `--red-border`, `--red`).
- `.rp-transcript` — transcript surface in the locked serif stack
  (`var(--serif)`, 15 px / 1.55). The `data-empty="true"` attribute
  (set when no transcript yet) softens to `--text-muted` italic so
  the placeholder reads as quiet help text.
- `.rp-mobile-section` / `.rp-mobile-body` — collapsible
  `<details>`/`<summary>` panel pair used by the mobile editor; the
  summary is a 44 px tap target composing `--bg-panel` + `--border`.
- `.rp-ack-row` — checkbox row used on the sign-acknowledgement page.
  Composes the same `.rp-panel`-style surface so the two acknowledgement
  rows read as part of the same form.
- `.rp-pair-shell` / `.rp-pair-code-tile` / `.rp-pair-code` — narrow
  centred column for the desktop device-pairing page (`/pair`,
  RFC 8628). The code chip is a mono-font 32 px tile with 0.18em
  letter-spacing — large enough to read across the room, locked so
  no inline-style escape hatch is needed (iter-36).

AI-drafted prose continues to wear `.ai-mark`; validation findings
continue to use the locked severity classes
(`.finding.blocker|warning|info`). Export choices on the sign page use
a plain `<select>` so we do not introduce a new dropdown component.

### 4.12 Mobile breakpoint (locked)

`@media (max-width: 720px)` is the canonical mobile breakpoint. At and
below 720 px:

- `.rp-workspace` collapses to a single column.
- `.rp-grid-3` and `.rp-grid-2` collapse to a single column.
- `.rp-mobile` tightens its padding to 12 px.

This breakpoint is shared with the rest of the design system; do not
add additional values.

### 4.13 Rulebook visual editor (`.rp-drag-handle`, `.rp-drag-active`, `.rp-editor-block`)

The visual rulebook editor (`/rulebooks/editor`) enables drag-and-drop
composition of rulebook YAML without raw text editing. It uses the
locked `.split` / `.pane` shell with a visual editor on the left and a
live YAML preview on the right.

New locked helpers in `frontend/app/radiopad.css`:

- `.rp-drag-handle` — grab cursor indicator for draggable list items.
  Renders in `--text-faint`; switches to `cursor: grabbing` on
  `:active`. Used on section and rule rows.
- `.rp-drag-active` — highlight applied to the element being dragged or
  the drop-target row during `dragover`. Composes `--accent-tint`
  background and `--accent-soft` border — no new tokens.
- `.rp-editor-block` — visual block container wrapping each editor
  section (metadata, style, sections, rules, prompt blocks). Composes
  `--bg-panel`, `--border`, `--radius`, `--shadow-xs` — identical to
  the `.rp-panel` surface but with tighter padding for nested blocks.
- `.rp-editor-block.collapsed` — collapsed state for collapsible
  prompt block cards; tightens bottom padding.

These helpers only compose locked tokens; they do not introduce new
colours, radii, or shadow semantics.

### 4.14 Audit-fix additions (`.rp-yaml-preview`, `.rp-kpi-value`, `.rp-severity-label`, `.banner.ok`)

Added in the UI/UX audit fix pass, these helpers extract inline styles
into named classes that compose locked tokens only:

- `.rp-yaml-preview` — styled `<pre>` for the live YAML preview in the
  rulebook visual editor. Composes `--mono`, `--bg-subtle`, `--border`,
  `--radius-sm`. Replaces inline `fontFamily`/`fontSize`/`background`/
  `border`/`borderRadius` that violated the design lock.
- `.rp-kpi-value` — large serif stat value used by the analytics
  dashboard `Kpi` component. `font: 600 28px/1.2 var(--serif)`.
  Replaces inline `fontSize`/`fontFamily`.
- `.rp-severity-label` — uppercase severity heading (blocker / warning /
  info) used by the report findings panel. `font: 500 11px var(--sans);
  text-transform: uppercase; color: var(--text-muted)`. Replaces
  inline `font`/`textTransform`/`color`.
- `.banner.ok` — success variant of the inline banner, mirroring
  `.banner.warn` / `.banner.info` / `.banner.danger`. Composes
  `--green-bg`, `--green`, `--green-soft`. Used by admin/settings
   and admin/security pages.

### 4.15 Desktop backend status banner (`.rp-desktop-status`)

The Tauri shell emits backend-sidecar health events to the renderer. The
frontend renders those events as a single inline `.banner` immediately below
the topbar:

- `.rp-desktop-status` — full-width banner modifier that removes side radii
  and side borders so the message reads as part of the app chrome. It must be
  combined with `.banner.info`, `.banner.warn`, or `.banner.danger`; it does
  not introduce new colours.
- `.rp-desktop-status-meta` — optional mono metadata chip for restart attempt
  counts. Composes `--mono` and existing text sizing only.

Use cases:

- `starting` -> `.banner.info`
- `restarting` or `degraded` -> `.banner.warn`
- `failed` -> `.banner.danger`

The banner is hidden for `ready` and `disabled` states. No animation is used,
so the desktop app does not spend idle GPU/CPU on status chrome.

### 4.16 Report editor — ribbon, document & inspector

The report editor (`/reports/view`) replaces the legacy three-pane
`.rp-workspace` with a Microsoft-Word-style **ribbon** toolbar on top, the
report as a calm central **document**, and a tabbed right **inspector**. The
former `.rp-workspace` remains for other surfaces; only the report editor moves
to this layout. New locked helpers in `frontend/app/radiopad.css` (locked
tokens only — no new colours, radii, or shadow semantics):

- `.rp-doc-header` — flex strip holding Back, the study title
  (`.rp-doc-title`), the accession chip (`.rp-doc-accession`, mono on
  `--bg-subtle`), the `.rp-status` pill, and the auto-save indicator
  (`.rp-doc-saved`, pushed right with `margin-left: auto`).
- `.rp-ribbon` — panel-surface shell (`--bg-panel` / `--border` / `--radius` /
  `--shadow-xs`) containing the tab bar and the active tab's tools.
- `.rp-ribbon-tabbar` + `.rp-ribbon-tab` (+ `.is-active`) — the Home / Review /
  Export / Finalize tabs. Active tab carries the accent underline, matching the
  sidebar/inspector active idiom.
- `.rp-ribbon-surface` — flex row of tool groups for the active tab.
- `.rp-ribbon-group` + `.rp-ribbon-group-controls` + `.rp-ribbon-group-label` —
  a Word-style group: controls on top, an uppercase `--text-faint` label
  pinned to the bottom (`justify-content: space-between`).
- `.rp-ribbon-divider` — 1px `--border-soft` vertical rule between groups.
- `.rp-ribbon-field` (+ `> label`, `> select`) — compact labelled selector for
  the in-ribbon AI provider / Rulebook pickers on the Home tab.
- `.rp-report-body` — `minmax(0,1fr) minmax(260px,340px)` grid (document +
  inspector); collapses to one column at `≤960px`.
- `.rp-doc` — the report "paper": `.rp-panel`-style surface holding the section
  editors (narrative sections keep `.rp-narrative` + `.ai-mark`).
- `.rp-inspector` — sticky tabbed right panel. `.rp-inspector-tabbar` +
  `.rp-inspector-tab` (+ `.is-active`) switch Context / Checks / Sign-off;
  `.rp-inspector-body` pads the active panel. `.rp-inspector-body .rp-panel`
  resets nested panel chrome so the inspector itself is the only card.
- `.rp-menu` + `.rp-menu-popover` + `.rp-menu-item` — generic dropdown built on
  the `.rp-rewrite-popover` idiom, used by the `Export ▾` menu. Disabled items
  drop to `--text-faint` and keep `not-allowed`.

Validation severities still use the locked `.finding.blocker|warning|info`
classes and the `.rp-severity-label`; AI-drafted prose still wears `.ai-mark`.
No emoji are used (the former 🎙 voice toggle is now a text button).

---

## 5. Iconography

Use inline SVG only. No emoji in UI chrome. Stroke icons use `1.5px`
strokes and inherit `currentColor`. Brand mark is the warm sienna
gradient circle defined in `.brand-mark`.

---

## 6. Motion

120ms cubic ease for hover/focus transitions on background, border, and
shadow. No bouncy springs. No content reflows on hover.

---

## 7. Accessibility

- WCAG AA contrast against `--bg` and `--bg-panel` is verified for every
  text token in this doc.
- Focus rings must always be visible; `:focus-visible` ⇒ accent ring.
- Validation findings duplicate colour with explicit text labels
  (`Blocker`, `Warning`, `Info`) so colour-blind users have parity.
- AI-draft marker uses both colour and the explicit "AI draft — review
  required" caption above the highlighted block.

---

## 8. Don't list

The following are **forbidden**:

1. Importing Tailwind, Material UI, Ant Design, Chakra, Bootstrap.
2. Introducing dark-mode tokens.
3. Adding new accent colours.
4. Using emoji as functional icons.
5. Using a primary navigation pattern other than the canonical
   left-sidebar shell described in §3.1 (no header-heavy nav, no
   bottom-tab nav, no command-palette-only nav).
6. Adding heavy dropshadows that imply elevation > the existing
   `--shadow-md`.
7. Using rounded-full pills for things that aren't status.

---

## 9. Cheat sheet for new screens

When you build a new RadioPad surface:

1. The page renders inside `<AppShell>`; do not re-implement chrome.
2. Use `<Container>` + `<PageHeader title description primaryAction />`
   for the top of every page.
3. Inside the page, use `.rp-panel` for grouped content and
   `.section-block` (label + control) for every form field.
4. For data-driven pages, render `<Skeleton />` while loading,
   `<EmptyState />` for zero rows, and `<ErrorState onRetry />` on
   fetch failure.
5. Primary action uses `.primary` (one per surface); everything else
   uses `.ghost` / `.subtle`.
6. Status lives in semantic pills, not standalone colours.
7. AI-generated content always wears `.ai-mark`.
8. Reports render in serif; chrome and forms in sans.
9. Two-pane editor surfaces opt into `<Container fluid>` and use the
   in-page `.split` primitive (§3.1.1).

If a new pattern doesn't fit any of the above, stop and propose a token
or class addition in a PR before shipping it. Do not improvise.


---

## 5. Internationalization (Iter-35)

RadioPad ships chrome translations for **six locales**: `en` (default,
canonical source of truth), `es`, `de`, `fr`, `pt`, `hi`. The
locale picker lives in the topbar as a `select.subtle` extension of
the existing `.subtle` button class; no new design token is
introduced.

**Locale negotiation** (resolved on each request / first paint):

1. `?lang=<tag>` query parameter (writes the cookie + redirects).
2. `radiopad-locale` cookie (set by the picker, `SameSite=Lax`,
   1-year max-age, not `HttpOnly`).
3. `Accept-Language` header (best-of, with `q=` weighting).
4. Tenant default: `TenantSettings.Locale` exposed via
   `GET /api/tenant/settings/locale`.
5. `en` fallback.

The per-user override on `User.PreferredLocale` is set by
`PUT /api/users/me/locale`; any tenant member may write it. The tenant
default is set by `PUT /api/tenant/settings/locale` (`ItAdmin` /
`MedicalDirector` only).

**Clinical content stays English.** Rulebook YAML, finding/lexicon text,
and validation messages emitted by `RadioPad.Validation` are **never**
translated. Message bundles in `frontend/messages/*.json` deliberately
omit those keys; if a clinical surface needs localized chrome (severity
labels, banner copy), add it to the `validation.severity` /
`banner` namespaces; never to a "rulebook" namespace.

## 4.11 Iter-45 friendly-copy + widescreen layout

To make the product comfortable for non-technical radiologists on widescreen monitors, three additions to the locked token layer:

- .rp-container max-width raised from 1280px to 1600px so admin/settings pages fill more of the available canvas. .rp-container.fluid (no cap) and .rp-container.narrow (880px) remain available as opt-ins.
- .rp-page-grid � two-column page layout: `grid-template-columns: minmax(0,1fr) 320px`. Children: .rp-page-main (form/content) and .rp-page-aside (sticky help sidecar). Collapses to one column under 1080px so 13-inch laptops still get a comfortable single-column reading width.
- .rp-help / .rp-help-title � sidecar help card on the muted surface (`--bg-subtle` + `--border-soft`) for short `What you control here` / `Need help?` / `Privacy & safety` blocks.
- .rp-advanced � styled `<details>` element used to hide technical fields (env-var refs, URLs, JWT settings, raw JSON, sensitivity tuning) behind a single `Show advanced options` disclosure. `::-webkit-details-marker` is hidden; a rotating `?` glyph is used instead.

Copy rules added with this iteration:

- **No PRD codes, no iteration codes, no API paths** in user-visible JSX strings.
- **No raw acronyms** as labels (`WADO-RS`, `QIDO-RS`, `DICOMweb`, `CMK`, `KMS`, `SCIM`, `OIDC`, `RBAC`). When the underlying concept is still needed in copy, use plain-English language (`imaging archive`, `encryption key`, `single sign-on`) and tuck the technical detail inside .rp-advanced.
- **No env-var scheme samples** (`env:NAME`, `aws:arn:�`, `azkv:�`, `gcp:�`) shown to end users. If a placeholder is needed, use the existing configured value, never the raw scheme grammar.
- **No technical jargon as severity labels.** The severity dropdown asks a question (`How strict should the safety check be?`) and uses friendly answers (`Just show a note` / `Show a warning (recommended)` / `Block signing until reviewed`) while preserving the underlying `Info`/`Warning`/`Blocker` enum.

