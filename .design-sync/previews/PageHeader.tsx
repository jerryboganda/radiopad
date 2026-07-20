import { PageHeader } from '@radiopad/frontend';

const PlusIcon = () => (
  <svg width={14} height={14} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
    <path d="M12 5v14M5 12h14" />
  </svg>
);

const FilterIcon = () => (
  <svg width={14} height={14} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
    <path d="M22 3H2l8 9.46V19l4 2v-8.54z" />
  </svg>
);

// Full header — title, subtitle, secondary (ghost) + primary actions,
// as on the worklist page.
export const WorklistHeader = () => (
  <div style={{ maxWidth: 860 }}>
    <PageHeader
      title="Worklist"
      description="Studies assigned to you across Meridian Imaging — 6 awaiting a report."
      primaryAction={
        <button className="primary" type="button" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <PlusIcon /> New report
        </button>
      }
      secondaryActions={
        <button className="ghost" type="button" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <FilterIcon /> Filters
        </button>
      }
    />
  </div>
);

// Text-only header — title + description, no actions row (settings pages).
export const SettingsHeader = () => (
  <div style={{ maxWidth: 860 }}>
    <PageHeader
      title="Dictation settings"
      description="Microphone, wake word, and on-device speech model preferences for this workstation."
    />
  </div>
);

// Rich title node — count badge alongside the title, two-action row
// (template library authoring page).
export const LibraryHeader = () => (
  <div style={{ maxWidth: 860 }}>
    <PageHeader
      title={
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
          Template library
          <span className="badge info">12 active</span>
        </span>
      }
      description="Structured report templates shared with everyone in your tenant."
      primaryAction={<button className="primary" type="button">New template</button>}
      secondaryActions={<button className="subtle" type="button">Import JSON</button>}
    />
  </div>
);
