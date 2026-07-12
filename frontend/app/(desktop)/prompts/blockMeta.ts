/**
 * Friendly metadata for prompt-block keys.
 *
 * Prompt blocks are stored under terse snake_case keys (`system`,
 * `findings_to_impression`, …). On their own those keys tell a reviewer
 * nothing about what each block controls, so Prompt Studio pairs every block
 * with a human title and a one-line description of its role. Keys not listed
 * here (custom blocks added by an admin) fall back to a title-cased label and
 * a generic description.
 */

export type BlockMeta = {
  /** Human title shown as the card heading, e.g. "Findings → Impression". */
  title: string;
  /** One sentence on what the block controls, in plain clinical language. */
  description: string;
};

const KNOWN: Record<string, BlockMeta> = {
  system: {
    title: 'System',
    description:
      "Sets the AI's role and the hard rules it must follow when drafting this study type.",
  },
  findings_to_impression: {
    title: 'Findings → Impression',
    description:
      'How the AI condenses the Findings section into a concise, faithful Impression.',
  },
  cleanup: {
    title: 'Cleanup',
    description:
      'Tidies grammar and structure without changing measurements, organ names, or critical-result wording.',
  },
  dictation_cleanup: {
    title: 'Dictation cleanup',
    description:
      'Normalises raw dictation — punctuation, numbers, and headings — before any clinical reasoning.',
  },
  follow_up: {
    title: 'Follow-up',
    description:
      'Governs how recommended follow-up imaging or actions are phrased and bounded.',
  },
  impression: {
    title: 'Impression',
    description: 'Shapes the tone and structure of the generated Impression.',
  },
};

/** Title-case a snake_case key as a fallback label, e.g. `my_block` → "My block". */
function titleCase(key: string): string {
  const spaced = key.replace(/_/g, ' ').trim();
  if (!spaced) return key;
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Resolve friendly metadata for a block key, with a graceful custom fallback. */
export function getBlockMeta(key: string): BlockMeta {
  const known = KNOWN[key.toLowerCase()];
  if (known) return known;
  return { title: titleCase(key), description: 'Custom prompt block.' };
}
