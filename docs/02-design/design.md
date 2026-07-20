# RadioPad — Design System (RC tokens, dual-theme sidebar shell)

**Status:** RC design system (PRD v3.0 §20) · Light default + first-class dark · Build-time Tailwind · Sidebar shell  **Owner:** Product Design  **Last Updated:** 2026-07-13

> **MISSION-CRITICAL RULE.** RadioPad's visual identity is the **RC design
> system**: a light-first, white/blue clinical-SaaS palette with a
> **first-class deep-navy dark theme**, specified in
> **`RadioPad_Enterprise_PRD_v3.0.md` §20** and rendered pixel-faithful in the
> reference mockups at **`UI UX SCREENS/Authentication/`** (RC-01…RC-10, light
> and dark frames plus state variants). The mockups are the source of truth
> for how screens look; this document is the source of truth for how they are
> built.
>
> The **canonical token source** is **`frontend/app/tokens.css`** — RC
> `--color-*` primitives (light values in `:root`, dark overrides under
> `html[data-theme="dark"]`) plus the compatibility alias layers that keep
> every historical token name resolving — and **`frontend/tailwind.config.ts`**
> (Tailwind scales pointed at the same CSS variables). RadioPad's original
> **44 alias names** (`--bg`, `--accent`, `--red`, semantic families, radii,
> shadows, `--sans`/`--mono`) remain the **stable contract** you write against;
> they are re-pointed onto the RC primitives, joined by new tokens the RC
> system introduces (`--accent-fg`, `--scrim`, `--link`, `--bg-selected`, the
> `--ai` and `--navy` families).
>
> **Both themes are mandatory.** Light is the first-run default (THEME-001);
> dark is a first-class theme, never an afterthought. Never hardcode a colour
> — write tokens and both themes come for free. Every UI change must be
> checked in **both** themes before it ships. Print and exports always render
> the light document theme (THEME-015).
>
> **Build-time Tailwind 3 is part of the stack** (`@tailwind` directives in
> `globals.css`, config in `tailwind.config.ts`, PostCSS + Autoprefixer).
> Utilities compile to static CSS at build time and ship into the
> `output: 'export'` bundle (Tauri/Capacitor-safe). Utilities and the named
> RadioPad component classes may be mixed freely; both resolve to the same
> CSS variables, so both are automatically theme-aware.
>
> The **app shell** is the enterprise-SaaS **left-sidebar shell** (sidebar +
> topbar + page header) — see §3.1. The two-pane `.split` primitive remains an
> *in-page* editor primitive, never the app shell.

This document is the source of truth referenced by:
- `AGENTS.md`
- `CLAUDE.md`
- `/memories/repo/radiopad-design-lock.md`

If those files disagree with this document, this document wins.

**Reference mockup catalog** (`UI UX SCREENS/Authentication/`):

| ID | Surface | Notable states |
| --- | --- | --- |
| RC-01 | Study context panel (light+dark) | Loading / No priors / PACS disconnected |
| RC-02 | Findings editor — generated-line treatment, review checklist | Autosaving / Unsaved warning / Sync conflict |
| RC-03 | Impression editor — numbered impressions, Accept/Undo per line | Empty / Generating / Awaiting review / Error |
| RC-04 | Validation panel — severity tiles, inline linked issues, override | All clear / Mixed / Running / Engine offline |
| RC-05 | Priors comparison — side-by-side diff, sync scroll | Changed/New blue vs Different amber chips |
| RC-06 | Composer ribbon — Review / AI Compose / Sign-off grouped icon buttons, Rewrite ▾ menu, scope chip | AI activity rail |
| RC-07 | Citation & provenance modal | Loading / Source unavailable |
| RC-08 | Dictation bar | Idle blue / Listening green / Paused amber / Processing blue / Disconnected red / Error red |
| RC-09 | Export panel — destinations, validation gate, step-up | Blocked / Sending / Delivered / Failed |
| RC-10 | Hotkey customization | Default/Custom/Conflict badges, key-recording |

---

## 1. Design philosophy

RadioPad is a clinical reporting product; the RC system expresses that as a
calm, high-trust enterprise SaaS:

- **White/blue clinical clarity.** Light canvas (`#F5F8FB`) with pure-white
  cards, cool ink text, a single confident blue accent. No warm-paper tint,
  no editorial styling — the reporting workspace reads like clinical
  software, not a magazine.
- **Dark is a first-class theme.** Deep navy (`#0B1422` canvas), never pure
  black (THEME-014). Surfaces gain real elevation steps in dark; text and
  status hues are re-tuned per theme, not merely inverted.
- **One accent.** RadioPad blue (`--accent`, `#2F88D8` light / `#3B82F6`
  dark). No additional brand colours. Status semantics use the dedicated
  semantic families in §2.5 — and never hue alone.
- **Inter everywhere.** One sans family for chrome, forms, *and* report
  prose (the serif report body is retired). Mono is reserved for accession
  numbers, rule IDs, and hashes.
- **AI is visibly provisional.** Anything a model wrote wears the blue
  "✨ generated" treatment plus an amber "Requires review" pairing until a
  radiologist accepts or edits it (§4.1). This is a clinical-safety
  affordance, not decoration.
- **Hairline > heavyweight.** Borders are 1px, panels separated by rules and
  the lightest shadow that solves the problem. Motion is purposeful feedback,
  never decoration in clinical views (§6).

---

## 2. Design tokens (canonical)

The **canonical source** is **`frontend/app/tokens.css`**. It has three layers:

1. **RC primitives** — `--color-*` custom properties: light values in
   `:root`, the full dark set overridden under `html[data-theme="dark"]`
   (only primitives are overridden; every alias re-resolves automatically).
   Also `--font-*`, `--space-*`, `--text-*`, radii, shadows, and the motion
   token block.
2. **Compatibility aliases** —
   **Layer A**: historical Hallmark primitive names (`--color-paper*`,
   `--color-saffron*`, `--color-marine*`) re-pointed at RC primitives so old
   call sites keep resolving. **Do not write new code against Layer A.**
   **Layer B**: RadioPad's original **44-name semantic contract** (`--bg`,
   `--bg-panel`, `--border`, `--text*`, `--accent*`, the green/blue/red/
   amber/purple families with `-bg`/`-border` pairs, radii, shadows, fonts)
   plus the **new RC tokens**: `--accent-fg`, `--scrim`, `--link`,
   `--bg-selected`, `--bg-elevated` (real elevation on dark), and the
   `--ai`/`--ai-bg`/`--ai-border` and `--navy`/`--navy-bg`/`--navy-fg`
   families. Layer B is what you write against.
3. **Base + shared component classes** — focus ring, the global
   reduced-motion idiom, `forced-colors` handling, and the RC-styled shared
   classes (`.control-button`, `.status-badge`, `.data-table`, … — §3.9).

The matching Tailwind scales live in `frontend/tailwind.config.ts`; every
Tailwind colour is `var(--color-*)`, so utilities are theme-aware with no
`dark:` duplication. `globals.css` declares no colour tokens — it carries the
`@tailwind` directives and the legacy class layer. Do not redefine tokens
inline, and do not reintroduce raw hex values; extend `tokens.css` instead
(and mirror new scales in `tailwind.config.ts`).

The tables below give the **alias name** (the stable contract), the RC
primitive it resolves to, and the **light / dark** values.

### 2.1 Surfaces

| Token | Primitive | Light | Dark | Use |
| --- | --- | --- | --- | --- |
| `--bg` / `--bg-app` | `--color-canvas` | `#F5F8FB` | `#0B1422` | App background |
| `--bg-panel` | `--color-surface` | `#FFFFFF` | `#111D2D` | Cards, panels, popovers |
| `--bg-elevated` | `--color-elevated` | `#FFFFFF` | `#16273B` | Modals, menus (real elevation step on dark) |
| `--bg-subtle` | `--color-surface-subtle` | `#EEF3F8` | `#152235` | Hover, wells, code chips |
| `--bg-muted` | `--color-surface-muted` | `#E3ECF4` | `#1B2C42` | Pressed / selected wells |
| `--bg-selected` | `--color-selected` | `#EAF3FC` | `#16304B` | Selected rows, active nav tint |

### 2.2 Borders

| Token | Primitive | Light | Dark |
| --- | --- | --- | --- |
| `--border` | `--color-rule` | `#D8E2EB` | `#2B3D52` |
| `--border-soft` | `--color-rule-soft` | `#E7EEF4` | `#223349` |
| `--border-strong` | `--color-rule-strong` | `#B3C4D4` | `#3E5570` |

### 2.3 Text

| Token | Primitive | Light | Dark | Use |
| --- | --- | --- | --- | --- |
| `--text` | `--color-ink` | `#0F1F38` | `#EDF6FF` | Body |
| `--text-strong` | `--color-ink-strong` | `#091428` | `#FFFFFF` | Titles |
| `--text-muted` | `--color-ink-soft` | `#40536B` | `#B9C7D7` | Secondary |
| `--text-soft` | `--color-ink-mute` | `#5D7085` | `#8CA0B5` | Tertiary |
| `--text-faint` | `--color-ink-faint` | `#8FA1B5` | `#64798F` | Placeholder |
| `--text-inverse` | `--color-ink-inverse` | `#FFFFFF` | `#0F1F38` | Text on inverse surfaces |

### 2.4 Accent, focus, links, scrim

| Token | Primitive | Light | Dark |
| --- | --- | --- | --- |
| `--accent` | `--color-accent` | `#2F88D8` | `#3B82F6` |
| `--accent-strong` / `--accent-hover` | `--color-accent-deep` | `#1F6FB8` | `#619BF8` |
| `--accent-soft` | `--color-accent-soft` | `#BBDCF6` | `#1E3A5F` |
| `--accent-tint` | `--color-accent-tint` | `#EAF3FC` | `#16283F` |
| `--accent-fg` | `--color-accent-fg` | `#FFFFFF` | `#FFFFFF` |
| (focus ring) | `--color-focus-ring` | `#5EAAF0` | `#83C4FF` |
| `--link` | `--color-link` | `#1F6FB8` | `#7CB8F5` |
| `--scrim` | `--color-scrim` | `rgba(15,31,56,.45)` | `rgba(2,8,18,.6)` |

**`--accent-fg` is the only correct colour for text/icons on accent (or
status-filled) surfaces.** Never `color: white` — that is exactly the class of
hardcode that ghosts a theme. `--scrim` is the only overlay/backdrop colour.
`--link` styles inline hyperlinks (distinct from `--accent` so links stay
readable on tinted surfaces).

### 2.5 Semantic families (status / severity / categories)

Each family is a fg / `-bg` / `-border` triad, re-tuned per theme:

| Family | Role | Light fg / bg / border | Dark fg / bg / border |
| --- | --- | --- | --- |
| `--green` | success | `#11845B` / `#DCF2E8` / `#A7DCC5` | `#2FBF87` / `#0E2B22` / `#1D4A39` |
| `--blue` | info | `#2565AE` / `#DFECFA` / `#B7D3F0` | `#6FA8E8` / `#12263C` / `#26456B` |
| `--red` | danger / blocker | `#C43D3D` / `#FAE3E3` / `#EEBCBC` | `#E36868` / `#351619` / `#5C2528` |
| `--amber` | warning | `#A65E00` / `#FBF1DE` / `#ECD3A4` | `#E5A43B` / `#33270F` / `#57431C` |
| `--ai` | **AI "generated"** | `#1D63C2` / `#E8F1FD` / `#B9D6F7` | `#7CB8F5` / `#142943` / `#2A4A73` |
| `--purple` | provenance / custom | `#7C3AED` / `#F1EAFD` / `#D9C9F5` | `#B99AF2` / `#251C3D` / `#47356B` |
| `--navy` | STAT / strong-selected | `#23406E` / `#E1E9F6` / fg `#FFFFFF` (`--navy-fg`) | `#35619E` / `#182C49` / fg `#FFFFFF` |

Validation severity mapping (unchanged, locked):

- **Blocker** → Red family
- **Warning** → Amber family
- **Info / Style** → Blue family
- **Success / All clear** → Green family
- **AI-generated content highlight** → **`--ai` blue family** (§4.1). The
  purple family is *no longer* the AI marker; purple is reserved for
  provenance-chain accents and "Custom" categories (RC-07/RC-10).

### 2.6 Radii

RC radii: **control 10px / panel 14px / pill**.

`--radius-sm: 8px`, `--radius: 10px` (= `--radius-control`),
`--radius-lg: 14px` (= `--radius-panel`), `--radius-pill: 999px`.
Buttons/inputs/menu items use `--radius`; cards/panels/modals use
`--radius-lg`; chips/badges use `--radius-pill`.

### 2.7 Shadows

`--shadow-xs`, `--shadow-sm`, `--shadow-md`, `--shadow-lg` — cool-ink alphas
in light, deeper black-based alphas in dark (overridden in the dark block of
`tokens.css`). Use the lightest one that solves the problem; never write a
raw `box-shadow` colour.

### 2.8 Typography

| Token | Stack |
| --- | --- |
| `--sans` / `--font-body` | `'Inter Variable', Inter, 'Segoe UI', BlinkMacSystemFont, system-ui, sans-serif` |
| `--font-display` | `'Inter Variable', Inter, 'Segoe UI', system-ui, sans-serif` |
| `--mono` / `--font-mono` | `'Cascadia Mono', 'SFMono-Regular', Consolas, ui-monospace, monospace` |
| `--serif` / `--font-serif` | **retired** — alias resolves to the sans stack |

**Inter Variable is self-hosted** via `@fontsource-variable/inter`, imported
at the top of `frontend/app/layout.tsx` (no network fetch — Tauri/Capacitor
safe). **The serif report body is retired**: RC mockups render report prose
in the sans stack, and the `--serif` alias is kept only so legacy call sites
keep resolving — do not use it in new code. `--mono` stays for accession
numbers, rule IDs, and hashes.

Body: **13.5px** / line-height **1.5**; headings step up from this base.
Type scale tokens: `--text-xs` … `--text-2xl`, `--text-display-s`,
`--text-display`.

### 2.9 Spacing

4px unit scale: `--space-1` (4px) → `--space-11` (192px); the common rhythm
is 4 / 8 / 12 / 16 / 24 / 32.

### 2.10 Theme system (light / dark / system)

The theme runtime lives in **`frontend/lib/theme.ts`**; the dark palette
lives entirely in `tokens.css`. Requirements it implements (PRD §20):

| Req | Behaviour |
| --- | --- |
| THEME-001 | First-run default is **Light** on every surface |
| THEME-002 | Visible light/dark toggle on the sign-in screen and in every shell topbar (`<ThemeToggle />`) |
| THEME-004 | Preference persisted per-device in `localStorage['rp-theme']` (`'light' \| 'dark' \| 'system'`; never PHI) |
| THEME-005 | Switching is instant — no reload, no re-mount, no layout shift; scroll/focus/drafts preserved |
| THEME-006 | Inline pre-paint bootstrap script in `app/layout.tsx` `<head>` reads localStorage and stamps `data-theme` before first paint — **no wrong-theme flash** |
| THEME-014 | Dark is deep navy, never pure black |
| THEME-015 | Print / exports always render the **light document theme** (an `@media print` block in `tokens.css` re-asserts the light primitives even in a dark session) |

**`lib/theme.ts` API:**

```ts
type ThemePreference = 'light' | 'dark' | 'system';
type ResolvedTheme = 'light' | 'dark';

getThemePreference(): ThemePreference        // localStorage['rp-theme'], default 'light'
resolveTheme(pref?): ResolvedTheme           // 'system' → matchMedia
applyTheme(pref?): ResolvedTheme             // sets html[data-theme] + <meta name="theme-color">
setThemePreference(pref): ResolvedTheme      // persists, applies, dispatches 'rp-theme-change'
watchSystemTheme(): () => void               // OS-scheme sync while pref === 'system'
THEME_BOOTSTRAP_SCRIPT                       // the inline pre-paint script (THEME-006)
THEME_COLORS                                 // { light: '#f5f8fb', dark: '#0b1422' } — browser-chrome meta
```

The mechanism: the resolved theme is applied as `data-theme="dark"` on
`<html>` (absent = light). `tokens.css` overrides the RC primitives under
`html[data-theme='dark']`; every alias and every component class re-resolves
automatically. `color-scheme` is set per block so native controls
(scrollbars, form widgets) follow.

**`<ThemeToggle />`** (`frontend/components/ui/ThemeToggle.tsx`) is the
mockups' sun/moon pill switch (`role="switch"`, `.rp-theme-toggle`). It flips
explicit light/dark; the full Light/Dark/System preference picker lives in
Settings. It belongs in the shell topbar and on the auth card (THEME-002) —
**note: the RC topbar rework (and with it the toggle's topbar placement)
lands in the shell phase.**

Tailwind: `darkMode: ['selector', '[data-theme="dark"]']`. Because all
colours are variables, `dark:` variants are rarely needed — reach for them
only for genuinely structural differences, never for colour flips.

**Dark-mode authoring rules (locked):**

1. **Never hardcode colours** — no hex, no `rgb()`, no named colours in
   feature CSS or TSX. Tokens only. `color: white` on an accent fill is
   `var(--accent-fg)`; overlays are `var(--scrim)`; shadows are the
   `--shadow-*` tokens.
2. **Check both themes for every UI change** (see §9). A change is not done
   until it has been eyeballed light *and* dark.
3. If a component genuinely needs a dark-specific adjustment, override an RC
   *primitive* in the dark block of `tokens.css` — do not sprinkle
   `html[data-theme='dark']` selectors through feature stylesheets.
4. **Intentionally-dark exceptions** (dark in *both* themes, by design): the
   `.op-bash` terminal block (globals.css) and presentation/present-mode
   surfaces. Do not "fix" them.

---

## 3. Components & class API

The canonical stylesheets ship these classes. Reuse them; do not redefine.
Everything below composes tokens only, so every class is automatically
correct in both themes.

### 3.1 App shell (sidebar — canonical)

RadioPad uses a fixed left sidebar + topbar + page header shell. The sidebar
carries primary navigation; the topbar carries global chrome.

**RC topbar (target anatomy, per mockups):** RadioPad logo + wordmark on the
left, the **global search field** (opens the Cmd+K command palette), then on
the right the **`HIPAA Compliant` pill** (green family), the **notification
bell** (with count), **`<ThemeToggle />`**, and the **avatar + name + role
menu**. The RC sidebar is a flat icon+label list with a blue active bar +
`--bg-selected` tint, section dividers, and a Collapse control at the bottom.

> **Note:** the topbar/sidebar rework to this RC anatomy lands in the shell
> phase of the redesign. Until it lands, the current chrome below remains
> canonical; both are built from the same classes and tokens.

```html
<div class="rp-shell">
  <aside class="rp-sidebar" aria-label="Primary">
    <a class="rp-sidebar-brand" href="/">
      <span class="brand-mark"><span class="brand-mark-letter">R</span></span>
      <span class="rp-sidebar-brand-text">
        <span class="rp-sidebar-brand-title">RadioPad</span>
        <span class="rp-sidebar-brand-meta">AI radiology reporting</span>
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
      <div class="rp-topbar-actions">…updates, theme toggle, locale, profile, page-action slot…</div>
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

**IA (RC target):** Dashboard · Worklist · Report Composer · Templates ·
Protocols · AI Assistant · Findings Library · Reports · Analytics · Quality &
Peer Review · Users & Teams (web) · System Settings — surface/permission tags
in `nav.config.tsx` preserved. (Pre-shell-phase grouping: Workspace /
Library / Integrations / Admin.)

Sidebar is collapsible to icon-only on desktop (state persisted in
`localStorage`). On viewports `≤900px` the sidebar becomes a left slide-out
drawer triggered by the topbar hamburger; the drawer traps focus, closes on
`Escape` / backdrop click (backdrop uses `--scrim`), and respects
`prefers-reduced-motion`.

**Check-for-updates control (`.rp-update`, desktop shell only).** A 36×36
topbar icon button (`.rp-update-btn`) that drives the Tauri auto-updater
(DESK-001). Renders only inside the Tauri webview. A silent check on launch
shows an accent dot (`.rp-update-dot`) when an update is waiting; status text
(`.rp-update-label`) uses the semantic families — blue for checking/
downloading, green for up-to-date, red for failure.

#### 3.1.1 In-page two-pane primitive (`.split`)

`.split` defaults to `grid-template-columns: minmax(380px, 460px) 1fr` and is
preserved for **in-page** editor surfaces (study context · editor in
`/reports/view`; rulebook tree · YAML in `/rulebooks/editor`). It is not the
app shell. Pages that use `.split` opt out of the default `<PageHeader>` and
request a full-bleed `<Container fluid>`.

#### 3.1.2 Legacy `.topbar` / `.app` classes

The original `.topbar` and `.app` classes still exist in `globals.css` as
**per-pane chrome** primitives (inside two-pane editor surfaces). They must
not be used as the application root — `.rp-shell` is canonical.

### 3.2 Buttons

| Class | Use |
| --- | --- |
| (none) | Default — secondary action |
| `.primary` | **Blue fill** (`--accent` bg, `--accent-fg` text), primary CTA (one per surface) |
| `.primary-ghost` | Blue outline, secondary CTA |
| `.ghost` | Transparent, tertiary |
| `.subtle` | Subtle filled surface |
| `.icon-btn` | Icon-only, square padding |

Disabled state is `opacity: 0.5; cursor: not-allowed`. Focus ring is
`2px solid var(--color-focus-ring)` with `2px` offset (global
`:focus-visible` rule in `tokens.css`).

### 3.3 Inputs / textareas / selects

All inputs share the same shell: 1px `--border`, `--radius-sm`, `--bg-panel`
surface, focus → `--accent` border + `--accent-soft` halo. Labels render as
small-caps muted text above the field (`.section-block label`).

#### 3.3.1 Searchable combobox (`.rp-combobox*`)

A filterable replacement for a native `<select>` when the option list is long
(e.g. the 20+-entry Rulebook picker). Component:
`components/ui/SearchableSelect.tsx`. Anatomy: `.rp-combobox-trigger` button
(restating the input shell) with `.rp-combobox-value` (faint when
`[data-placeholder]`) + `.rp-combobox-caret`, opening a `.rp-combobox-panel`
popover (`--bg-elevated`, `--shadow-md`, `--radius`) holding a
`.rp-combobox-search` input over a `.rp-combobox-list` of
`.rp-combobox-option` rows (`hover`/`.is-active` → `--bg-subtle`;
`[aria-selected]` → `--accent-strong`; `[aria-disabled]` muted) and a
`.rp-combobox-empty` no-match state. Click-outside + Escape close it. Use it
wherever an option list is long enough to want type-to-filter (the locked
SearchableSelect pattern); keep native `<select>` for short lists.

Checkbox rows inside `.rp-profile-popover` use `.rp-profile-popover-check`
with `.rp-profile-check-label`; the row is `align-items: flex-start` and the
box uses `accent-color: var(--accent)`. Containers must not
`overflow: hidden` around popovers.

### 3.4 Messages (chat & report sections)

`.msg`, `.msg.user`, `.msg.assistant`, `.msg.error`. AI-drafted content wraps
in the `--ai` blue left-rule block (§4.1). Errors use the red family
(`--red-bg` + `--red-border` + `--red`).

### 3.5 Composer

`.composer` + `.composer-shell` are the canonical sticky-bottom input
pattern — free-text dictation paste, AI prompts in Prompt Studio, rulebook
YAML edits.

### 3.6 Pills, badges, chips & status vocabulary

Use `border-radius: var(--radius-pill)` and the semantic family tokens. Rule
IDs / accession numbers / hashes render in `code` (mono, subtle bg).

**`.badge` tones** (radiopad.css): `.badge.ok` (green), `.badge.info` (blue),
`.badge.warn` (amber), `.badge.danger` (red), **`.badge.ai` (AI blue — the
"generated" chip)**.

**`.banner` tones** (radiopad.css): `.banner.ok`, `.banner.info`,
`.banner.warn`, `.banner.danger`, **`.banner.ai`** — same families;
`.rp-banner.*` mirrors them for admin namespaces.

**`.status-badge[data-tone]`** (tokens.css): `ready` (green), `review`
(amber), `blocked` (red), `info` (blue), `ai` (AI blue), **`stat` (filled
navy — `--navy` bg, `--navy-fg` text)**, `draft`/`muted` (neutral).

**Chip vocabulary (locked — RC mockups):**

| Chip | Family / treatment |
| --- | --- |
| **Urgent** | Red |
| **Routine** | Green |
| **STAT** | Navy, filled (`--navy` + `--navy-fg`) |
| **Requires review** | Amber |
| **✨ generated** | AI blue (`.badge.ai` / `data-tone="ai"`) |
| **HIPAA Compliant** | Green (topbar pill) |
| **Ready** | Green |
| **Offline / Disconnected** | Red |
| **Custom** | Purple (provenance/custom — RC-10) |

Chips always carry their text label — colour is never the only signal.

### 3.7 Card grid & chips (`.rp-card*`, `.rp-chip*`)

Browsable collection landing pages (e.g. `/rulebooks`) use a responsive card
grid: `.rp-card-grid` (`auto-fill`, `minmax(256px, 1fr)`, 14px gap);
`.rp-card` clickable surface (`--bg-panel`, hairline border, `--shadow-xs`;
hover lift → `--border-strong` + `--shadow-md`); `.rp-card-head` /
`.rp-card-title` / `.rp-card-id` (mono) / `.rp-chip-row` of `.rp-chip` /
`.rp-card-meta` / `.rp-card-actions` (inner buttons `stopPropagation()`).
`.rp-filter-bar` + `.rp-search` sit above the grid.

### 3.8 Sticky toolbar (`.rp-toolbar.sticky`)

Pins an action row (Cancel/Validate/Save/Publish) to the top of a scrolling
editor (`--bg-app`, bottom hairline, `z-index:5`). Pair with
`.split.rp-editor-split` for visual editors with stacked form panels.

### 3.9 Shared component classes in `tokens.css`

These live next to the tokens and are a stable contract, safe to use directly
in pages:

- `.control-button` (+ `.secondary`, `.is-success`, `.is-error`,
  `[aria-busy]`) — accent-filled control button, `--accent-fg` text.
- `.status-badge` + `data-tone` — see §3.6.
- `.metric-grid` / `.metric-card` (+ `data-tone` accent top-rule) /
  `.metric-card-value` / `.metric-card-label` — KPI tiles.
- `.table-wrap` + `.data-table` — bordered panel table (uppercase `th` on
  `--bg-subtle`, row hover).
- `.tab-list` / `.tab-button` (`[aria-selected]` → accent fill).
- `.view-panel`, `.list-panel`, `.empty-state`, `.masthead`,
  `.source-pill` / `.section-kicker`.

### 3.10 Progress bar (`.rp-progress`)

The shared determinate/indeterminate progress primitive, in `radiopad.css`.
Before it existed, every long-running surface hand-rolled a bar from inline
`var(--border)` / `var(--accent)` styles, which is exactly what the colour rule
forbids.

```html
<div class="rp-progress-label"><span>Downloading — 1.2 GB of 2.3 GB</span><b>52%</b></div>
<div class="rp-progress" role="progressbar" aria-valuenow="52" aria-valuemin="0" aria-valuemax="100">
  <span class="rp-progress-fill" style="width:52%"></span>
</div>
```

- `.rp-progress-fill` takes its width from the caller; that inline `width` is
  the one sanctioned inline style here (it is a value, not a colour).
- `data-indeterminate="true"` on `.rp-progress` sweeps the fill instead, for
  work whose total is genuinely unknown (verify / extract / install). **Do not
  fake a percentage** — omit `aria-valuenow` in this mode.
- The sweep is disabled under `prefers-reduced-motion` (§6).

---

## 4. RadioPad-specific patterns (built on the locked tokens)

### 4.1 AI provenance — the blue "generated" treatment

Any text written by the AI gateway must visually distinguish itself until
acknowledged (PRD §20.4, RC-02/03/06). The class name **`.ai-mark`** and the
clinical-safety semantics are preserved from day one; the colour is now the
**`--ai` blue family** (the old purple marker is retired — purple now means
provenance/custom, §2.5):

```css
.ai-mark {
  background: var(--ai-bg);
  border: 1px solid var(--ai-border);
  border-left: 3px solid var(--ai);
  border-radius: var(--radius-sm);
  padding: 10px 12px;
}
.ai-mark::before {
  content: '✨ generated — review required';
  display: block;
  font-size: 10.5px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--ai);
  font-weight: 600;
  margin-bottom: 6px;
}
```

Rules:

- The tinted field + the explicit **"✨ generated"** label always travel
  together — **never hue-only** (colour-blind parity + forced-colors).
- Pair with an amber **"Requires review"** chip at the section/report level
  until every generated span is accepted or edited (RC-02 review checklist).
- The marker disappears the moment the radiologist **edits** the section or
  clicks **Accept** (RC-03 Accept/Undo per generated line).
- Inputs nested inside (`textarea`, `.rp-section-editor`) go transparent so
  the field reads as one tinted block.
- Motion: `.ai-mark` pops in via `rp-pop-in` (reduced-motion-gated).

### 4.2 Validation finding rows

`.finding.blocker` (red), `.finding.warning` (amber), `.finding.info` (blue).
All three use a 3px left rule and `--bg-subtle` background; the rule id
renders in `var(--mono)` underneath the message. RC-04 adds severity tiles
(Blockers red / Warnings amber / Style blue) above the list, inline
linked-issue highlights in the document, and override-with-reason — all
composed from the same families.

### 4.3 Provider compliance pills

| Compliance class | Pill family |
| --- | --- |
| `Blocked` | Red |
| `Sandbox` | Amber |
| `DeIdentifiedOnly` | Blue |
| `PhiApproved` | Green |
| `LocalOnly` | Purple |

### 4.4 Report narrative

Findings, Impression, and Recommendations render in the **sans (Inter) stack**
at 14.5px / 1.6 line-height — the serif report body is retired (§2.8).
Section headings are small caps in the muted token. Generated narrative wears
`.ai-mark` (§4.1); the document still reads as a calm report, the RC way:
white section cards, hairline rules, quiet chrome.

### 4.5 Admin dashboard helpers

Dense operational layouts may use the `rp-` helpers in `radiopad.css`:
`.rp-grid-3`, `.rp-list`, `.rp-stat-label`, `.rp-stat-value`,
`.rp-divider-row`, `.rp-actions`, `.rp-subtle-link`, `.rp-stat-tile`
(+ `.rp-stat-tile-row`, `.rp-stat-sub`), `.rp-stat-strip`, and `.rp-banner`
(`.warn`/`.info`/`.danger`). These compose locked tokens only.

### 4.6 Report editor — rewrite menu

"Rewrite ▾" opens `.rp-rewrite-menu` / `.rp-rewrite-popover` listing the four
rewrite modes; each `.rp-rewrite-option` shows a label + muted hint. Returned
text appears inside `.ai-mark`; the "Diff" toggle renders original/proposed
side-by-side via `.rp-rewrite-diff` + `.rp-rewrite-pre`.

### 4.7 Tab control (`.rp-tabs`)

Pill-segmented selector: `.rp-tabs` container, `.rp-tab` segments,
`.rp-tab.active` selected, `.rp-tab-count` count pill. Composes `--bg-subtle`,
`--border`, `--radius-pill`, `--shadow-xs` only.

### 4.8 Prior-compare grid (`.rp-grid-2`, `.rp-diff-*`)

Current/prior pairs render inside `.rp-grid-2` (collapses ≤720px) with
`.rp-grid-2-row` (`display: contents`) keeping pairs aligned. Differences use
`.rp-diff-add` (green — additive) and `.rp-diff-remove` (red — superseded).
RC-05 layers on top: **Changed/New = blue chips, Different = amber chips**,
synchronized scrolling between the two columns.

### 4.9 Mobile companion & breakpoint

`@media (max-width: 720px)` is the canonical mobile breakpoint (grids and
`.rp-workspace` collapse to one column; `.rp-mobile` tightens padding). Do
not add additional breakpoint values (shell drawer threshold `≤900px` and the
editor-body collapse `≤960px`/`≤1100px` are the other locked values).

Mobile/companion helpers (`radiopad.css`, tokens only): `.rp-mobile`,
`.rp-mic-btn` (+ `.recording` red family, `.is-live` pulse), `.rp-transcript`
(sans stack; `data-empty` → muted italic), `.rp-mobile-section` /
`.rp-mobile-body`, `.rp-ack-row`, `.rp-pair-shell` / `.rp-pair-code-tile` /
`.rp-pair-code`, `.rp-companion-remote`, `.rp-interim-dictation`,
`.rp-mobile-update`, `.rp-mic-live-dot`, `.rp-companion-host` /
`.rp-companion-host-panel`.

The dictation bar follows **RC-08's six state colours**: Idle blue ·
Listening green · Paused amber · Processing blue · Disconnected red · Error
red — with <150ms visual feedback and Space/Esc key bindings.

### 4.10 Rulebook visual editor (`.rp-drag-handle`, `.rp-drag-active`, `.rp-editor-block`)

Drag-and-drop rulebook composition inside the `.split` shell:
`.rp-drag-handle` (`--text-faint`, grab cursor), `.rp-drag-active`
(`--accent-tint` bg + `--accent-soft` border on drag/drop targets),
`.rp-editor-block` (+ `.collapsed`) block containers, `.rp-yaml-preview`
(mono `<pre>` on `--bg-subtle`).

### 4.11 Desktop backend status banner (`.rp-desktop-status`)

Sidecar health events render as a single full-width `.banner` under the
topbar: `starting` → `.banner.info`, `restarting`/`degraded` →
`.banner.warn`, `failed` → `.banner.danger`; hidden for `ready`/`disabled`.
`.rp-desktop-status` strips side radii/borders; `.rp-desktop-status-meta` is
a mono metadata chip. No continuous animation.

### 4.12 Report Composer (RC-01…RC-09 anatomy)

The report editor (`/reports/view`) is being rebuilt to the RC
reporting-workspace anatomy (PRD §20.9) — pixel-faithful to the mockups:

- **Patient context bar** (`PatientContextBar`) — sticky strip: identity,
  procedure, **priority chip** (Urgent/Routine/STAT per §3.6), location,
  indication, priors count, review chip, save state, Export split-button.
- **Study context panel** (RC-01, collapsible) — study metadata card + case
  queue table with priority chips; states: Loading / No priors / PACS
  disconnected.
- **Composer** — central column of **section cards** (Clinical information /
  Technique / Findings / Impression) with "Copied from RIS" and "Requires
  review" chips and per-section ⋮ menus. Generated lines wear the §4.1
  treatment with Accept/Undo per line (RC-02/03, SectionEditor decorations).
- **Composer ribbon** (RC-06) — unified Word-style ribbon merging report
  tools and AI actions into three icon-button groups: Review (Dictate/Voice
  cmds/Validate/Compare/Format draft), AI Compose (Generate Draft, blue
  filled/Generate Impression/Rewrite ▾ — Concise/Formal/Patient-friendly/
  Referring summary/Custom edit, each iconed/In my style/scope chip), and
  Sign-off (Sign & send/Acknowledge & lock/Review & sign). AI activity rail.
- **Right rail tabs** — Review checklist (progress ring) / Details /
  Validation (RC-04) / AI activity / Export (RC-09: destinations with Ready
  badges → per-destination format → validation gate → data-boundary notice →
  passkey step-up → Blocked/Sending/Delivered/Failed states).
- **Provenance modal** (RC-07) — evidence spans, prompt/rulebook/model chain,
  role views; Loading / Source unavailable states.
- **Dictation bar** (RC-08) — six states, §4.9.
- Autosave / unsaved / sync-conflict banners per RC-02.

Existing ribbon/inspector helpers (`.rp-doc-header`, `.rp-ribbon*`,
`.rp-report-body`, `.rp-doc`, `.rp-inspector*`, `.rp-menu*`) remain the class
substrate and are restyled by the tokens; every mockup state frame is built
from the §4.16 state trio.

### 4.13 Prompt Studio (`/prompts`)

Two-pane workspace inside `<Container>` + `<PageHeader>`: `.rp-context-bar`
(rulebook `SearchableSelect` + status `.badge` + `.rp-chip-dirty` unsaved
indicator) over `.rp-grid-2.rp-studio-grid`. Block editor classes
(`.rp-block-panel`, `.rp-block-list`, `.rp-block-card`, `.rp-block-head/`
`-title/-key/-desc`, `.rp-prompt-textarea`, `.rp-block-footer`,
`.rp-add-block*`); workspace tabs reuse §4.7 with `.rp-tab-body` /
`.rp-tab-intro`; output diff reuses §4.8 plus `.rp-diff-controls` /
`.rp-diff-legend` / `.rp-diff-panel` / `.rp-diff-gutter`; approval rows
`.rp-approval-*` gated by `isMedicalDirector`. Test Runner posts to
`POST /api/prompts/validate` (transient, never persisted).

### 4.14 Auth entrance (`/login`, `/register`, `/pair`)

The only routes that bypass the sidebar shell (`AppShell` renders them inside
`.rp-public-auth-content`). All share `components/auth/AuthScaffold.tsx` — a
split-screen with a branded aside (blue brand panel in the RC restyle) and
the focused auth card. **A `<ThemeToggle />` sits on the auth card
(THEME-002).** Classes: `.rp-auth-split` / `.rp-auth-aside` (hidden ≤880px) /
`.rp-auth-aside-motif` / `.rp-auth-main` / `.rp-auth-card` (entrance
animation gated by `prefers-reduced-motion`), aside content
(`.rp-auth-brand`, `.rp-auth-headline`, `.rp-auth-features`,
`.rp-auth-trust`), card chrome (`.rp-auth-head/-form/-actions/-divider/`
`-hint`, `.rp-field-hint`/`.rp-field-error`, `.rp-auth-foot`), pairing rail
(`.rp-pair-steps` over the `.rp-pair-code` chip). Viewport-fit compaction
tiers at `≤1020px` / `≤840px` keep the entrance scroll-free.

Functional model: **password + mandatory TOTP + biometric** (magic-link
removed); SSO when enabled; device pairing; org bootstrap is CLI-gated.

### 4.15 Friendly copy + widescreen layout (locked copy rules)

Layout: `.rp-container` max-width 1600px (`.fluid` / `.narrow` opt-ins);
`.rp-page-grid` two-column (`minmax(0,1fr) 320px`, collapses <1080px) with
`.rp-page-main` + `.rp-page-aside`; `.rp-help` / `.rp-help-title` sidecar
help cards on `--bg-subtle`; `.rp-advanced` styled `<details>` for technical
fields.

Copy rules (locked):

- **No PRD codes, iteration codes, or API paths** in user-visible strings.
- **No raw acronyms** as labels (`WADO-RS`, `DICOMweb`, `CMK`, `SCIM`,
  `OIDC`, `RBAC`) — plain English in copy (`imaging archive`, `encryption
  key`, `single sign-on`); technical detail goes inside `.rp-advanced`.
- **No env-var scheme samples** (`env:NAME`, `aws:arn:…`) shown to end users.
- **No jargon as severity labels** — the severity dropdown asks "How strict
  should the safety check be?" with friendly answers while preserving the
  `Info`/`Warning`/`Blocker` enum.

### 4.16 State trio (loading / empty / error) — locked

Every data-driven page renders **`<Skeleton />`** while loading,
**`<EmptyState />`** for zero rows, and **`<ErrorState onRetry />`** on fetch
failure. This trio is unchanged by the RC redesign — the components are
restyled by the tokens and are the substrate for every mockup state frame
(RC-01 "Loading", RC-03 "Empty", RC-04 "Engine offline", RC-09 "Failed", …).
Do not hand-roll spinners, "no data" paragraphs, or error `<div>`s.

### 4.17 On-device model manager (`.rp-model-*`)

`components/models/OnDeviceModels.tsx`, in `radiopad.css`. Cards for the local
model catalog, grouped by kind.

- `.rp-model-section` + `.rp-model-section-head` (icon, `<h3>`, and a
  right-aligned `.rp-model-section-count` reading "2 of 3 ready").
- `.rp-model-grid` — `auto-fill minmax(340px, 1fr)`, single column ≤720px.
- `.rp-model-card` — `.rp-model-card-head` (`.rp-model-icon[data-kind]`,
  `.rp-model-headings` with `.rp-model-title` + mono `.rp-model-id`, badge),
  then `.rp-model-meta` chips, `.rp-model-note`, `.rp-model-actions`, and
  `.rp-model-msg[data-tone]`.
  - `data-selected="true"` → accent border on `--bg-selected` (primary engine,
    or registered as a report-generation provider).
  - `data-state="failed"` → `--red-border`.
- `.rp-model-chain` / `.rp-model-chain-step[data-ok]` — the **prerequisite
  chain**. An orchestrator model needs its GGUF *and* a llama.cpp runtime *and*
  a running server; each link is its own row so a half-installed chain is
  readable rather than inferred from a failed action.

**Truthfulness rule.** A model card must never show "Ready" for a state that
cannot actually run, and must never offer an action the backend will reject
(`SetPrimary` is STT-only; `Delete` is `HostedFile`-only). This screen regressed
on exactly that once — the card claimed Ready, offered Test and Make primary,
and both failed with a message blaming a missing download — so prefer an honest
"Setup incomplete" badge plus a visible chain over a single optimistic pill.

---

## 5. Iconography

Use inline SVG only (lucide-react or bespoke 1.5px-stroke icons inheriting
`currentColor`). No emoji in UI chrome — the `✨` glyph inside the `.ai-mark`
label and chip is the single sanctioned exception (it is part of the locked
"generated" wordmark, always accompanied by text). Brand mark is the blue
`.brand-mark` tile.

---

## 6. Motion

Motion is a **first-class, token-driven layer** of the design system. Every
animation is **fully gated by `prefers-reduced-motion`** and must never
compromise the clinical workflow: **no decorative motion in clinical views** —
on the report-editing/dictation canvas, motion is limited to entrance and
state-change feedback; nothing animates continuously behind text being read,
dictated, or signed. Motion never implies an action the system didn't take
(RadioPad never auto-signs; AI text keeps `.ai-mark` until acknowledged).

**Where it lives.** Tokens in `frontend/app/tokens.css`; the keyframe library
+ entrance/stagger utilities + motion-driven components (Banner, Toast,
Reveal, PageTransition) in `frontend/app/motion.css`; Tailwind utilities
mirrored in `frontend/tailwind.config.ts`.

**Tokens.**
- Easings: `--ease-out/in/in-out` (UI) + expressive `--ease-pop`,
  `--ease-snap`, `--ease-spring`, `--ease-overshoot`.
- Transition durations: `--dur-fast` 120ms, `--dur-base` 180ms, `--dur-slow`
  260ms; composed `--transition-fast|base|slow|snap|spring`.
- Animation durations: `--anim-fast` 160ms, `--anim-base` 260ms,
  `--anim-slow` 420ms, `--anim-spin` 700ms.
- Stagger scale: `--delay-1…8` (40ms steps); `.rp-stagger` cascades direct
  children.

**Vocabulary.**
- Entrance: `.rp-anim-fade-in[-up|-down]`, `.rp-anim-scale-in`,
  `.rp-anim-pop-in`, `.rp-anim-slide-left|right`, `.rp-anim-spring-in`; route
  changes via `<PageTransition>`; on-scroll via `<Reveal>`.
- Interaction: hover/focus transitions on background/border/shadow/transform
  via `--transition-*`; buttons get a tactile `:active` press.
- Attention/feedback: `.rp-motion-pulse`, `.rp-motion-glow`, count-ups via
  `<AnimatedNumber>`, status via `<Banner>` / toasts.
- Dictation feedback (RC-08) must render state changes in **<150ms** —
  use `--dur-fast`.

**Theme switching is not animated** — a theme change swaps variables in one
frame (THEME-005); do not add cross-fade transitions to surfaces for it.

**Reduced motion.** A single global `@media (prefers-reduced-motion: reduce)`
rule in `tokens.css` neutralizes transitions/animations app-wide (collapsing
infinite spinners to a single frame). Do not re-implement reduced-motion
handling per component.

Verify the catalog and reduced-motion behavior at the dev route
**`/design/motion`** — in both themes.

---

## 7. Accessibility

- **WCAG 2.2 AA** contrast is required for every text token against its
  documented surfaces **in both themes** — the §2 palettes are tuned for
  this; spot-check any new pairing in light *and* dark.
- Focus rings must always be visible; global `:focus-visible` ⇒
  `--color-focus-ring` ring, 2px offset (works on both themes).
- Status, severity, and provenance never rely on hue alone: findings carry
  explicit text labels (`Blocker`, `Warning`, `Info`), chips carry their
  words, and the AI marker carries the "✨ generated — review required"
  caption.
- **Windows High Contrast / `forced-colors`**: a `forced-colors: active`
  block in `tokens.css` keeps `forced-color-adjust: auto` on status/
  provenance chrome so system colours apply and meaning survives via labels
  + borders. (A bespoke HC palette is a documented follow-up.)
- **Reduced motion**: single global idiom (§6).
- Native semantics: theme toggle is `role="switch"` with `aria-checked`;
  busy buttons use `aria-busy`; comboboxes/tabs follow the ARIA patterns
  already encoded in the locked classes.

---

## 8. Don't list

The following are **forbidden**:

1. Importing Material UI, Ant Design, Chakra, or Bootstrap. (Build-time
   **Tailwind 3** is allowed — the banned items are component/theme
   frameworks that would fight the RC tokens.)
2. **Hardcoding colours** — hex/rgb/named colours in feature CSS, inline
   styles, or TSX. Tokens only; that is what keeps both themes correct.
   (Sanctioned exceptions: the intentionally-dark `.op-bash` terminal block
   and present-mode surfaces.)
3. **Shipping a UI change verified in only one theme.** Dark mode is
   required, not optional; so is light. (The old "no dark mode / light-only"
   rule is revoked and must not be reintroduced.)
4. Adding new accent colours.
5. Using emoji as functional icons (§5; the `.ai-mark` `✨` wordmark is the
   sole exception).
6. Using a primary navigation pattern other than the canonical left-sidebar
   shell (§3.1) — no header-heavy nav, no bottom-tab nav, no
   command-palette-only nav (Cmd+K search *augments* the sidebar, it does not
   replace it).
7. Adding heavy dropshadows that imply elevation beyond `--shadow-md`
   (modals/menus use `--bg-elevated` + `--shadow-lg`, nothing more).
8. Using rounded-full pills for things that aren't status/chips.
9. Serif report prose — the serif stack is retired; `--serif` exists only as
   a legacy alias.
10. Re-implementing theme plumbing — always go through `lib/theme.ts` and
    `data-theme`; never toggle classes or set colours from feature code.

---

## 9. Cheat sheet for new screens

When you build a new RadioPad surface:

1. The page renders inside `<AppShell>`; do not re-implement chrome.
2. Use `<Container>` + `<PageHeader title description primaryAction />` for
   the top of every page.
3. Inside the page, use `.rp-panel` / `.view-panel` for grouped content and
   `.section-block` (label + control) for every form field.
4. For data-driven pages, render `<Skeleton />` while loading,
   `<EmptyState />` for zero rows, `<ErrorState onRetry />` on failure
   (§4.16).
5. Primary action uses `.primary` (one per surface); everything else uses
   `.ghost` / `.subtle`.
6. Status lives in semantic pills/chips (§3.6), not standalone colours.
7. AI-generated content always wears `.ai-mark` + the review pairing (§4.1).
8. Everything is Inter; IDs/hashes are `--mono`; no serif.
9. Two-pane editor surfaces opt into `<Container fluid>` + `.split` (§3.1.1).
10. **Check the screen in BOTH themes** (toggle in the topbar/auth card, or
    `localStorage.setItem('rp-theme','dark')` + reload). Use `agent-browser`
    to screenshot light and dark; also sanity-check a print preview on
    report/export surfaces (must render light — THEME-015).
11. Copy follows §4.15 (no codes, no raw acronyms, no env-var grammar).

If a new pattern doesn't fit any of the above, stop and propose a token or
class addition to `tokens.css` (+ `tailwind.config.ts`) in a PR before
shipping it. Do not improvise.

---

## 10. Internationalization

RadioPad ships chrome translations for **six locales**: `en` (default,
canonical source of truth), `es`, `de`, `fr`, `pt`, `hi`. The locale picker
lives in the topbar as a `select.subtle`; no new design token.

**Locale negotiation** (resolved on each request / first paint):

1. `?lang=<tag>` query parameter (writes the cookie + redirects).
2. `radiopad-locale` cookie (`SameSite=Lax`, 1-year max-age).
3. `Accept-Language` header (best-of, `q=` weighting).
4. Tenant default: `TenantSettings.Locale` via
   `GET /api/tenant/settings/locale`.
5. `en` fallback.

Per-user override on `User.PreferredLocale` via `PUT /api/users/me/locale`;
tenant default via `PUT /api/tenant/settings/locale` (`ItAdmin` /
`MedicalDirector` only).

**Clinical content stays English.** Rulebook YAML, finding/lexicon text, and
validation messages emitted by `RadioPad.Validation` are **never** translated.
Message bundles in `frontend/messages/*.json` deliberately omit those keys;
localized chrome for clinical surfaces (severity labels, banner copy) goes in
the `validation.severity` / `banner` namespaces, never a "rulebook" namespace.
