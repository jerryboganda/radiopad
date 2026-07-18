// F12 — the AI actions bar must expose a free-text "Custom edit" that fires onRewrite('custom',
// instruction). The instruction is trimmed and the button is disabled until there is one.
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import AiActionsBar, { type AiBarAction } from '@/components/reports/AiActionsBar';
import type { RewriteMode } from '@/lib/api';

function renderBar(onRewrite: (mode: RewriteMode, instruction?: string) => void) {
  return render(
    <AiActionsBar
      canEdit
      busyAction={null as AiBarAction | null}
      onGenerateDraft={vi.fn()}
      onGenerateImpression={vi.fn()}
      rewriteModes={[{ mode: 'concise', label: 'Concise', hint: 'shorter' }]}
      sections={[{ key: 'impression', label: 'Impression' }]}
      rewriteSection="impression"
      onRewriteSectionChange={vi.fn()}
      onRewrite={onRewrite}
      rewriteBusy={false}
      rewriteOpen
      onRewriteOpenChange={vi.fn()}
      stylePanelOpen={false}
      onToggleStylePanel={vi.fn()}
      providers={[]}
      providerId=""
      onProviderChange={vi.fn()}
    />,
  );
}

describe('AiActionsBar — custom edit (F12)', () => {
  it('fires onRewrite("custom", instruction) with the trimmed instruction', () => {
    const onRewrite = vi.fn();
    renderBar(onRewrite);

    const box = screen.getByLabelText(/custom edit/i);
    fireEvent.change(box, { target: { value: '  make the impression more concise  ' } });
    fireEvent.click(screen.getByRole('button', { name: /apply custom edit/i }));

    expect(onRewrite).toHaveBeenCalledWith('custom', 'make the impression more concise');
  });

  it('disables Apply until there is a non-empty instruction', () => {
    renderBar(vi.fn());
    const apply = screen.getByRole('button', { name: /apply custom edit/i });
    expect(apply).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/custom edit/i), { target: { value: 'x' } });
    expect(apply).not.toBeDisabled();
  });
});
