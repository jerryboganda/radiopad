# BRAND-LOCK — RadioPad Marketing Site

> **This palette is LOCKED.** Every color below is extracted verbatim from the RadioPad
> product's canonical token source, [`frontend/app/hallmark.css`](../frontend/app/hallmark.css).
> The marketing site reuses these values **exactly** — no new brand hues, no shifted
> scheme, no re-tinting. Fonts, spacing, type scale, layout, and motion are free to change
> (see "Design direction" at the bottom).
>
> OKLCH is the source of truth (copy it directly into CSS). Hex is a computed sRGB
> equivalent (Björn Ottosson OKLCH→sRGB, D65) for tooling that can't read OKLCH — treat
> hex as the fallback, OKLCH as canonical.

---

## 1. Locked palette

### Surfaces (warm "paper")
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-paper` | `oklch(96.5% 0.012 75)` | `#f8f2eb` | Page background | hallmark.css:22 |
| `--color-paper-soft` | `oklch(99% 0.006 75)` | `#fefbf7` | Elevated surface / card / button text on accent | hallmark.css:23 |
| `--color-paper-warm` | `oklch(93% 0.02 70)` | `#f1e6da` | Subtle section bg / hover fill | hallmark.css:24 |

### Ink (text)
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-ink` | `oklch(20% 0.022 55)` | `#1e130c` | Primary text / headlines | hallmark.css:25 |
| `--color-ink-soft` | `oklch(38% 0.018 55)` | `#4a4039` | Secondary / body text | hallmark.css:26 |
| `--color-ink-mute` | `oklch(50% 0.012 60)` | `#69625d` | Faint / caption / disabled text | hallmark.css:27 |

### Rules (borders)
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-rule` | `oklch(86% 0.014 70)` | `#d7d0c7` | Primary border / hairline | hallmark.css:28 |
| `--color-rule-soft` | `oklch(91% 0.01 70)` | `#e6e0da` | Soft divider | hallmark.css:29 |

### Brand accent (terracotta) — the single accent, used site-wide
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-accent` | `oklch(58% 0.18 35)` | `#ce4522` | Primary CTA fill, brand mark | hallmark.css:32 |
| `--color-accent-deep` | `oklch(42% 0.2 32)` | `#9e0000` | CTA hover/active, **accent-colored small text & links** | hallmark.css:33 |
| `--color-accent-soft` | `oklch(82% 0.08 45)` | `#f1b499` | Accent tint background | hallmark.css:34 |

### Semantic states (used sparingly on marketing — mostly for iconography / validation storytelling)
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-success` | `oklch(50% 0.09 150)` | `#397247` | Success / "validated" green | hallmark.css:41 |
| `--color-success-soft` | `oklch(90% 0.04 145)` | `#cee6ce` | Success tint | hallmark.css:42 |
| `--color-marine` | `oklch(34% 0.09 240)` | `#003c61` | Info blue | hallmark.css:39 |
| `--color-marine-soft` | `oklch(83% 0.045 240)` | `#aecce2` | Info tint | hallmark.css:40 |
| `--color-danger` | `oklch(52% 0.17 25)` | `#b63132` | Error / "blocker" red | hallmark.css:43 |
| `--color-danger-soft` | `oklch(89% 0.055 32)` | `#fdcec4` | Error tint | hallmark.css:44 |
| `--color-saffron` | `oklch(78% 0.16 78)` | `#efa810` | Warning amber | hallmark.css:37 |
| `--color-saffron-soft` | `oklch(91% 0.07 80)` | `#faddad` | Warning tint | hallmark.css:38 |

### Clinical-safety marker (purple) — carry it for brand fidelity even if lightly used
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-ai` | `oklch(45% 0.14 300)` | `#623e96` | "AI-generated" disclosure purple — must stay visually distinct from info-blue | hallmark.css:49 |
| `--color-ai-soft` | `oklch(90% 0.05 300)` | `#e3d7fb` | AI tint | hallmark.css:50 |

### Focus
| Token | OKLCH (canonical) | Hex (computed) | Role | Source |
|---|---|---|---|---|
| `--color-focus-ring` | `oklch(48% 0.2 32)` | `#b30000` | Keyboard focus outline (2px) | hallmark.css:45 |

**Theme:** Light-only. No dark-mode token set exists in the product, and the brief
explicitly requires "BRIGHT / LIGHT (never dark)." The whole site locks to one light
theme (taste-skill §4.11 Page Theme Lock).

---

## 2. Usage notes & guardrails

- **One accent, locked** (taste-skill §4.2 Color Consistency Lock): terracotta
  `--color-accent` is the *only* accent across the entire site. No blue/teal/rose CTAs
  sneaking into later sections. Semantic colors (green/blue/red/amber/purple) appear
  **only** where they carry real meaning (e.g. illustrating the validation engine:
  blocker=red, warning=amber, info=blue, validated=green; the AI-disclosure purple).
  They are never decorative accents.

- **⚠ Accent contrast rule (WCAG, verified):**
  - `--color-accent` `#ce4522` on `--color-paper` = **4.20:1** → passes AA for **large
    text only** (≥24px / ≥18.66px bold). **Fails** AA for normal-size text/links.
  - Therefore: accent-colored **small text, inline links, and small labels use
    `--color-accent-deep` `#9e0000`** (7.70:1 on paper — AA ✓). Reserve bright
    `--color-accent` for large display words, icon fills, and button *fills* (where the
    label sits on the accent, not the accent on paper).
  - `--color-paper-soft` `#fefbf7` on `--color-accent` fill = **4.52:1** → AA ✓ for
    button label text. Good.

- **Verified contrast (text on `--color-paper` `#f8f2eb`):**
  | Pair | Ratio | Verdict |
  |---|---|---|
  | ink `#1e130c` | 16.38 | AA ✓ (AAA) |
  | ink-soft `#4a4039` | 9.07 | AA ✓ (AAA) |
  | ink-mute `#69625d` | 5.39 | AA ✓ (body) |
  | accent-deep `#9e0000` | 7.70 | AA ✓ |
  | accent `#ce4522` | 4.20 | large text only |
  | marine `#003c61` | 10.39 | AA ✓ |
  | success `#397247` | 5.14 | AA ✓ |
  | danger `#b63132` | 5.44 | AA ✓ |

- **Shadows** tint to the paper hue, never pure black (taste-skill §4.4). Use warm,
  low-opacity ink shadows, e.g. `0 18px 60px color-mix(in oklch, var(--color-ink), transparent 90%)`.

- **Backgrounds:** alternate only *within* the warm family — `--color-paper` ↔
  `--color-paper-soft` ↔ `--color-paper-warm`, plus occasional `--color-accent-soft`
  or a full `--color-ink` "reverse" band for one deliberate high-contrast section.
  Never flip to a cool-gray or a different hue family mid-page.

---

## 3. Reference-only (NOT locked — free to replace on the marketing site)

Captured from the product for reference; the marketing site replaces these with its own
system (see Design Direction). Product source: `frontend/app/hallmark.css`.

- **Fonts (product):** display `Aptos Display`, body `Aptos`, mono `Cascadia Mono`,
  serif `Source Serif Pro`. These are Microsoft/Adobe system fonts, **not freely
  web-embeddable**, so the marketing site uses a new self-hosted pairing.
- **Spacing (product):** 11-step 4px grid, `--space-1`…`--space-11` (4→192px).
- **Type scale (product):** 8-step, `--text-xs` 12px … `--text-display` 56px.
- **Motion tokens (product):** durations 120/180/260ms; easings include
  `--ease-out cubic-bezier(0.16,1,0.3,1)`, `--ease-spring cubic-bezier(0.175,0.885,0.32,1.275)`,
  `--ease-pop`, `--ease-overshoot`. **We will reuse `--ease-out` (0.16,1,0.3,1) as the
  primary marketing easing** — it's the product's signature curve and a free way to keep
  motion feeling on-brand while everything else upgrades.

---

## 4. How these tokens ship into the Astro site (Phase 4)

Mirror the product's pattern: a `:root` block of `--color-*` custom properties (verbatim
OKLCH from §1) + a matching Tailwind v4 `@theme` mapping, so both CSS-variable and
utility (`bg-paper`, `text-ink`, `bg-accent`) authoring resolve to the identical locked
values. No hardcoded hex in components — always `var(--color-*)` or the mapped utility.
