// F12 — the composer ribbon must expose a free-text "Custom edit" that fires onRewrite('custom',
// instruction). The instruction is trimmed and the button is disabled until there is one.
// Also locks down the group-gating rule: Review renders regardless of canEdit; AI Compose is
// canEdit-gated; Sign-off disappears entirely (caption included) when neither canEdit nor canSign
// is set, so no orphan group caption with zero buttons underneath it.
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import ComposerRibbon, { type ComposerRibbonProps } from '@/components/reports/ComposerRibbon';
import type { RewriteMode } from '@/lib/api';

function baseProps(onRewrite: (mode: RewriteMode, instruction?: string) => void): ComposerRibbonProps {
  return {
    dictating: false,
    onDictate: vi.fn(),
    voiceCommandMode: false,
    onToggleVoiceCommands: vi.fn(),
    canValidate: false,
    onValidate: vi.fn(),
    showPrior: false,
    onToggleCompare: vi.fn(),
    showDictationDraft: false,
    onToggleFormatDraft: vi.fn(),
    canEdit: true,
    busyAction: null,
    onGenerateDraft: vi.fn(),
    onGenerateImpression: vi.fn(),
    rewriteModes: [{ mode: 'concise', label: 'Concise', hint: 'shorter' }],
    sections: [{ key: 'impression', label: 'Impression' }],
    rewriteSection: 'impression',
    onRewriteSectionChange: vi.fn(),
    onRewrite,
    rewriteBusy: false,
    rewriteOpen: true,
    onRewriteOpenChange: vi.fn(),
    stylePanelOpen: false,
    onToggleStylePanel: vi.fn(),
    providerId: '',
    canSign: false,
    canExport: false,
    showSignSend: false,
    onToggleSignSend: vi.fn(),
    blockers: 0,
    enforceBlockers: true,
    onAcknowledge: vi.fn(),
    providers: [],
    rewriteProviderId: '',
    onRewriteProviderChange: vi.fn(),
    primarySigned: false,
    onOpenSignoff: vi.fn(),
    pairOpen: false,
    onTogglePair: vi.fn(),
  };
}

function renderBar(
  onRewrite: (mode: RewriteMode, instruction?: string) => void,
  overrides: Partial<ComposerRibbonProps> = {},
) {
  return render(<ComposerRibbon {...baseProps(onRewrite)} {...overrides} />);
}

describe('ComposerRibbon — custom edit (F12)', () => {
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

describe('ComposerRibbon — group gating', () => {
  it('keeps the Review group visible even when canEdit is false', () => {
    renderBar(vi.fn(), { canEdit: false });
    expect(screen.getByRole('button', { name: /dictate/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /compare/i })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /generate draft/i })).toBeNull();
  });

  it('hides the Sign-off group entirely when neither canEdit nor canSign is set', () => {
    renderBar(vi.fn(), { canEdit: false, canSign: false });
    expect(screen.queryByText('Sign-off')).toBeNull();
    expect(screen.queryByRole('button', { name: /acknowledge . lock/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /review . sign/i })).toBeNull();
  });

  it('shows Acknowledge & lock when canEdit is true even without sign permission', () => {
    renderBar(vi.fn(), { canEdit: true, canSign: false });
    expect(screen.getByRole('button', { name: /acknowledge . lock/i })).toBeTruthy();
  });
});
