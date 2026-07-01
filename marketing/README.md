# RadioPad Marketing Site

Brand-locked marketing site (landing + blog) for RadioPad. Built with **Astro** (SSG),
**Svelte islands**, **GSAP**, and **Tailwind v4**. Colors are locked to the product's
Hallmark palette (see [BRAND-LOCK.md](./BRAND-LOCK.md)); fonts, layout, and motion are
a from-scratch premium redesign built with the `design-taste-frontend` taste-skill.

## Commands

Run from the repo root (pnpm workspace) or from `marketing/`:

```bash
pnpm --filter @radiopad/marketing dev       # dev server (HMR) at http://localhost:4321
pnpm --filter @radiopad/marketing build      # static build -> marketing/dist
pnpm --filter @radiopad/marketing preview     # serve the built dist locally

# convenience scripts from repo root:
pnpm marketing:dev
pnpm marketing:build
```

## Structure

```
marketing/
  astro.config.mjs          # integrations (svelte, mdx, sitemap), tailwind vite plugin, markdown config
  BRAND-LOCK.md             # the locked color palette (OKLCH + hex) + usage rules
  scripts/gen-assets.mjs    # regenerates self-hosted fonts, blog covers, avatars, OG image
  public/
    fonts/                  # self-hosted woff2 (Clash Display, General Sans, JetBrains Mono)
    authors/                # author avatars
    og-default.png          # social share image
    favicon.svg  robots.txt
  src/
    styles/                 # app.css (entry) -> fonts, tokens (alias layer), base, home, blog
    lib/site.ts             # site config (name, url, nav, footer, cta)
    layouts/BaseLayout.astro
    components/
      BaseHead.astro        # <head>: SEO, OG/Twitter, JSON-LD, font preloads
      Nav.svelte            # nav island (mobile toggle + scrolled state)
      Footer.astro
      Motion.svelte         # headless motion island (GSAP loaded here only)
      home/                 # Hero, TrustMarquee, Metrics, Features, HowItWorks, Safety,
                            #   Security, Testimonials, Pricing, Faq, FinalCta
      blog/PostCard.astro
    content.config.ts       # blog collection schema (typed)
    content/blog/*.mdx      # posts
    pages/
      index.astro           # home
      blog/index.astro      # blog index
      blog/[slug].astro     # post template
      blog/tag/[tag].astro  # tag archive
      rss.xml.ts            # RSS feed
      404.astro
```

## Design system

- **Colors are LOCKED** (see BRAND-LOCK.md). Canonical `--color-*` tokens live in the
  Tailwind `@theme` block in `src/styles/app.css`; short aliases (`--paper`, `--accent`,
  ...) mirror the product's `hallmark.css` pattern so component CSS and Tailwind utilities
  resolve to the identical locked values. Do not introduce new brand hues.
- **Fonts:** Clash Display (display), General Sans (body), JetBrains Mono (technical),
  all self-hosted from `public/fonts`. Regenerate with `node scripts/gen-assets.mjs`.
- **Motion:** GSAP + ScrollTrigger live only inside `Motion.svelte`, dynamically imported
  so the blog index and static content never ship GSAP. Everything honors
  `prefers-reduced-motion`, and reveal elements are visible without JS.

## Adding a blog post

Create `src/content/blog/<slug>.mdx`:

```mdx
---
title: "Your title"
description: "One-line summary for cards and SEO."
date: 2026-07-01
tags: ["Product"]
cover: "../../assets/blog/<slug>.jpg"     # local image, optimized by astro:assets
coverAlt: "Describe the cover for screen readers"
author:
  name: "Author Name"
  role: "Role"
  avatar: "/authors/author.jpg"
readingTime: "5 min read"
featured: false
---

## A section heading

Body copy. Use `## H2` headings; the post template builds its table of contents from them.
```

Add the cover image to `src/assets/blog/` and the avatar to `public/authors/`.

## Before deploying

1. Set the real origin in `astro.config.mjs` (`site`) and `src/lib/site.ts` (`url`).
   This drives canonical URLs, the sitemap, the RSS feed, and OG/Twitter absolute URLs.
2. `pnpm marketing:build` outputs a static site to `marketing/dist` (deploy anywhere:
   Netlify, Vercel static, S3, Nginx, or the product's own infra).

## Delivery checklist

**SEO**
- [x] Per-page `<title>`, meta description, canonical
- [x] Open Graph + Twitter card tags + `og-default.png`
- [x] JSON-LD: Organization (home), BlogPosting + BreadcrumbList (posts)
- [x] `@astrojs/sitemap` (`sitemap-index.xml`)
- [x] RSS feed (`/rss.xml`) via `@astrojs/rss`
- [x] `robots.txt` with sitemap reference
- [x] Semantic HTML, single `<h1>` per page, correct heading hierarchy, alt text
- [x] Clean trailing-slash URLs

**Performance**
- [x] Images via `astro:assets` `<Image>` (responsive webp, lazy below the fold)
- [x] Fonts self-hosted + preloaded, `font-display: swap`
- [x] GSAP loaded only in islands; static pages ship ~zero JS
- [x] Local Core Web Vitals: LCP ~80ms, CLS 0

**Accessibility**
- [x] Keyboard nav + visible focus rings (`--color-focus-ring`)
- [x] Contrast checked against the locked palette (accent-deep for small accent text)
- [x] `prefers-reduced-motion` honored across all motion
- [x] Reveal content visible without JS (no-JS / crawler safe)
- [x] Skip-to-content link, `aria-current`, labeled controls

**Quality**
- [x] `npx impeccable detect src` clean (no AI-slop anti-patterns)
- [x] Zero em-dashes in rendered output (taste-skill hard rule)
