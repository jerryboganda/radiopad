import { ErrorState } from '@radiopad/frontend';

// Canonical data-load failure with retry — the standard worklist error surface.
export const WorklistLoadFailed = () => (
  <div style={{ maxWidth: 560 }}>
    <ErrorState
      title="Couldn't load your worklist"
      message="The RadioPad server didn't respond in time. Check your connection and try again."
      onRetry={() => {}}
    />
  </div>
);

// Defaults only — generic title, no message, no retry (non-recoverable panel error).
export const DefaultsNoRetry = () => (
  <div style={{ maxWidth: 560 }}>
    <ErrorState />
  </div>
);

// Custom retry label — rulebook validation service failure with a domain-specific action.
export const ValidationServiceDown = () => (
  <div style={{ maxWidth: 560 }}>
    <ErrorState
      title="Validation service unavailable"
      message="Rulebook chest_ct_v1 could not be evaluated. The report stays in Draft until validation succeeds."
      onRetry={() => {}}
      retryLabel="Re-run validation"
    />
  </div>
);
