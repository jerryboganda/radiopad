# RadioPad Marketing Site

The public website for RadioPad at https://radiopadstudio.com. Static **Astro** site with **GSAP**
(ScrollTrigger + SplitText from npm) and **Lenis** smooth scroll (vendored in `src/vendor/`).
Dark "reading room" design locked in [BRAND-LOCK.md](./BRAND-LOCK.md); the approved homepage
mockup lives in `mockups/v2/option-a-cinematic.html`.

## Commands

Run from the repo root (pnpm workspace) or from `marketing/`:

```bash
pnpm --filter @radiopad/marketing dev       # dev server (HMR) at http://localhost:4321
pnpm --filter @radiopad/marketing build     # static build -> marketing/dist (CI does this; see repo CLAUDE.md)
```

Deploys: a push to `main` touching `marketing/**`, `CHANGELOG.md` or `rulebooks/*.yaml` runs
`.github/workflows/hostinger-deploy.yml`, which builds and rsyncs `dist/` to Hostinger
(`/changelog/` reads `../CHANGELOG.md`, `/product/validation/` reads `../rulebooks/*.yaml` at build time).

## Pages

| Route | Source | Live element |
|---|---|---|
| `/` | `pages/index.astro` + `components/home/*` | CT slice + self-typing report, pinned workflow, platform explorer, audit log |
| `/product/` | `pages/product/index.astro` | Workflow, surfaces, platform explorer |
| `/product/validation/` | `pages/product/validation.astro` | Real `chest_ct_v1` rules + filterable library of all rulebooks (`#cli` section) |
| `/product/dictation/` | `pages/product/dictation.astro` | Phone pairing demo |
| `/solutions/{hospitals,imaging-groups,radiologists}/` | `pages/solutions/[slug].astro` + `data/solutions.ts` | Role switcher / rule-impact demo |
| `/integrations/` | `pages/integrations.astro` | One report as FHIR R4 JSON, PDF, DOCX, text |
| `/security/` | `pages/security.astro` | Data-flow diagram, live audit log (`#ai`, `#disclosure`) |
| `/pricing/` | `pages/pricing.astro` | Plan chooser (no prices) |
| `/download/` | `pages/download.astro` | Latest GitHub release (.msi / .apk) fetched live, with fallback link |
| `/demo/` | `pages/demo.astro` | Demo request form (`/contact` redirects here) |
| `/about/` | `pages/about.astro` | Principles (`#careers`; `/careers` redirects here) |
| `/changelog/` | `pages/changelog.astro` | Repo `CHANGELOG.md`, filter by change type |
| `/blog/`, `/blog/<slug>/`, `/blog/tag/<tag>/` | `pages/blog/*`, `content/blog/*.mdx` | Tag filter, table of contents |
| `/legal/{privacy,terms,dpa,responsible-ai}/` | `pages/legal/[slug].astro`, `content/legal/*.md` | Section list. **Drafts pending legal review.** |
| 404 | `pages/404.astro` | |

## Structure

```
src/
  layouts/BaseLayout.astro   head, icon sprite, nav, <main>, footer, global script
  lib/site.ts                origin, contact addresses, nav mega menus, footer columns
  components/                Nav, Footer, Sprite + Icon, PageHero, SectionHead, Faq, CtaBand, Divider
  components/home/           homepage sections, reusable on inner pages (each at most once per page)
  scripts/site.js            global: Lenis, menus, anchors, magnetic buttons, spotlight, tabs, copy, FAQ, reveals
  scripts/*.js               per-feature demos, imported by the component that renders them
  styles/site.css            tokens, fonts, every shared component
  styles/pages.css           inner-page patterns (grids, tables, prose, forms, notices)
  styles/blog.css            blog and legal reading layout (all classes prefixed)
public/fonts/                Bricolage Grotesque, Geist, Geist Mono (self-hosted woff2)
```

## Configuration

- `src/lib/site.ts`: `email` and `securityEmail` are used everywhere on the site. Set real, monitored
  mailboxes before launch.
- `PUBLIC_DEMO_ENDPOINT` (build-time env): if set, the demo form POSTs JSON there and shows success
  only on a 2xx response. If unset, the form opens the visitor's email app with the request prefilled.

## Rules for new content

- Colours only from the tokens in `site.css`; one accent; semantic colours only for meaning.
- No fake customers, testimonials, certifications, prices or statistics.
- Every page: one `<h1>` (PageHero), unique title and description, `data-reveal` on blocks, ends with `CtaBand`.
- New client JS respects `prefers-reduced-motion` (import `reduce` from `scripts/site.js`).
