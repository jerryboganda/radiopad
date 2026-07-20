import { AnimatedNumber } from '@radiopad/frontend';

// Canonical use — validation metric cards, as on the desktop Validation page.
export const ValidationMetrics = () => (
  <div style={{ display: 'flex', gap: 12, maxWidth: 640 }}>
    <div className="metric-card" data-tone="info" style={{ flex: 1 }}>
      <div className="metric-card-value"><AnimatedNumber value={128} /></div>
      <div className="metric-card-label">Reports validated</div>
    </div>
    <div className="metric-card" data-tone="blocked" style={{ flex: 1 }}>
      <div className="metric-card-value"><AnimatedNumber value={3} /></div>
      <div className="metric-card-label">Blockers</div>
    </div>
    <div className="metric-card" data-tone="review" style={{ flex: 1 }}>
      <div className="metric-card-value"><AnimatedNumber value={17} /></div>
      <div className="metric-card-label">Warnings</div>
    </div>
  </div>
);

// Custom format — thousands separator for monthly AI request volume.
export const ThousandsFormat = () => (
  <p style={{ margin: 0, fontSize: 15 }}>
    AI requests this month:{' '}
    <strong><AnimatedNumber value={24318} format={(n) => Math.round(n).toLocaleString('en-US')} /></strong>
  </p>
);

// Fixed decimals — mean turnaround time in hours on the quality analytics page.
export const DecimalTurnaround = () => (
  <p style={{ margin: 0, fontSize: 15 }}>
    Mean report turnaround:{' '}
    <strong><AnimatedNumber value={2.4} decimals={1} /> h</strong>{' '}
    (target ≤ <AnimatedNumber value={4.0} decimals={1} /> h)
  </p>
);
