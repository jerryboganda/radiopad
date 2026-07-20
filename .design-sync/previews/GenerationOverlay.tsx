import { GenerationOverlay } from '@radiopad/frontend';

// The overlay is position:fixed inset:0 — under the capture's translateZ(0)
// containment it fills the story cell, so give it a real intake surface to
// blur behind the centered card (as it does over the wizard in the product).
function IntakeBackdrop() {
  return (
    <div
      style={{
        minHeight: 540,
        padding: 20,
        background: 'var(--bg)',
      }}
    >
      <div
        style={{
          border: '1px solid var(--border)',
          borderRadius: 10,
          background: 'var(--bg-panel)',
          padding: 18,
          maxWidth: 620,
        }}
      >
        <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-strong)' }}>
          New report — CT Chest with contrast
        </div>
        <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--text-muted)' }}>
          Rana Iqbal · 58 F · Acc CT-2094-1187 · Indication: suspected pulmonary embolism.
        </p>
        <p style={{ margin: '6px 0 0', fontSize: 13, color: 'var(--text-muted)' }}>
          Template: CT Chest — PE protocol · Rulebook: chest_ct_v1
        </p>
      </div>
    </div>
  );
}

// PRIMARY — the create → generate pipeline in flight: staged progress list,
// pulsing orb, progress bar, and the real elapsed timer. Timers run during
// capture, so an early stage showing as active is the honest state.
export const DraftInFlight = () => (
  <>
    <IntakeBackdrop />
    <GenerationOverlay active done={false} providerName="MedGemma (on-device)" />
  </>
);

// Failure state — the pipeline died mid-generate; the radiologist gets an
// inline error with Retry / Back instead of being stranded on a spinner.
export const ProviderTimedOut = () => (
  <>
    <IntakeBackdrop />
    <GenerationOverlay
      active
      done={false}
      providerName="MedGemma (on-device)"
      error="Provider timed out after 60s — the draft was not created."
      onRetry={() => {}}
      onBack={() => {}}
    />
  </>
);
