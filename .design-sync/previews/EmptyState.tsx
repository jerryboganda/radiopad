import { EmptyState } from '@radiopad/frontend';

// Canonical worklist empty state — default folder icon, description, primary CTA.
export const WorklistEmpty = () => (
  <div style={{ maxWidth: 560 }}>
    <EmptyState
      title="No studies in your worklist"
      description="Studies assigned to you will appear here as soon as they arrive from PACS."
      action={<button className="primary" type="button">Connect PACS source</button>}
    />
  </div>
);

// Filtered-out empty state with a custom magnifier icon and a ghost reset action.
export const NoSearchResults = () => (
  <div style={{ maxWidth: 560 }}>
    <EmptyState
      icon={
        <svg width={18} height={18} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
          <circle cx={11} cy={11} r={7} />
          <path d="m21 21-4.35-4.35" />
        </svg>
      }
      title="No reports match your filters"
      description={'Nothing signed between 12 Jul and 19 Jul for modality "CT". Try widening the date range.'}
      action={<button className="ghost" type="button">Clear filters</button>}
    />
  </div>
);

// Minimal form — title only, as used in a small library panel with no templates yet.
export const TemplatesMinimal = () => (
  <div style={{ maxWidth: 560 }}>
    <EmptyState title="No report templates yet" />
  </div>
);
