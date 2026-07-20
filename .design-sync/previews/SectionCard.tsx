import { SectionCard } from '@radiopad/frontend';

const FindingsIcon = () => (
  <svg width={15} height={15} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
    <path d="M14 2v6h6M16 13H8M16 17H8M10 9H8" />
  </svg>
);

// RC-02/03 — a Findings section holding unreviewed AI text: provenance chips
// ("✨ generated" + "Requires review"), body wearing .ai-mark, Accept/Undo footer.
export const FindingsGenerated = () => (
  <div style={{ maxWidth: 640 }}>
    <SectionCard
      sectionKey="findings"
      title="Findings"
      icon={<FindingsIcon />}
      generated
      menuItems={[
        { label: 'Regenerate section', onClick: () => {} },
        { label: 'Insert template text', onClick: () => {} },
        { label: 'Clear section', onClick: () => {} },
      ]}
      actions={
        <>
          <button className="primary" type="button">Accept</button>
          <button className="ghost" type="button">Undo</button>
        </>
      }
    >
      <div className="ai-mark">
        <p style={{ margin: 0 }}>
          Focal consolidation in the right lower lobe with air bronchograms.
          No pleural effusion or pneumothorax. Heart size within normal limits.
          Degenerative changes of the thoracic spine.
        </p>
      </div>
    </SectionCard>
  </div>
);

// A reviewed, radiologist-authored Impression — no provenance chips, no footer.
export const ImpressionReviewed = () => (
  <div style={{ maxWidth: 640 }}>
    <SectionCard sectionKey="impression" title="Impression" icon={<FindingsIcon />}>
      <ol style={{ margin: 0, paddingLeft: 18 }}>
        <li>Right lower lobe pneumonia.</li>
        <li>No radiographic evidence of malignancy.</li>
        <li>Recommend follow-up radiograph in 6 weeks to confirm resolution.</li>
      </ol>
    </SectionCard>
  </div>
);
