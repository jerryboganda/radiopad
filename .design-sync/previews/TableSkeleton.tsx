import { TableSkeleton } from '@radiopad/frontend';

// Canonical worklist loading — the default 6 rows x 5 columns
// (accession, patient, modality, status, assigned radiologist).
export const WorklistLoading = () => (
  <div style={{ maxWidth: 680 }}>
    <TableSkeleton />
  </div>
);

// Compact panel — 3 rows x 3 columns, e.g. recent validation runs in a side panel.
export const CompactPanel = () => (
  <div style={{ maxWidth: 420 }}>
    <TableSkeleton rows={3} cols={3} />
  </div>
);

// Wide audit table — 8 rows x 7 columns while the audit log page fetches.
export const AuditLogLoading = () => (
  <div style={{ maxWidth: 820 }}>
    <TableSkeleton rows={8} cols={7} />
  </div>
);
