import { PatientContextBar } from '@radiopad/frontend';

// RC-01 composer strip — full identity: back button, avatar, patient +
// status chip, accession, demographics, procedure/indication/priors
// segments, saved state, Export.
export const ChestCtDraft = () => (
  <div style={{ maxWidth: 860 }}>
    <PatientContextBar
      title="Amina Yusuf"
      chips={<span className="badge info">Draft</span>}
      accession="ACC-8867"
      meta="58Y · F · MRN 004821"
      segments={[
        { label: 'Procedure', value: 'CT Chest w/o contrast' },
        { label: 'Priors', value: '2 priors', onClick: () => {} },
      ]}
      saveState="saved"
      savedLabel="Saved 2 min ago"
      onBack={() => {}}
      onExport={() => {}}
    />
  </div>
);

// AI-drafted report awaiting review — amber "Requires review" chip,
// autosaving spinner, Export disabled until the draft is acknowledged.
export const AiDraftRequiresReview = () => (
  <div style={{ maxWidth: 860 }}>
    <PatientContextBar
      title="Daniel Okafor"
      chips={<span className="badge ai">AI drafted</span>}
      accession="ACC-9012"
      meta="41Y · M · MRN 007310"
      segments={[{ label: 'Procedure', value: 'MRI Brain w/ contrast' }]}
      requiresReview
      saveState="saving"
      onBack={() => {}}
      onExport={() => {}}
      exportDisabled
      exportTitle="Acknowledge the AI draft before exporting"
    />
  </div>
);

// Offline / sync failure — amber "Not synced" state with the Retry sync
// pill next to Export.
export const SyncError = () => (
  <div style={{ maxWidth: 860 }}>
    <PatientContextBar
      title="Priya Raman"
      chips={<span className="badge warn">Unsigned</span>}
      accession="ACC-9155"
      meta="66Y · F · MRN 001954"
      segments={[{ label: 'Procedure', value: 'US Abdomen complete' }]}
      saveState="error"
      onRetrySync={() => {}}
      onBack={() => {}}
      onExport={() => {}}
    />
  </div>
);
