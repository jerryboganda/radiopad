import { CrossCheckBadge } from '@radiopad/frontend';

// The badge is position:fixed bottom-right — under the capture's translateZ(0)
// containment it anchors to the story cell, so each story gets a quiet section
// surface for the badge to float over (as it does over the editor in the product).
function SectionBackdrop({ text }: { text: string }) {
  return (
    <div
      style={{
        minHeight: 260,
        border: '1px solid var(--border)',
        borderRadius: 10,
        background: 'var(--bg-panel)',
        padding: 16,
      }}
    >
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-strong)' }}>
        Findings — dictated
      </div>
      <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--text-muted)', maxWidth: 460 }}>
        {text}
      </p>
    </div>
  );
}

// In flight — spinner + current engine stage; non-blocking, the radiologist
// keeps dictating while the cross-check polls.
export const Running = () => (
  <>
    <SectionBackdrop text="There is a 9 mm nodule in the left upper lobe. No pleural effusion. The right kidney demonstrates a simple cyst." />
    <CrossCheckBadge status="running" stage="Checking laterality…" />
  </>
);

// Finished — success tone with the suggestion count and a dismiss affordance.
export const Completed = () => (
  <>
    <SectionBackdrop text="There is a 9 mm nodule in the left upper lobe. No pleural effusion. The right kidney demonstrates a simple cyst." />
    <CrossCheckBadge status="completed" stage="3 suggestions" onDismiss={() => {}} />
  </>
);

// Failed — danger tone; the run died and says so rather than passing silently.
export const Failed = () => (
  <>
    <SectionBackdrop text="There is a 9 mm nodule in the left upper lobe. No pleural effusion. The right kidney demonstrates a simple cyst." />
    <CrossCheckBadge status="failed" stage="cross-check failed" onDismiss={() => {}} />
  </>
);
