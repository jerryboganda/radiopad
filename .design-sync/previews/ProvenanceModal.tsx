import { ProvenanceModal } from '@radiopad/frontend';

// The modal scrim is position:fixed inset:0 — under the capture's translateZ(0)
// containment it fills the story cell, so give it an editor-ish surface for the
// scrim to darken (as it does over the report editor in the product).
//
// Capture glue: .rp-provenance clamps to min(80vh, 640px). At the 640px capture
// viewport 80vh = 512px, which scroll-clips the bottom chain rows + the "What
// this means" panel — an artifact of the short capture window, not the product
// look (real desktop windows are ≥800px tall, so the product clamp is 640px).
// Pinning to the product's own 640px cap misrepresents nothing; the sheet is one
// shared document, so this single rule must stay identical across stories.
const CAPTURE_FIT = '.rp-provenance { max-height: 640px; }';

function EditorBackdrop() {
  return (
    <div
      style={{
        minHeight: 600,
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
          maxWidth: 680,
        }}
      >
        <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-strong)' }}>Findings</div>
        <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--text-muted)' }}>
          Segmental filling defect in the right lower lobe pulmonary artery. No evidence of right
          heart strain. Lungs otherwise clear.
        </p>
      </div>
    </div>
  );
}

// PRIMARY — RC-07 provenance chain for a completed Generate Draft: prompt
// version, model route, rulebook + template bindings, and the human-review row.
export const GenerateDraftProvenance = () => (
  <>
    <style>{CAPTURE_FIT}</style>
    <EditorBackdrop />
    <ProvenanceModal
      open
      onClose={() => {}}
      entry={{
        id: 3,
        startedAt: Date.UTC(2026, 6, 20, 9, 32, 0),
        action: 'Generate Draft',
        status: 'completed',
        scope: 'Findings, Impression',
        provider: 'MedGemma (on-device)',
        model: 'medgemma-4b-it',
        promptVersion: 'report-draft@v12',
        latencyMs: 41800,
      }}
      context={{
        rulebook: { name: 'chest_ct_v1', version: '1.4.0' },
        template: { name: 'CT Chest — PE protocol' },
      }}
    />
  </>
);

// Partial chain — a failed action: the danger banner explains the chain is
// incomplete, and unrecorded rows show the explicit "Not recorded" state
// (prompt version + template) instead of invented values.
export const FailedActionPartialChain = () => (
  <>
    <style>{CAPTURE_FIT}</style>
    <EditorBackdrop />
    <ProvenanceModal
      open
      onClose={() => {}}
      entry={{
        id: 4,
        startedAt: Date.UTC(2026, 6, 20, 10, 5, 0),
        action: 'Rewrite (Concise)',
        status: 'failed',
        scope: 'Impression',
        provider: 'Claude',
        model: 'claude-sonnet-4-5',
        error: 'Provider returned 429 — rate limited.',
      }}
      context={{
        rulebook: { name: 'chest_ct_v1', version: '1.4.0' },
        template: null,
      }}
    />
  </>
);
