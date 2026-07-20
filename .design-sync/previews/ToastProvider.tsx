import { useEffect } from 'react';
import { ToastProvider, useToast, type ToastInput } from '@radiopad/frontend';

// Pushes toasts once on mount so the static capture shows the toast region
// populated (duration: 0 keeps them until dismissed — no auto-dismiss race).
function PushOnMount({ items }: { items: ToastInput[] }) {
  const { toast } = useToast();
  useEffect(() => {
    items.forEach((t) => toast({ ...t, duration: 0 }));
    // mount-only: the fixture list is static
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return null;
}

// A quiet app surface behind the toasts, so the fixed bottom-right region has
// something to float over (as it does over the worklist in the product).
function WorklistBackdrop({ note }: { note: string }) {
  return (
    <div
      style={{
        minHeight: 300,
        border: '1px solid var(--border)',
        borderRadius: 10,
        background: 'var(--bg-panel)',
        padding: 16,
      }}
    >
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-strong)' }}>Worklist</div>
      <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--text-muted)', maxWidth: 420 }}>{note}</p>
    </div>
  );
}

// Success + info stack — the shapes fired after export and rulebook binding.
export const ExportedToPacs = () => (
  <ToastProvider>
    <WorklistBackdrop note="CT Chest — signed report queued for delivery. Toasts stack bottom-right and slide in from the right." />
    <PushOnMount
      items={[
        { tone: 'success', title: 'Report exported to PACS', message: 'CT Chest — sent to Orthanc node 1.' },
        { tone: 'info', title: 'Rulebook bound', message: 'chest_ct_v1 is now validating this report.' },
      ]}
    />
  </ToastProvider>
);

// Danger toast — errors linger longer (8 s default) and read as blockers.
export const CriticalResultFlagged = () => (
  <ToastProvider>
    <WorklistBackdrop note="Validation surfaced a communication-critical finding on the open study." />
    <PushOnMount
      items={[
        {
          tone: 'danger',
          title: 'Critical result flagged',
          message: 'Suspected tension pneumothorax — notify the referring physician now.',
        },
      ]}
    />
  </ToastProvider>
);

// AI tone — background generation finished; the draft still requires review.
export const AiDraftReady = () => (
  <ToastProvider>
    <WorklistBackdrop note="Impression generation runs in the background while the radiologist keeps dictating." />
    <PushOnMount
      items={[
        { tone: 'ai', title: 'Impression draft ready', message: 'Drafted from Findings — review before acknowledging.' },
      ]}
    />
  </ToastProvider>
);
