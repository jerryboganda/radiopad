# Building with the RadioPad RC design system

RadioPad is an AI-assisted radiology reporting product. Its RC design system is a
light-first white/blue clinical SaaS look with a first-class deep-navy dark theme.
Before styling anything, read the shipped stylesheets — they are the truth:
`styles.css` imports `tokens/globals.css` (element defaults, buttons, inputs),
`tokens/tokens.css` (ALL design tokens, light + dark), `tokens/motion.css`,
`tokens/radiopad.css` (the `.rp-*` component classes), and `_ds_bundle.css` (sidebar
shell + page chrome). `guidelines/design.md` is the full design doc; each component's
`.prompt.md` shows its intended composition.

## Setup

No provider or wrapper is required — every component works standalone. Body styles
(Inter, 13.5px, `--bg-app` background) apply automatically. Light is the default
theme; set `data-theme="dark"` on `<html>` to render the deep-navy dark theme —
every token flips, never use pure black. A bare `<button>` is already a RadioPad
button; inputs/selects/textareas are styled at the element level too.

## Styling idiom — tokens + documented classes, never hardcoded colors

There are NO utility classes (no Tailwind vocabulary). Style in this order:
1. Library components from `window.RadioPad` (preferred).
2. The documented CSS classes below for chrome the library doesn't cover.
3. Inline styles whose colors/radii/shadows are `var(--*)` tokens. Never hex/rgb.

Core tokens — surfaces: `--bg-app`, `--bg`, `--bg-panel`, `--bg-elevated`,
`--bg-subtle`, `--bg-muted`, `--bg-selected`; text: `--text`, `--text-strong`,
`--text-muted`, `--text-faint`, `--text-inverse`; borders: `--border`,
`--border-soft`, `--border-strong`; accent: `--accent`, `--accent-hover`,
`--accent-soft`, `--accent-tint`, `--accent-fg` (text on accent); semantic families
(each with `-bg` and `-border`): `--green`, `--blue`, `--red`, `--amber`, `--ai`,
`--purple`; plus `--navy`, `--link`, `--scrim`; radii: `--radius-sm`, `--radius`,
`--radius-lg`, `--radius-pill`; shadows: `--shadow-xs/-sm/-md/-lg`; fonts:
`--sans`, `--mono`. Validation severity mapping: Blocker→red, Warning→amber,
Info/Style→blue.

Key classes — buttons: `.primary`, `.primary-ghost`, `.ghost`, `.subtle`,
`.icon-btn` (all valid on `<button>` and `<a>`); chips: `.badge` (variants
`.badge.ai` "✨ generated", `.badge.warn`), `.rp-status` + tone class
(`neutral|info|success|warning|danger|ai`); layout: `.rp-shell`, `.rp-sidebar`,
`.rp-topbar`, `.rp-page-header`, `.rp-panel`, `.section-block`; report surfaces:
`.rp-sectioncard`, `.finding`, `.composer`, `.msg`; feedback: `.rp-banner`,
`.rp-empty`, `.rp-toast`, `.rp-spinner`.

## Non-negotiable product rules

- AI-generated text wears `.ai-mark` (tinted field + "✨ generated" label) paired
  with an amber "Requires review" flag until a radiologist reviews it — never
  hue-only. Use `<SectionCard generated>` or `<Banner tone="ai">` for this.
- Radiologists sign reports; never render an auto-sign affordance.
- Pages live in a left-sidebar shell (`.rp-shell` with `.rp-sidebar` +
  `.rp-topbar`); start page content with `Container` + `PageHeader`.

## Idiomatic page block

```jsx
const { Container, PageHeader, SectionCard, Banner, StatusBadge } = window.RadioPad;

<Container>
  <PageHeader
    title="CT Chest — Report"
    description="Amina Yusuf · MRN 004821 · CT Chest w/o contrast"
    secondaryActions={<button className="ghost">Validate</button>}
    primaryAction={<button className="primary">Sign report</button>}
  />
  <Banner tone="ai" title="AI-generated draft">Review the Impression before signing.</Banner>
  <SectionCard
    sectionKey="impression"
    title="Impression"
    generated
    actions={<><button className="primary">Accept</button><button className="ghost">Undo</button></>}
  >
    <div className="ai-mark"><p>1. Right lower lobe pneumonia.</p></div>
  </SectionCard>
</Container>
```
