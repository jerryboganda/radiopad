// Central site config: origin, contact, navigation and footer.
// `url` also lives in astro.config.mjs `site` (canonical URLs, sitemap, RSS).

export const site = {
  name: "RadioPad",
  description:
    "AI-assisted radiology reporting. RadioPad drafts and validates; the radiologist reviews and signs. The AI never signs.",
  url: "https://radiopadstudio.com",
  // ponytail: placeholder mailbox; replace with a real, monitored address before launch.
  email: "hello@radiopadstudio.com",
  securityEmail: "security@radiopadstudio.com",
  signIn: "https://admin.radiopadstudio.com/login",
  releases: "https://github.com/jerryboganda/radiopad/releases/latest",
  releasesApi: "https://api.github.com/repos/jerryboganda/radiopad/releases/latest",
};

export type MegaItem = { label: string; desc: string; href: string; icon: string };
export type NavEntry =
  | { label: string; href: string }
  | { label: string; id: string; items: MegaItem[]; feature?: { title: string; desc: string; cta: string; href: string }; small?: boolean };

export const nav: NavEntry[] = [
  {
    label: "Product",
    id: "mega-product",
    items: [
      { label: "Product overview", desc: "Dictate, draft, validate, sign", href: "/product/", icon: "doc" },
      { label: "Dictation", desc: "Local engine and phone companion", href: "/product/dictation/", icon: "mic" },
      { label: "Validation engine", desc: "Rulebooks your department writes", href: "/product/validation/", icon: "shield" },
      { label: "Templates and layouts", desc: "Per modality, per institution", href: "/product/#platform", icon: "layout" },
      { label: "Findings library", desc: "Reusable, searchable phrasing", href: "/product/#platform", icon: "book" },
      { label: "Critical results", desc: "Documented before sign-off", href: "/product/#platform", icon: "alert" },
      { label: "Peer review", desc: "Second reads, scored discrepancies", href: "/product/#platform", icon: "users" },
      { label: "Marketplace", desc: "Signed plugins and connectors", href: "/product/#platform", icon: "grid" },
    ],
    feature: { title: "RadioPad for Windows", desc: "The full reporting workstation, with hotkeys and a PACS/RIS bridge.", cta: "Download", href: "/download/" },
  },
  {
    label: "Solutions",
    id: "mega-sol",
    items: [
      { label: "Hospitals and health systems", desc: "Rulebooks, SSO and audit at scale", href: "/solutions/hospitals/", icon: "building" },
      { label: "Imaging groups", desc: "Many sites, one set of standards", href: "/solutions/imaging-groups/", icon: "stack" },
      { label: "Radiologists", desc: "Faster drafts on your own workstation", href: "/solutions/radiologists/", icon: "user" },
      { label: "IT and governance", desc: "On-prem, tenant isolation, audit", href: "/security/", icon: "lock" },
      { label: "PACS and RIS teams", desc: "DICOMweb, Orthanc, FHIR export", href: "/integrations/", icon: "server" },
      { label: "Quality and safety", desc: "Blockers before signature", href: "/product/validation/", icon: "shield" },
      { label: "AI oversight", desc: "Drift monitoring, prompt overrides", href: "/security/#ai", icon: "pulse" },
      { label: "Peer review programs", desc: "Structured second reads", href: "/product/#platform", icon: "users" },
    ],
    feature: { title: "See it with your rulebooks", desc: "A walkthrough on your modalities and templates.", cta: "Book a demo", href: "/demo/" },
  },
  { label: "Integrations", href: "/integrations/" },
  { label: "Security", href: "/security/" },
  { label: "Pricing", href: "/pricing/" },
  {
    label: "Resources",
    id: "mega-res",
    small: true,
    items: [
      { label: "Blog", desc: "Product and clinical notes", href: "/blog/", icon: "doc" },
      { label: "Changelog", desc: "What shipped, release by release", href: "/changelog/", icon: "pulse" },
      { label: "CLI reference", desc: "Author and batch-validate rulebooks", href: "/product/validation/#cli", icon: "term" },
      { label: "Security overview", desc: "How data and AI calls flow", href: "/security/", icon: "lock" },
      { label: "Download", desc: "Windows app and phone companion", href: "/download/", icon: "win" },
      { label: "About RadioPad", desc: "Why the radiologist signs", href: "/about/", icon: "building" },
    ],
  },
];

export const footer: Record<string, { label: string; href: string }[]> = {
  Product: [
    { label: "Overview", href: "/product/" },
    { label: "Dictation", href: "/product/dictation/" },
    { label: "Validation engine", href: "/product/validation/" },
    { label: "Platform modules", href: "/product/#platform" },
    { label: "Integrations", href: "/integrations/" },
    { label: "Download", href: "/download/" },
  ],
  Solutions: [
    { label: "Hospitals", href: "/solutions/hospitals/" },
    { label: "Imaging groups", href: "/solutions/imaging-groups/" },
    { label: "Radiologists", href: "/solutions/radiologists/" },
    { label: "IT and governance", href: "/security/" },
    { label: "Pricing", href: "/pricing/" },
  ],
  Resources: [
    { label: "Blog", href: "/blog/" },
    { label: "Changelog", href: "/changelog/" },
    { label: "CLI reference", href: "/product/validation/#cli" },
    { label: "Security overview", href: "/security/" },
    { label: "RSS", href: "/rss.xml" },
  ],
  Company: [
    { label: "About", href: "/about/" },
    { label: "Careers", href: "/about/#careers" },
    { label: "Contact", href: "/demo/" },
    { label: "Book a demo", href: "/demo/" },
  ],
  Legal: [
    { label: "Privacy", href: "/legal/privacy/" },
    { label: "Terms", href: "/legal/terms/" },
    { label: "Data processing", href: "/legal/dpa/" },
    { label: "Responsible AI", href: "/legal/responsible-ai/" },
  ],
};

export const BRAND_PATH = "M4 12h3l2 5 4-12 2 7h5";
