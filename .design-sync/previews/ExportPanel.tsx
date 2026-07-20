import { ExportPanel } from '@radiopad/frontend';

// RC-09 export panel — worklist-right-rail width. Destinations + format
// radios + validation gate banner + data-boundary notice + Export action.

const noop = () => {};
const exportOk = async () => {};

// Acknowledged report, validation clean (warnings reviewed) — everything
// green, Export enabled.
export const ValidatedClean = () => (
  <div style={{ maxWidth: 400 }}>
    <ExportPanel
      canExport
      exportAllowed
      validated
      blockers={0}
      warnings={2}
      onOpenValidation={noop}
      onExport={exportOk}
    />
  </div>
);

// Validation left blockers open — danger gate banner, destinations + Export
// disabled.
export const BlockedByValidation = () => (
  <div style={{ maxWidth: 400 }}>
    <ExportPanel
      canExport
      exportAllowed
      validated
      blockers={2}
      warnings={1}
      onOpenValidation={noop}
      onExport={exportOk}
    />
  </div>
);

// Status gate: report not yet acknowledged (and validation not run) —
// info banner + amber blocked-reason banner.
export const AwaitingAcknowledgement = () => (
  <div style={{ maxWidth: 400 }}>
    <ExportPanel
      canExport
      exportAllowed={false}
      exportBlockedReason="Acknowledge the report before exporting — AI text in Findings is still unreviewed."
      validated={false}
      blockers={0}
      warnings={0}
      onOpenValidation={noop}
      onExport={exportOk}
    />
  </div>
);
