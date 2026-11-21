// Content for /solutions/<slug>/ (rendered by src/pages/solutions/[slug].astro).
// Facts only from README, PRD, CHANGELOG and rulebooks; numbers are product-intrinsic.

type Cap = { icon: string; title: string; text: string; href: string; more: string };
type Role = { label: string; items: string[]; href: string; more: string };
type Demo =
  | { kind: "roles"; eyebrow: string; title: string; sub: string; roles: Role[] }
  | { kind: "rules"; eyebrow: string; title: string; sub: string; toggles: boolean };

export type Solution = {
  slug: string;
  crumb: string;
  meta: string;
  description: string;
  primary: "demo" | "download";
  hero: { eyebrow: string; lead: string; title: string; text: string };
  change: { title: string; sub: string; rows: { label: string; before: string; after: string }[] };
  caps: Cap[];
  demo: Demo;
  faqTitle: string;
  faq: { q: string; a: string }[];
  cta: { lead: string; title: string; text: string };
};

// Illustrative chest CT draft checked against three rules from rulebooks/chest_ct_v1.yaml.
// Each <mark data-m> is the text a rule flags; `to` is the corrected wording.
export const chestDemo = {
  findings: "6 mm solid nodule in the right lower lobe. No pleural effusion. No mediastinal lymphadenopathy.",
  impression:
    '<mark data-m="meas">8 mm</mark> solid nodule, <mark data-m="lat">left</mark> lower lobe. <mark data-m="fu">Suggest repeat CT in 3 months.</mark>',
  rules: [
    { id: "lat", rule: "laterality_consistency", sev: "blocker", text: "Findings say right lower lobe. Impression says left.", to: "right" },
    { id: "meas", rule: "measurement_consistency", sev: "warning", text: "The nodule is 6 mm in Findings and 8 mm in Impression.", to: "6 mm" },
    { id: "fu", rule: "unauthorized_followup", sev: "warning", text: "Follow-up wording is not on the approved list.", to: "Recommend follow-up imaging as clinically indicated." },
  ],
};

export const solutions: Solution[] = [
  {
    slug: "hospitals",
    crumb: "Hospitals and health systems",
    meta: "RadioPad for hospitals and health systems",
    description:
      "One reporting standard across readers and sites: institution rulebooks, SSO and MFA, an append-only audit log, tenant isolation, and on-prem or private cloud deployment with local models.",
    primary: "demo",
    hero: {
      eyebrow: "For hospitals and health systems",
      lead: "Many readers. Many sites.",
      title: "One reporting standard.",
      text: "RadioPad drafts every report against your institution's rulebooks and templates, keeps AI text marked until a radiologist reviews it, and writes every AI call to an append-only audit log. Run it inside your network, with a local model if you prefer.",
    },
    change: {
      title: "What changes for your department.",
      sub: "The same rules apply to every reader, every site and every draft, and the record shows how each report was made.",
      rows: [
        { label: "Report structure", before: "Each reader and site formats reports their own way.", after: "Templates and required sections apply to every reader in the tenant." },
        { label: "Local standards", before: "Reporting standards live in documents readers check from memory.", after: "The rulebooks your department writes are checked on every draft, before signature." },
        { label: "Critical results", before: "Communication to the clinician is documented when someone remembers.", after: "A critical finding holds the report until communication, with read-back, is documented." },
        { label: "AI use", before: "AI output is hard to see and harder to audit.", after: "Every AI call is recorded with the provider, model, prompt version and whether the request contained PHI." },
        { label: "Sign-in", before: "Another set of credentials for every new tool.", after: "Readers sign in through your identity provider over OIDC, with MFA." },
      ],
    },
    demo: {
      kind: "roles",
      eyebrow: "Roles",
      title: "One platform, four seats at the table.",
      sub: "Pick a role to see what it works with day to day and what it controls.",
      roles: [
        {
          label: "Radiologists",
          items: [
            "Dictate on the Windows workstation or from a paired phone",
            "A draft built from dictation, priors, measurements and study metadata",
            "Blockers and warnings shown before signature",
            "Only a radiologist signs, and AI text stays marked until reviewed",
          ],
          href: "/product/",
          more: "How reporting works",
        },
        {
          label: "Department leads",
          items: [
            "Write and version rulebooks per modality and body part",
            "Approve per-institution prompt overrides before they reach the AI",
            "Structured peer review with scored discrepancies",
            "Critical results documented before sign-off",
          ],
          href: "/product/validation/",
          more: "The validation engine",
        },
        {
          label: "IT and security",
          items: [
            "SSO over OIDC, with multi-factor authentication",
            "Tenant isolation for data, rulebooks and prompts",
            "On-prem or private cloud, with a local model through Ollama or vLLM",
            "An append-only audit log you can verify offline with the CLI",
          ],
          href: "/security/",
          more: "Security and governance",
        },
        {
          label: "Integration teams",
          items: [
            "PACS/RIS bridge in the Windows app",
            "DICOMweb study lookup over QIDO-RS and WADO-RS",
            "Orthanc bridge for HL7 v2 and DICOM SR",
            "Export as FHIR R4 DiagnosticReport, PDF, DOCX or plain text",
          ],
          href: "/integrations/",
          more: "Integrations",
        },
      ],
    },
    caps: [
      { icon: "shield", title: "Institution rulebooks", text: "Laterality, required sections, critical results, measurement consistency, negation conflicts and approved follow-up phrasing, checked on every draft.", href: "/product/validation/", more: "Validation engine" },
      { icon: "layout", title: "Templates and layouts", text: "Per modality and per institution, across CT, MR, XR, US, MG, NM, PET and fluoroscopy.", href: "/product/", more: "Product overview" },
      { icon: "lock", title: "SSO, MFA and audit", text: "Sign-in through your identity provider, tenant isolation, and an append-only record of every AI call.", href: "/security/", more: "Security" },
      { icon: "server", title: "On-prem or private cloud", text: "Deploy inside your network and keep drafting on a local model, so clinical text stays on your hardware.", href: "/security/", more: "Deployment options" },
      { icon: "globe", title: "PACS/RIS and FHIR", text: "Study context over DICOMweb, an Orthanc bridge, and signed reports out as FHIR R4 DiagnosticReport.", href: "/integrations/", more: "Integrations" },
      { icon: "pulse", title: "AI oversight", text: "Drift monitoring of AI output and prompt overrides that a medical director approves per institution.", href: "/security/#ai", more: "AI oversight" },
    ],
    faqTitle: "Questions health systems ask first.",
    faq: [
      { q: "Can every site work to one rulebook?", a: "Yes. A rulebook set for the tenant applies to every reader and site. A department that reports differently can carry its own rulebook on top of it." },
      { q: "Can RadioPad run inside our network?", a: "Yes. Deploy on-prem or in a private cloud, and run drafting on a local model through Ollama or vLLM so clinical text stays on your hardware." },
      { q: "How do our radiologists sign in?", a: "Through your identity provider over OIDC, with multi-factor authentication." },
      { q: "What does the audit log record?", a: "Every AI call, with the provider, model, prompt version and rulebook version, and whether the request contained PHI. Entries are append-only and hash-chained, and the CLI can verify the chain offline." },
      { q: "Does the AI ever sign a report?", a: "No. Signing is a radiologist action. AI-drafted text stays marked until it has been reviewed or edited." },
      { q: "How is it priced for a health system?", a: "Enterprise terms are custom. Book a demo and we will scope it with you." },
    ],
    cta: { lead: "Bring your rulebooks.", title: "See them run on your studies.", text: "Book a walkthrough for your department, with your modalities, templates and identity provider." },
  },
  {
    slug: "imaging-groups",
    crumb: "Imaging groups",
    meta: "RadioPad for imaging groups",
    description:
      "Shared templates and rulebooks per tenant, peer review across sites, critical-results documentation and a web admin console for groups that read for many sites and subspecialties.",
    primary: "demo",
    hero: {
      eyebrow: "For imaging groups",
      lead: "Every site. Every subspecialty.",
      title: "One set of standards.",
      text: "Run shared templates and rulebooks for the whole group, review reads across sites, and document critical results before sign-off. Administrators manage users, rules and AI settings from the web console.",
    },
    change: {
      title: "What changes for your group.",
      sub: "Standards are set once for the tenant, then checked on every draft at every site.",
      rows: [
        { label: "Adding a site", before: "Each new site arrives with its own templates and habits.", after: "New sites start on the group's templates and rulebooks." },
        { label: "Subspecialty reads", before: "Readers keep private macros and phrasing.", after: "A shared findings library gives every reader the same approved phrasing." },
        { label: "Peer review", before: "Second reads are ad hoc and hard to compare.", after: "Structured second reads with scored discrepancies, across sites." },
        { label: "Critical results", before: "Callbacks are logged somewhere else, if at all.", after: "The report holds until communication to the clinician, with read-back, is documented." },
        { label: "Administration", before: "Changing a rule means messaging every site.", after: "Change users, roles, rulebooks, templates and AI settings once in the web console." },
      ],
    },
    demo: {
      kind: "rules",
      eyebrow: "Rule impact",
      title: "Turn a rule on once. Every site is checked.",
      sub: "An illustrative chest CT draft against three rules from chest_ct_v1. Switch a rule off to see what slips through, or fix a flag the way a radiologist would.",
      toggles: true,
    },
    caps: [
      { icon: "layout", title: "Shared templates", text: "Per modality and per institution, so every site reports in the same structure.", href: "/product/", more: "Templates and layouts" },
      { icon: "shield", title: "Rulebooks per tenant", text: "Written by your group, versioned, and checked before every signature.", href: "/product/validation/", more: "Validation engine" },
      { icon: "users", title: "Peer review across sites", text: "Structured second reads with scored discrepancies for your quality program.", href: "/product/", more: "Platform modules" },
      { icon: "alert", title: "Critical results", text: "A critical finding is a blocker until communication to the clinician is documented.", href: "/product/validation/", more: "Critical-results rules" },
      { icon: "monitor", title: "Web admin console", text: "Users, roles, rulebooks, templates and AI settings. Reporting stays on the Windows workstation.", href: "/product/", more: "Product overview" },
      { icon: "grid", title: "Team plan", text: "Priced per seat for groups. Talk to us about your sites and readers.", href: "/pricing/", more: "Pricing" },
    ],
    faqTitle: "Questions imaging groups ask first.",
    faq: [
      { q: "Can a site or subspecialty keep its own rules?", a: "Yes. The group's rulebook applies across the tenant, and a department can carry its own rulebook where it reports differently." },
      { q: "Is there a web version for reporting?", a: "No. Reporting runs in the Windows desktop app. The web console is for administrators: users, roles, rulebooks, templates and AI settings." },
      { q: "How does peer review work?", a: "Peer review records structured second reads and scores discrepancies, so your quality team can compare reads across sites." },
      { q: "How are critical results handled?", a: "Critical-result language is a blocker in the rulebook. The report is held until communication to the responsible clinician is documented with read-back, naming the recipient and the time." },
      { q: "Can we choose our AI provider?", a: "Yes. Use Anthropic Claude, Azure OpenAI, or a local model through Ollama or vLLM. Every call is written to the append-only audit log." },
      { q: "How is pricing structured?", a: "The Team plan is priced per seat. Talk to us for a quote that fits your sites and readers." },
    ],
    cta: { lead: "Bring your group.", title: "Set the standard once.", text: "Book a demo to see shared templates, rulebooks and peer review on your modalities." },
  },
  {
    slug: "radiologists",
    crumb: "Radiologists",
    meta: "RadioPad for radiologists",
    description:
      "A Windows reporting workstation with hotkeys, on-device dictation, your phone as a microphone, templates and a findings library, and blockers caught before you sign. Start a free trial.",
    primary: "download",
    hero: {
      eyebrow: "For radiologists",
      lead: "Keep your eyes on the images.",
      title: "Let the draft come to you.",
      text: "RadioPad runs on your Windows workstation. Dictate on the device or through your phone, get a structured draft in your template, and see every blocker before you sign. You review. You sign.",
    },
    change: {
      title: "A day in the reading room, before and after.",
      sub: "The parts of reporting that are not reading images get shorter.",
      rows: [
        { label: "Opening a study", before: "Copy the accession, open a template, retype the history.", after: "Study metadata and prior reports feed the draft." },
        { label: "Dictating", before: "Dictation is tied to one microphone at one desk.", after: "Use the on-device engine, or pair your phone as a microphone." },
        { label: "Formatting", before: "Hand-edit every report into the department's structure.", after: "The draft lands in your template, with AI text marked until you review it." },
        { label: "Checking", before: "A left and right swap comes back from the referrer after sign-off.", after: "Laterality, measurement and negation conflicts are flagged before you sign." },
        { label: "Signing", before: "Sign and hope nothing slipped.", after: "Blockers hold the report. You sign when it reads right." },
      ],
    },
    demo: {
      kind: "rules",
      eyebrow: "Try it",
      title: "Catch it before you sign.",
      sub: "An illustrative chest CT draft with one blocker and two warnings. Fix each flag, then sign.",
      toggles: false,
    },
    caps: [
      { icon: "win", title: "Windows workstation", text: "A native .msi that updates itself, with hotkeys, a secure clipboard and a PACS/RIS bridge.", href: "/product/", more: "Product overview" },
      { icon: "mic", title: "On-device dictation", text: "A local dictation engine that runs on the workstation.", href: "/product/dictation/", more: "Dictation" },
      { icon: "phone", title: "Your phone as a mic", text: "Pair the Android or iOS companion with a short code or QR and dictate into the report open on your desktop.", href: "/product/dictation/", more: "Phone companion" },
      { icon: "book", title: "Templates and findings library", text: "Per-modality templates and reusable, searchable phrasing.", href: "/product/", more: "Templates and library" },
      { icon: "shield", title: "Blockers before you sign", text: "Laterality, required sections, critical results and measurement consistency, flagged on the draft.", href: "/product/validation/", more: "Validation engine" },
      { icon: "user", title: "Free trial", text: "Start on the Radiologist plan on your own workstation.", href: "/pricing/", more: "Plans" },
    ],
    faqTitle: "Questions radiologists ask first.",
    faq: [
      { q: "Which operating systems does it run on?", a: "Windows. The desktop app ships as an .msi and updates itself. macOS and Linux desktop builds are not offered." },
      { q: "Can I dictate from my phone?", a: "Yes. Pair the companion app for Android or iOS with your desktop session using a short code or QR, and your voice streams into the report open on the desktop. Editing and signing stay on the workstation." },
      { q: "Does dictation need the cloud?", a: "The desktop app includes an on-device dictation engine. The phone companion streams your voice to the desktop through a cloud relay." },
      { q: "Will RadioPad sign for me?", a: "No. Only a radiologist can sign. AI-drafted text stays marked until you review or edit it." },
      { q: "Can I keep my own phrasing?", a: "Yes. Templates are set per modality, and the findings library keeps reusable phrasing searchable." },
      { q: "How do I start?", a: "Download the Windows app and start a free trial on the Radiologist plan." },
    ],
    cta: { lead: "Read the next study.", title: "With the draft already there.", text: "Download RadioPad for Windows and start a free trial on your own workstation, or book a demo for your department." },
  },
];
