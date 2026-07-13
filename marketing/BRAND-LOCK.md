# BRAND-LOCK — RadioPad Marketing Site (RC design system)

> **This palette is LOCKED.** Every color below is taken verbatim from the RadioPad
> product's canonical token source, [`frontend/app/tokens.css`](../frontend/app/tokens.css)
> (the **RC design system**, PRD v3.0 §20; reference mockups RC-01…RC-10 at
> `UI UX SCREENS/Authentication/`). The marketing site reuses these values **exactly** —
> no new brand hues, no shifted scheme, no re-tinting. Fonts, spacing, type scale,
> layout, and motion remain free (see "Design direction" at the bottom).
>
> **Locked on 2026-07-13. This lock SUPERSEDES the previous Hallmark lock**
> (warm-paper / terracotta, sourced from the retired `frontend/app/hallmark.css`).
> No Hallmark value may appear anywhere on the site.
>
> Hex is canonical here (the product ships hex). The marketing token names keep the
> site's original surface vocabulary (`paper` / `ink` / `rule` / `accent`) but every
> value is an RC primitive: `paper`=canvas, `paper-soft`=surface, `paper-warm`=surface-subtle.

---

## 1. Locked palette — LIGHT (default) and DARK (first-class)

Both schemes are mandatory. The site follows the visitor's OS preference via
`@media (prefers-color-scheme: dark)` — no toggle on marketing. Dark is the RC
deep-navy theme, **never pure black**.

### Surfaces (clinical white / deep navy)
| Token | Light | Dark | Role | RC source name |
|---|---|---|---|---|
| `--color-paper` | `#f5f8fb` | `#0b1422` | Page background | `--color-canvas` |
| `--color-paper-soft` | `#ffffff` | `#111d2d` | Card / panel / elevated surface | `--color-surface` |
| `--color-paper-warm` | `#eef3f8` | `#152235` | Subtle section bg / hover fill | `--color-surface-subtle` |

### Ink (text)
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-ink` | `#0f1f38` | `#edf6ff` | Primary text / headlines |
| `--color-ink-soft` | `#40536b` | `#b9c7d7` | Secondary / body text |
| `--color-ink-mute` | `#5d7085` | `#8ca0b5` | Faint / caption text |

### Rules (borders)
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-rule` | `#d8e2eb` | `#2b3d52` | Primary border / hairline |
| `--color-rule-soft` | `#e7eef4` | `#223349` | Soft divider |

### Brand accent (RadioPad blue) — the single accent, used site-wide
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-accent` | `#2f88d8` | `#3b82f6` | Primary CTA fill, brand mark |
| `--color-accent-deep` | `#1f6fb8` | `#619bf8` | CTA hover; **accent-colored small text & links** (dark hover/link is deliberately LIGHTER, per RC) |
| `--color-accent-soft` | `#bbdcf6` | `#1e3a5f` | Soft accent fill / tint block |
| `--color-accent-tint` | `#eaf3fc` | `#16283f` | Tinted chip / selected background |
| `--color-accent-fg` | `#ffffff` | `#ffffff` | Text/icon ON accent fills (both schemes) |

### Semantic states (only where they carry meaning — validation storytelling)
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-success` | `#11845b` | `#2fbf87` | Success / "validated" |
| `--color-success-soft` | `#dcf2e8` | `#0e2b22` | Success tint |
| `--color-warning` | `#a65e00` | `#e5a43b` | Warning amber |
| `--color-warning-soft` | `#fbf1de` | `#33270f` | Warning tint |
| `--color-danger` | `#c43d3d` | `#e36868` | Error / "blocker" |
| `--color-danger-soft` | `#fae3e3` | `#351619` | Error tint |
| `--color-info` | `#2565ae` | `#6fa8e8` | Info blue |
| `--color-info-soft` | `#dfecfa` | `#12263c` | Info tint |

### Clinical-safety marker (AI blue — distinct from accent and info)
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-ai` | `#1d63c2` | `#7cb8f5` | "AI-generated" disclosure |
| `--color-ai-soft` | `#e8f1fd` | `#142943` | AI tint |

### Focus
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-focus` | `#5eaaf0` | `#83c4ff` | Keyboard focus outline (2px) |

### Reverse "band" surfaces (marketing-only composite tokens)
The site's high-contrast reverse sections (final CTA band, demo band, dark bento cell,
code blocks) do **not** use raw `--ink` as a background — ink flips light in dark mode.
They use dedicated band tokens built from RC values:

| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-band` | `#0f1f38` (ink navy) | `#16273b` (RC elevated) | Reverse band background |
| `--color-band-ink` | `#f5f8fb` | `#edf6ff` | Text on the band |
| `--color-band-accent` | `#bbdcf6` | `#7cb8f5` | Accent marks ON the band |

Shadows tint to a fixed navy shade (`--shadow-ink`: light `#0f1f38`, dark `#020812`),
never `var(--ink)` and never pure black.

---

## 2. Usage notes & guardrails

- **One accent, locked:** RadioPad blue `--color-accent` is the *only* accent across the
  entire site. No terracotta, teal, or rose anywhere. Semantic colors (green/amber/red/
  info-blue/AI-blue) appear **only** where they carry real meaning (validation engine:
  blocker=red, warning=amber, info=blue, validated=green; the AI-disclosure treatment).
  They are never decorative accents.

- **Accent contrast rule (matches the product):**
  - Small accent-colored text and inline links use `--color-accent-deep`
    (light `#1f6fb8` = 4.9:1 on canvas, AA ✓; dark `#619bf8` = 6.6:1 on canvas, AA ✓).
  - Bright `--color-accent` is reserved for button *fills*, icon fills, and large display
    accents. Labels on accent fills always use `--color-accent-fg` (white) — never a
    surface token, which would go dark in the dark scheme.
  - Tinted chips (e.g. blog tags) pair `--color-accent-deep` text on
    `--color-accent-tint` backgrounds (AA ✓ in both schemes); `--color-accent-soft` is
    too strong a background for small text.

- **Both schemes are mandatory.** Every new section/component must be checked in light
  AND dark before it ships. Never hardcode a hex in `.astro`/`.svelte`/section CSS —
  always `var(--*)` aliases (or the mapped Tailwind utility), so both schemes resolve.

- **Backgrounds:** alternate only within the RC surface family — `--color-paper` ↔
  `--color-paper-soft` ↔ `--color-paper-warm`, plus occasional `--color-accent-soft` /
  `--color-accent-tint`, or a deliberate `--color-band` reverse band. Never flip to a
  warm hue or a different color family mid-page.

- **Brand mark:** blue rounded square (`--color-accent`) with the white waveform stroke
  (`#ffffff` = `--color-accent-fg`). Works unchanged on both schemes.

---

## 3. Reference-only (NOT locked — marketing's own system, unchanged by this re-lock)

- **Fonts (marketing):** display **Clash Display**, body **General Sans**, mono
  **JetBrains Mono** — self-hosted woff2. Marketing typography was always free and is
  deliberately NOT switched to the product's Inter. (Product fonts for reference:
  Inter Variable everywhere, Cascadia Mono for IDs.)
- **Spacing / type scale / radii:** marketing's fluid scale in `src/styles/tokens.css`
  stays as-is.
- **Motion:** primary easing stays `cubic-bezier(0.16, 1, 0.3, 1)` — the product's
  signature `--ease-out` curve (still shipped in `frontend/app/tokens.css`).

---

## 4. How these tokens ship into the Astro site

Mirrors the product's pattern:

1. **Canonical values** live in the Tailwind v4 `@theme` block in
   [`src/styles/app.css`](src/styles/app.css) (light values), emitted as `:root`
   custom properties AND utilities (`bg-paper`, `text-ink`, `bg-accent`, …).
2. **Dark scheme** overrides the same `--color-*` custom properties in a
   `@media (prefers-color-scheme: dark)` block directly below `@theme`.
3. **Alias layer** in [`src/styles/tokens.css`](src/styles/tokens.css) re-points the
   short names (`--paper`, `--accent`, `--band`, …) used by all section CSS.
4. No hardcoded hex in components — always `var(--color-*)` / aliases / utilities.

Asset debt from the re-brand (generated art, tracked separately): `public/og-default.png`
still renders the old terracotta OG card — regenerate via `scripts/gen-assets.mjs`
(template already re-pointed to RC in this lock).
