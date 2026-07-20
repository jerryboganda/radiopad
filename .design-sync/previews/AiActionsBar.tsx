import { AiActionsBar } from '@radiopad/frontend';

// RC-06 AI actions bar — the AI verb row above the section cards (editor
// width, not a rail panel). rewriteOpen is a controlled prop, so the rewrite
// popover renders statically for capture.

const rewriteModes = [
  { mode: 'concise', label: 'Concise', hint: 'Shorter, denser prose' },
  { mode: 'formal', label: 'Formal', hint: 'Strict radiology register' },
  { mode: 'patient_friendly', label: 'Patient-friendly', hint: 'Plain-language summary for the patient' },
  { mode: 'referring_summary', label: 'Referring summary', hint: 'Brief note for the referring clinician' },
];

const sections = [
  { key: 'findings', label: 'Findings' },
  { key: 'impression', label: 'Impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

const base = {
  canEdit: true,
  busyAction: null,
  onGenerateDraft: () => {},
  onGenerateImpression: () => {},
  rewriteModes,
  sections,
  rewriteSection: 'impression',
  onRewriteSectionChange: () => {},
  onRewrite: () => {},
  rewriteBusy: false,
  rewriteOpen: false,
  onRewriteOpenChange: () => {},
  stylePanelOpen: false,
  onToggleStylePanel: () => {},
  providerId: 'prov_claude',
};

// Idle toolbar — all verbs enabled, scope chip mirrors the rewrite target.
export const Idle = () => (
  <div style={{ maxWidth: 760 }}>
    <AiActionsBar {...base} />
  </div>
);

// Controlled rewrite popover open — section select, four rewrite modes with
// hints, and the F12 custom-edit textarea. minHeight gives the absolute
// popover room below the bar.
export const RewritePopoverOpen = () => (
  <div style={{ maxWidth: 760, minHeight: 600 }}>
    <AiActionsBar {...base} rewriteOpen />
  </div>
);

// Draft generation in flight — spinner in the primary button, every other
// verb disabled.
export const GeneratingDraft = () => (
  <div style={{ maxWidth: 760 }}>
    <AiActionsBar {...base} busyAction="draft" />
  </div>
);
