# RadioPad — Design System (Hallmark tokens, EXTENDED shell)

**Status:** Hallmark token system (OKLCH) · Build-time Tailwind · Shell modernized (sidebar)  **Owner:** Product Design  **Last Updated:** 2026-06-26

> **MISSION-CRITICAL RULE.** RadioPad's visual identity is the **Hallmark
> "paper & ink" system** (ported from UBAG): a warm-paper, terracotta-accent,
> editorial palette expressed in **OKLCH**. The canonical token source is
> **`frontend/app/hallmark.css`** (OKLCH `--color-*` / `--font-*` / `--space-*`
> / `--text-*`) plus **`frontend/tailwind.config.ts`** (the matching Tailwind
> color/font/radius scales).
>
> RadioPad's **original 44 token names** (`--bg`, `--accent`, `--red`,
> semantic families, radii, shadows, `--serif`/`--sans`/`--mono`) remain the
> **stable contract** — they are re-pointed onto Hallmark via the alias layer
> in `hallmark.css`, so the existing class layer re-skins without a rewrite.
> Reuse those names verbatim; never reintroduce the old hex values, invent a
> new colour or typeface, or add a **dark-mode** variant (the system is
> **light-only**). To add a token, edit the Hallmark block in `hallmark.css`
> (and mirror it in `tailwind.config.ts`) — never inline.
>
> **Build-time Tailwind 3 is now part of the stack** (`@tailwind` directives
> in `globals.css`, config in `tailwind.config.ts`, PostCSS + Autoprefixer).
> Tailwind utilities compile to static CSS at build time and ship into the
> `output: 'export'` bundle (Tauri/Capacitor-safe). Utilities and the named
> Hallmark/RadioPad component classes may be mixed freely; the alias layer
> guarantees both resolve to the same tokens.
>
> The **app shell** has been modernized from the original Open Design
> topbar+split layout into an enterprise-SaaS **left-sidebar shell**
> (sidebar + slim contextual topbar + page header). The sidebar shell is
> now canonical and is documented in §3.1 below. The two-pane `.split`
> primitive is preserved as an *in-page* editor primitive (used inside
> `/reports/view` and `/rulebooks/editor`) but is no longer the app shell.

This document is the source of truth referenced by:
- `AGENTS.md`
- `CLAUDE.md`
- `/memories/repo/radiopad-design-lock.md`

If those files disagree with this document, this document wins.

---

## 1. Design philosophy

RadioPad inherits Open Design's philosophy, now expressed through the
Hallmark "paper & ink" system:

- **Warm paper, not cold console.** Light warm-paper backgrounds
  (`--color-paper` ≈ `oklch(96.5% 0.012 75)`), hairline rules, soft shadows.
  Avoid generic dark-grey "developer tool" aesthetics. Light-only — no dark
  palette.
- **Serif for prose, sans for UI chrome, mono for code.** Reports and
  AI-drafted narrative use the serif stack; controls use the sans/body stack;
  rule IDs / accession numbers / hashes use mono. Display headings use the
  Hallmark display stack (`--font-display`).
- **One accent.** Terracotta (`--color-accent` ≈ `oklch(58% 0.18 35)`). No
  additional brand colours. Status semantics use the dedicated semantic tints
  below.
- **Calm, document-like surfaces.** A reporting workspace should feel like
  a bound report, not a dashboard.
- **Hairline > heavyweight.** Borders are 1px, panels separated by lines
  rather than drop shadows wherever possible.

---

## 2. Design tokens (canonical)

The **canonical source** is the Hallmark layer in
**`frontend/app/hallmark.css`**: an OKLCH base set (`--color-*`, `--font-*`,
`--space-*`, `--text-*`) plus a **compatibility alias layer** that re-points
RadioPad's original 44 token names onto Hallmark. `globals.css` no longer
declares colour tokens — it only carries the `@tailwind` directives. The
matching Tailwind scales live in `frontend/tailwind.config.ts`. Do not
redefine tokens inline, and do not reintroduce the pre-migration hex values;
edit the Hallmark block instead.

The tables below give the **alias name** (the stable contract you write
against) and its **OKLCH value** (resolved via `hallmark.css`).

### 2.1 Surfaces

| Token | Value | Use |
| --- | --- | --- |
| `--bg` / `--bg-app` | `oklch(96.5% 0.012 75)` (paper) | App background, topbar |
| `--bg-panel` / `--bg-elevated` | `oklch(99% 0.006 75)` (paper-soft) | Panels, cards, popovers, modals |
| `--bg-subtle` | `oklch(93% 0.02 70)` (paper-warm) | Hover, secondary surfaces, code chips |
| `--bg-muted` | `color-mix(paper-warm + ink-mute 14%)` | Pressed/selected backgrounds |

### 2.2 Borders

| Token | Value |
| --- | --- |
| `--border` | `oklch(86% 0.014 70)` (rule) |
| `--border-strong` | `color-mix(rule + ink-mute 45%)` |
| `--border-soft` | `oklch(91% 0.01 70)` (rule-soft) |

### 2.3 Text

| Token | Value | Use |
| --- | --- | --- |
| `--text` | `oklch(20% 0.022 55)` (ink) | Body |
| `--text-strong` | `color-mix(ink + black 18%)` | Titles |
| `--text-muted` | `oklch(38% 0.018 55)` (ink-soft) | Secondary |
| `--text-soft` | `oklch(50% 0.012 60)` (ink-mute) | Tertiary |
| `--text-faint` | `color-mix(ink-mute + paper 35%)` | Placeholder |

### 2.4 Accent

| Token | Value |
| --- | --- |
| `--accent` | `oklch(58% 0.18 35)` (terracotta) |
| `--accent-strong` / `--accent-hover` | `oklch(42% 0.2 32)` (accent-deep) |
| `--accent-soft` | `oklch(82% 0.08 45)` (focus halo) |
| `--accent-tint` | `color-mix(accent-soft + paper 55%)` (tinted hover) |

### 2.5 Semantic tints (status / severity / categories)

OKLCH; each family maps a RadioPad alias onto a Hallmark hue. The AI/purple
family (`--color-ai`) is intentionally distinct from blue/marine — a clinical
safety affordance.

| Family | Foreground | Background |
| --- | --- | --- |
| Green (success) | `oklch(50% 0.09 150)` | `oklch(90% 0.04 145)` |
| Blue (info / marine) | `oklch(34% 0.09 240)` | `oklch(83% 0.045 240)` |
| Purple (AI) | `oklch(45% 0.14 300)` | `oklch(90% 0.05 300)` |
| Red (blocker / danger) | `oklch(52% 0.17 25)` | `oklch(89% 0.055 32)` |
| Amber (warning / saffron) | `color-mix(saffron + ink 45%)` | `oklch(91% 0.07 80)` |

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
      <div class="rp-topbar-actions">…check-for-updates, locale, profile, page action slot…</div>
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

**Check-for-updates control (`.rp-update`, desktop shell only).** A 36×36
top-bar icon button (`.rp-update-btn`, same chrome as `.rp-topbar-menu`)
that drives the Tauri auto-updater (DESK-001). It renders only inside the
Tauri webview and is absent on web/mobile. A silent check on launch shows an
accent dot (`.rp-update-dot`) when an update is waiting; clicking runs
check → download (live %) → install → relaunch. Status text
(`.rp-update-label`) uses the semantic families — blue for checking/
downloading, green for up-to-date, red for failure — and reuses existing
tokens only (no new tokens).

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

#### 3.3.1 Searchable combobox (`.rp-combobox*`)

A filterable replacement for a native `<select>` when the option list is long
enough to want type-to-filter (e.g. the report editor's 20+-entry Rulebook
picker). Component: `components/ui/SearchableSelect.tsx`; classes in
`globals.css`. Anatomy: a `.rp-combobox-trigger` button (restating the input
shell — `--border`, 6px radius, white panel, `--accent`/`--accent-soft` focus
ring — since a `<button>` doesn't inherit the element rules) with a
`.rp-combobox-value` (faint when `[data-placeholder]`) + `.rp-combobox-caret`,
opening a `.rp-combobox-panel` popover (`--bg-elevated`, `--shadow-md`,
`--radius`, mirrors `.rp-profile-popover`) that holds a `.rp-combobox-search`
input (inherits the input shell) over a `.rp-combobox-list` of
`.rp-combobox-option` rows (`hover`/`.is-active` → `--bg-subtle`;
`[aria-selected]` → `--accent-strong`; `[aria-disabled]` muted) and a
`.rp-combobox-empty` no-match state. Click-outside + Escape close it (the
ProfileMenu popover pattern). Use it ONLY where filtering helps — keep native
`<select>` elsewhere.

Checkbox rows inside `.rp-profile-popover` (the ProfileMenu "Dictation"
toggles) add `.rp-profile-popover-check` with a `.rp-profile-check-label` span
around the text. Because the popover is narrow, these labels wrap to 2–3 lines,
so the row is `align-items: flex-start` (box top-aligned with the first line,
not centred against the whole block — centring leaves the checkbox floating
mid-row and reads as broken) and the box uses `accent-color: var(--accent)`. Containers must not `overflow: hidden` around it or the
popover is clipped (see §4.16 — the inspector is `overflow: visible` for this).

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

### 3.7 Card grid & chips (`.rp-card*`, `.rp-chip*`)

Browsable collection landing pages (e.g. **Rulebooks** `/rulebooks`) use a
responsive card grid instead of a dense table. Anatomy:

- `.rp-card-grid` — `auto-fill` grid, `minmax(256px, 1fr)` columns, `14px` gap.
- `.rp-card` — clickable surface (`--bg-panel`, hairline border, `--shadow-xs`);
  lifts on hover (`--border-strong`, `--shadow-md`, `translateY(-1px)`) and shows
  an accent focus ring. Use a real `<button>`/`<a>` so it is keyboard-focusable.
  - `.rp-card-head` — title row (flex, space-between) holding `.rp-card-title`
    (`--text-strong`, 15px) and a status `.badge`.
  - `.rp-card-id` — the machine id in `--mono`/`--text-muted`.
  - `.rp-chip-row` of `.rp-chip` — pill tags for metadata (modality, body part).
  - `.rp-card-meta` — muted footer line (version · owner · updated).
  - `.rp-card-actions` — pushed to the bottom (`margin-top:auto`); inner buttons
    must `stopPropagation()` so they don't re-trigger the card's own click.
- `.rp-filter-bar` + `.rp-search` (max-width 320px) — search/filter row above the
  grid; pairs with `.rp-tabs`/`.badge` status filters.

### 3.8 Sticky toolbar (`.rp-toolbar.sticky`)

`.rp-toolbar.sticky` pins an action row (Cancel/Validate/Save/Publish) to the top
of a scrolling editor (`--bg-app`, bottom hairline, `z-index:5`). Pair with
`.split.rp-editor-split` (a wider left column, `minmax(420px, 0.9fr) 1fr`,
collapsing to one column ≤1100px) for visual editors with stacked form panels.

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
  family (`--red-bg`, `--red-border`, `--red`). On the phone companion
  it is a click TOGGLE (tap on / tap off); the `.is-live` modifier adds
  a soft `rp-mic-pulse` box-shadow so an active mic is unmistakable
  (respects `prefers-reduced-motion`).
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
- `.rp-companion-remote` — remote-control button row on the mobile
  dictation companion (`/companion`); a flex-wrap row of `.ghost`
  buttons (prev/next section, jump to Findings / Impression, new line,
  undo), with a full-width `.primary-ghost` "Generate impression (AI)"
  action below. The mobile companion otherwise reuses `.rp-mobile` /
  `.rp-mic-btn` / `.rp-transcript`.
- `.rp-interim-dictation` — the live (interim) dictation preview the
  desktop shows at the caret while the phone is speaking. Muted
  `--text-muted` italic so it reads as "not committed yet"; it is a
  ProseMirror widget decoration, never part of the saved document.
- `.rp-mobile-update` — "Check for updates" footer on the phone companion
  (mobile surface only; the desktop self-updates via Tauri). A `--border`
  top divider over a full-width `.subtle` check button; when an update is
  found it becomes a `.banner ok` with a `.primary` "Download & install"
  link (opens the release APK in the system browser).
- `.rp-mic-live-dot` — small pulsing `--red` dot in the desktop host
  panel while the phone mic is live (paired-session "listening"
  indicator); shares the `rp-mic-pulse` keyframe.
- `.rp-companion-host` / `.rp-companion-host-panel` — the desktop
  "Pair phone" host affordance inline in the report editor. The panel
  composes the standard `.rp-panel` surface and reuses the locked
  `.rp-pair-code-tile` / `.rp-pair-code` for the code and a bordered
  QR tile; no new tokens.

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

Motion is a **first-class, expressive layer** of the design system, not an
afterthought. RadioPad should feel alive and responsive. (This supersedes the
previous "calm, no bouncy springs" guidance.) Every animation is token-driven and
**fully gated by `prefers-reduced-motion`** so it never compromises the clinical
workflow.

**Where it lives.** Tokens in `frontend/app/hallmark.css`; the keyframe library +
entrance/stagger utilities + the motion-driven components (Banner, Toast, Reveal,
PageTransition) in `frontend/app/motion.css`; Tailwind utilities mirrored in
`frontend/tailwind.config.ts`.

**Tokens.**
- Easings: `--ease-out/in/in-out` (UI) + expressive `--ease-pop`, `--ease-snap`,
  `--ease-spring`, `--ease-overshoot`.
- Transition durations: `--dur-fast` 120ms, `--dur-base` 180ms, `--dur-slow` 260ms;
  composed tokens `--transition-fast|base|slow|snap|spring`.
- Animation durations: `--anim-fast` 160ms, `--anim-base` 260ms, `--anim-slow` 420ms,
  `--anim-spin` 700ms.
- Stagger scale: `--delay-1…8` (40ms steps); `.rp-stagger` cascades direct children.

**Vocabulary.**
- Entrance: `.rp-anim-fade-in[-up|-down]`, `.rp-anim-scale-in`, `.rp-anim-pop-in`,
  `.rp-anim-slide-left|right`, `.rp-anim-spring-in`; route changes via `<PageTransition>`;
  on-scroll via `<Reveal>`.
- Interaction: hover/focus transitions on background/border/shadow/transform via the
  `--transition-*` tokens; buttons get a tactile `:active` press; primary actions use
  `--ease-pop`/`--ease-spring` for reveals.
- Attention/feedback: `.rp-motion-pulse`, `.rp-motion-glow`, count-ups via
  `<AnimatedNumber>`, status via `<Banner>` / toasts.

**Discipline.** On the clinical report-editing/dictation canvas, motion is limited to
entrance and state-change feedback — nothing animates continuously behind text being
read, dictated, or signed. Motion never implies an action the system didn't take
(RadioPad never auto-signs; AI text keeps `.ai-mark` until acknowledged).

**Reduced motion.** A single global `@media (prefers-reduced-motion: reduce)` rule in
`hallmark.css` neutralizes transitions/animations app-wide. Do not re-implement
reduced-motion handling per component.

Verify the full catalog and reduced-motion behavior at the dev route **`/design/motion`**.

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

1. Importing Material UI, Ant Design, Chakra, or Bootstrap. (Build-time
   **Tailwind 3** is part of the stack and allowed — see §2; the banned items
   are component/theme frameworks that would fight the Hallmark tokens.)
2. Introducing dark-mode tokens or a `.dark` palette (the system is light-only;
   `darkMode: 'class'` is set in the Tailwind config but no dark values ship).
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

### 4.17 Prompt Studio (`/prompts`)

The Prompt Studio rebuild is a two-pane workspace rendered inside `<Container>` +
`<PageHeader>` like every other library page. A `.rp-filter-bar`-based context bar
(`.rp-context-bar`) carries the rulebook `SearchableSelect`, the rulebook status
`.badge`, and an `.rp-chip.rp-chip-dirty` "N unsaved drafts" indicator. Below it,
`.rp-grid-2.rp-studio-grid` splits the editor (left, slightly wider) from the
test/review workspace (right).

New classes (all in `radiopad.css`, composing locked tokens only — `--mono`,
`--bg-subtle`, `--border`, and the semantic green/red/amber families; no new
tokens):

- **Block editor** — `.rp-block-panel`, `.rp-block-list`, `.rp-block-card`,
  `.rp-block-head`/`-headings`/`-title`/`-key`/`-desc`, `.rp-prompt-textarea`
  (the monospace prompt field; there is no `.rp-textarea` class — the box,
  border, and focus ring come from the `input,textarea,select` element baseline
  in `globals.css`), `.rp-block-footer`/`-count`/`-saved`/`-actions`, and the
  inline add-block row `.rp-add-block`/`-input`/`-suggest` (replaces the old
  `window.prompt()`).
- **Workspace** — right pane uses the existing `.rp-tabs`/`.rp-tab` pill control
  (§4.7) with an `.rp-tab-count` pill for pending approvals. `.rp-tab-body` /
  `.rp-tab-intro` wrap each tool. The Test Runner result and Golden-case summary
  use an `.rp-stat-strip` of existing `.rp-stat-tile`s; findings render with the
  existing `.finding` rows (§4.2).
- **Output diff** — `.rp-diff-controls`, `.rp-diff-legend`/`-legend-item`, and a
  mono `.rp-diff-panel` whose lines reuse the existing `.rp-diff-add`/
  `.rp-diff-remove` families (§4.8) plus an `.rp-diff-gutter`.
- **Approval** — `.rp-approval-list`/`-row`/`-headings`/`-title`/`-meta`/
  `-actions`/`-note`; drafts are sorted first and gated by `isMedicalDirector`.

Prompt blocks are parsed with the canonical `yamlToRulebookEditor().prompt_blocks`
(scalar-clean), fixing the earlier leak where the YAML block-scalar indicator
(`>` / `|`) showed as the first editor line. The Test Runner is non-destructive:
it calls `POST /api/prompts/validate` (a transient, never-persisted report) instead
of overwriting a real report's findings.

### 4.18 Auth entrance (`/login`, `/register`, `/pair`)

The product's front door. These are the only routes that bypass the sidebar
shell: `AppShell` detects them and renders children inside
`.rp-public-auth-content` (now a full-height flex host, no padding). All three
pages share `components/auth/AuthScaffold.tsx`, a split-screen layout — a branded
showcase **aside** on the left and the focused auth **card** on the right.

New classes (all in `radiopad.css`, composing locked tokens only; the aside's
tint gradient and concentric "signal" ring motif reuse `--accent-tint` /
`--accent-soft` via `color-mix`, the same precedent as `.entry-brand-mark` — no
new colours, no dark mode):

- **Split shell** — `.rp-auth-split`, `.rp-auth-aside` (hidden ≤ 880px),
  `.rp-auth-aside-motif` (decorative rings), `.rp-auth-main`, `.rp-auth-card`
  (max-width 408px, with a `rp-auth-rise` entrance animation gated by
  `prefers-reduced-motion`).
- **Aside content** — `.rp-auth-brand` (reuses the sidebar `.brand-mark` +
  `.brand-mark-letter`), `.rp-auth-headline` (serif), `.rp-auth-tagline`,
  `.rp-auth-features`/`-feature`/`-feature-icon`/`-feature-text`/`-feature-title`/
  `-feature-sub`, `.rp-auth-aside-foot`. The feature list **showcases product
  capabilities** (AI-assisted drafting, validation rulebooks, hands-free dictation,
  structured templates) rather than auth mechanics. Each `.rp-auth-feature` is a tidy
  **card** (`--bg-panel` surface, `--border-soft`, `--radius`, `--shadow-xs`, with a
  `prefers-reduced-motion`-gated hover lift to `--shadow-sm` / `--accent-soft`); the
  icon tile is tinted with `--accent-tint`/`--accent-soft`. A compact **trust strip**
  `.rp-auth-trust`/`.rp-auth-trust-item` (pill chips on `--bg-subtle`, `--border`,
  `--text-muted`) sits below the cards for the credibility signal — all locked tokens,
  no new colours.
- **Card chrome** — `.rp-auth-mobile-brand` (shown only when the aside is hidden),
  `.rp-auth-head`/`-eyebrow`/`-title`/`-sub`, `.rp-auth-form`, `.rp-auth-actions`
  (full-width stacked buttons), `.rp-auth-divider` (labelled "or"-style rule),
  `.rp-auth-hint`, `.rp-field-hint`(`.rp-field-error`), `.rp-auth-foot` +
  `.rp-auth-link`. The dev/test bearer block is tucked into the existing
  `.rp-advanced` `<details>` (§3.1 chrome).
- **Check-your-email** — shared `components/auth/CheckYourEmail.tsx`:
  `.rp-auth-success`/`-icon`/`-title`/`-sub` and `.rp-auth-devlink`(`-label`) for
  the non-production dev link. Used by both magic-link request and registration.
- **Device-pairing rail** — `.rp-pair-steps`/`.rp-pair-step`(`.active`/`.done`)/
  `-dot`/`-label`, a three-step progress rail over the existing `.rp-pair-code` /
  `.rp-pair-code-tile` chip (now tinted with `--accent-tint`).

**Buttons / icons.** Buttons use the locked `.primary` / `.primary-ghost` /
`.ghost` variants. Feature and step icons are inline 1.7-stroke `currentColor`
SVGs (no emoji-as-icon, per §8).

**Viewport fit (no page scroll).** `.rp-auth-split` is `100vh`/`overflow:
hidden` — the entrance must always fit the viewport whole. The card's
comfortable rhythm needs ~1000px of height, so two `max-height` compaction
tiers (scoped strictly to `.rp-auth-*` / the auth card, never global controls)
tighten the vertical rhythm on shorter viewports: **≤ 1020px** shrinks card
margins/paddings, the step rail, title size, and button padding; **≤ 840px**
additionally sheds the aside's secondary copy (`.rp-auth-tagline`,
`.rp-auth-feature-sub`, `.rp-auth-aside-foot`) so the showcase pane also stays
inside the viewport. Verified overflow-free at 720/841/945/1021px heights.

**Functional model (passwordless).** Sign-in offers SSO (when
`NEXT_PUBLIC_ENABLE_SSO=true`), the email magic link (primary), device pairing,
and the dev bearer (when `NEXT_PUBLIC_ALLOW_DEV_LOGIN=true`). "Register" is
self-serve organization creation: `POST /api/registration/create-organization`
creates a tenant + first MedicalDirector admin + `TenantSettings`, emails a magic
link, and is gated by `RADIOPAD_ALLOW_SELF_SIGNUP` (off by default in Production).
There is no password and no password-reset surface; "Trouble signing in?" simply
re-requests a link.

