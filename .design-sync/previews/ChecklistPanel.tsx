import { ChecklistPanel } from '@radiopad/frontend';

// RC-01/02 review checklist — worklist-right-rail panel. Items derive entirely
// from the report literal + flags, so each story is a realistic report state.

const study = {
  accessionNumber: 'ACC-2049183',
  modality: 'CT',
  bodyPart: 'Chest',
  contrast: 'With',
  age: 62,
  gender: 'Male',
  comparison: 'CT chest 14 Jan 2026',
};

const draftReport = {
  id: 'rep_31f7c2',
  tenantId: 'tn_mercy',
  status: 'Draft',
  rulebookId: 'chest_ct_v1',
  templateId: 'ct-chest-contrast',
  rulebookPinned: false,
  templatePinned: false,
  study,
  indication: '62-year-old male with persistent cough and 8 kg weight loss over 3 months.',
  technique: 'Contrast-enhanced helical CT of the chest with 1.25 mm reconstructions.',
  comparison: 'CT chest dated 14 Jan 2026.',
  findings:
    'Spiculated 9 mm nodule in the right upper lobe. No pleural effusion or pneumothorax. Mediastinal nodes not enlarged.',
  impression: '',
  recommendations: '',
  aiHighlightsJson: '[]',
  updatedAt: '2026-07-20T18:42:00Z',
};

const completeReport = {
  ...draftReport,
  status: 'Validated',
  impression:
    '1. Spiculated right upper lobe nodule, 9 mm — suspicious for primary lung malignancy.\n2. No mediastinal lymphadenopathy.',
  recommendations: 'PET-CT and tissue sampling per lung nodule pathway.',
};

// Mid-draft: impression still empty, AI text unreviewed → partial ring,
// amber "requires review" row, Acknowledge button.
export const DraftWithAiText = () => (
  <div style={{ maxWidth: 380 }}>
    <ChecklistPanel
      report={draftReport}
      hasAiText
      validated={false}
      blockers={0}
      primarySigned={false}
      canEdit
      onAcknowledge={() => {}}
    />
  </div>
);

// All sections present, AI text acknowledged, validation clean — only
// "Report finalized" left todo.
export const ReadyToSign = () => (
  <div style={{ maxWidth: 380 }}>
    <ChecklistPanel
      report={completeReport}
      hasAiText={false}
      validated
      blockers={0}
      primarySigned={false}
      canEdit
      onAcknowledge={() => {}}
    />
  </div>
);

// Validation ran and left blockers open — amber pending row on the
// validation item.
export const ValidationBlockersOpen = () => (
  <div style={{ maxWidth: 380 }}>
    <ChecklistPanel
      report={completeReport}
      hasAiText={false}
      validated
      blockers={3}
      primarySigned={false}
      canEdit
      onAcknowledge={() => {}}
    />
  </div>
);
