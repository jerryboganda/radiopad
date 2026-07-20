import { StatusBadge } from '@radiopad/frontend';

// Report lifecycle — the canonical StatusBadge use in the worklist and
// report header (draft → validated → acknowledged → exported).
export const ReportLifecycle = () => (
  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
    <StatusBadge tone="neutral">Draft</StatusBadge>
    <StatusBadge tone="info">Validated</StatusBadge>
    <StatusBadge tone="success">Acknowledged</StatusBadge>
    <StatusBadge tone="success">Exported</StatusBadge>
  </div>
);

// Full tone sweep — every semantic tone with a realistic label.
export const AllTones = () => (
  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
    <StatusBadge tone="neutral">Queued</StatusBadge>
    <StatusBadge tone="info">In progress</StatusBadge>
    <StatusBadge tone="success">Signed</StatusBadge>
    <StatusBadge tone="warning">Pending review</StatusBadge>
    <StatusBadge tone="danger">Critical result</StatusBadge>
    <StatusBadge tone="ai">AI drafted</StatusBadge>
  </div>
);
