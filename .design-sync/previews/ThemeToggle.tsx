import { ThemeToggle } from '@radiopad/frontend';

// The control by itself — light resting state (sun glyph + pill switch).
// Dark cannot be forced via props (the switch reads the resolved app theme
// from localStorage/system), so the default state is shown.
export const Default = () => (
  <div style={{ padding: 8 }}>
    <ThemeToggle />
  </div>
);

// Where it actually lives: the right-hand utility cluster of the RC topbar,
// beside tenant and status chrome.
export const InTopbarCluster = () => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      maxWidth: 560,
      padding: '10px 16px',
      border: '1px solid var(--border)',
      borderRadius: 10,
      background: 'var(--bg-panel)',
    }}
  >
    <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-strong)' }}>RadioPad</span>
    <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Aga Khan Radiology</span>
    <span style={{ flex: 1 }} />
    <span className="badge ok">PACS linked</span>
    <ThemeToggle />
  </div>
);
