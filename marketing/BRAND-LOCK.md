# BRAND-LOCK: RadioPad marketing site ("reading room" design)

> **Locked on 2026-11-21** by owner decision. This replaces the earlier RC-palette lock (light clinical
> white + RadioPad blue, 2026-07-13) and the Hallmark lock before it. The owner reviewed three
> homepage directions (Dark Cinematic, Clinical Editorial, Kinetic Bento) and chose **Dark Cinematic**.
> The approved homepage mockup is `mockups/v2/option-a-cinematic.html`.
>
> Scope: the marketing site only (`marketing/`). The product UI keeps its own RC design system
> (`frontend/app/tokens.css`, see the repo `CLAUDE.md`); the two no longer share a palette.

## Concept

Radiologists read in dark rooms on grayscale monitors. The site is that room: a deep blue-black
canvas, film-white type, one pale ice-blue accent for light and focus, and clinical state colours
used only where they mean something (validation severities, AI-draft marking, signed state).
Bands are separated by thin animated light beams instead of colour changes.

The site is **dark only** by design (`color-scheme: dark`, `theme-color #04070c`). There is no light
theme and no toggle.

## Tokens (canonical source: `src/styles/site.css` `:root`)

| Token | Value | Role |
|---|---|---|
| `--bg` | `#04070c` | Page background |
| `--bg-1` / `--bg-2` / `--bg-3` | `#070b12` / `#0b111b` / `#111a27` | Raised surfaces, tiles, inputs |
| `--line` / `--line-2` / `--line-3` | `rgba(160,190,225,.10/.17/.28)` | Hairlines, borders, hover borders |
| `--fg` | `#e8eef6` | Primary text |
| `--fg-2` | `#a7b4c4` | Body / secondary text |
| `--fg-3` | `#6e7d90` | Captions, labels, mono readouts |
| `--film` | `#f3f7fb` | Headline white, primary button fill |
| `--accent` | `#86c5ff` | The single accent: icons, links, focus ring, beams |
| `--accent-deep` | `#4d9df0` | Accent gradients |
| `--red` / `--amber` / `--blue` / `--green` | `#ff6b6b` / `#f3b54a` / `#82a9ff` / `#58d3a0` | Blocker / warning / info / validated-signed only |
| `--ai` | `#9cc9ff` | "AI draft, requires review" marking |

Every semantic colour has a matching `*-bg` tint at 9% for chips and flags.

## Type

Self-hosted variable fonts in `public/fonts/` (latin subset, from Google Fonts):

- **Bricolage Grotesque** (display, 400 to 700, optical size axis): headlines, tight tracking (-0.03 to -0.05em).
- **Geist** (body and UI, 300 to 700).
- **Geist Mono** (400 to 500): eyebrows, DICOM-style overlays, code, data labels; uppercase with 0.1 to 0.14em tracking.

## Motion

- Easing: `--ease-out: cubic-bezier(0.23,1,0.32,1)` for UI, `--ease-io: cubic-bezier(0.77,0,0.175,1)` for on-screen travel.
- Press feedback `scale(.97)` on every pressable element; hover effects only under `(hover:hover) and (pointer:fine)`.
- Scroll motion (GSAP + ScrollTrigger + SplitText from npm, Lenis smooth scroll vendored in `src/vendor/lenis.mjs`): `data-reveal` fade-up, `data-split` line-mask headings, the pinned Dictate → Draft → Validate → Sign sequence.
- Signature moments: the synthetic CT slice + self-typing report in the home hero, and the auto-advancing Platform explorer.
- `prefers-reduced-motion`: no smooth scroll, no movement, demos render their final state.

## Guardrails

- One accent. Semantic colours never decorate.
- AI-drafted text always wears the `.ai-tag` / `.ai-text` treatment until a radiologist reviews it.
- No fake logos, testimonials, certifications, prices or statistics. Numbers are product-intrinsic or labelled illustrative.
- No hardcoded colours in new components: use the tokens above.
