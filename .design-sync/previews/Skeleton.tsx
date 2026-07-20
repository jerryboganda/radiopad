import { Skeleton } from '@radiopad/frontend';

// Variant sweep — text, row, and block placeholders at typical widths.
export const VariantSweep = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 520 }}>
    <div>
      <p style={{ margin: '0 0 4px', fontSize: 12, color: 'var(--color-ink-mute)' }}>text</p>
      <Skeleton variant="text" width="60%" />
    </div>
    <div>
      <p style={{ margin: '0 0 4px', fontSize: 12, color: 'var(--color-ink-mute)' }}>row</p>
      <Skeleton variant="row" width="100%" />
    </div>
    <div>
      <p style={{ margin: '0 0 4px', fontSize: 12, color: 'var(--color-ink-mute)' }}>block</p>
      <Skeleton variant="block" width="100%" />
    </div>
  </div>
);

// Report section loading — heading line, three body lines, thumbnail block,
// the shape used while a Findings section streams in from the AI gateway.
export const SectionLoading = () => (
  <div style={{ maxWidth: 520 }}>
    <Skeleton variant="text" width="30%" height={14} />
    <Skeleton variant="text" width="95%" />
    <Skeleton variant="text" width="88%" />
    <Skeleton variant="text" width="72%" />
    <div style={{ marginTop: 12 }}>
      <Skeleton variant="block" width={180} height={120} />
    </div>
  </div>
);

// Custom sizing — explicit width/height for a key-image strip placeholder.
export const KeyImageStrip = () => (
  <div style={{ display: 'flex', gap: 8 }}>
    <Skeleton variant="block" width={96} height={72} />
    <Skeleton variant="block" width={96} height={72} />
    <Skeleton variant="block" width={96} height={72} />
    <Skeleton variant="block" width={96} height={72} />
  </div>
);
