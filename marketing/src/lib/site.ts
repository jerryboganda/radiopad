// Central site config. Update `url` to the real production origin before deploy
// (it also lives in astro.config.mjs `site` for canonical/sitemap/RSS).

export const site = {
  name: "RadioPad",
  description:
    "AI-assisted radiology reporting. RadioPad drafts and validates; the radiologist reviews and signs. The AI never signs.",
  url: "https://radiopadstudio.com",
  email: "hello@radiopad.example",
  tagline:
    "AI-assisted radiology reporting. The radiologist stays the final authority.",
  nav: [
    { label: "Product", href: "/#features" },
    { label: "How it works", href: "/#how" },
    { label: "Security", href: "/#security" },
    { label: "Pricing", href: "/#pricing" },
    { label: "Blog", href: "/blog" },
  ],
  cta: { label: "Book a demo", href: "/#demo" },
  footer: {
    Product: [
      { label: "Features", href: "/#features" },
      { label: "How it works", href: "/#how" },
      { label: "Security", href: "/#security" },
      { label: "Pricing", href: "/#pricing" },
    ],
    Company: [
      { label: "About", href: "/#" },
      { label: "Blog", href: "/blog" },
      { label: "Careers", href: "/#" },
      { label: "Contact", href: "/#demo" },
    ],
    Resources: [
      { label: "Documentation", href: "/#" },
      { label: "Changelog", href: "/#" },
      { label: "Status", href: "/#" },
      { label: "RSS", href: "/rss.xml" },
    ],
    Legal: [
      { label: "Privacy", href: "/#" },
      { label: "Terms", href: "/#" },
      { label: "Data processing", href: "/#" },
    ],
  },
} as const;

export const BRAND_SVG = `<svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.4" stroke-linecap="round"><path d="M4 12h3l2 5 4-12 2 7h5"/></svg>`;
