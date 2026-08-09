import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import React from 'react';
import ComposerRibbon from '../components/reports/ComposerRibbon';
import CompanionHostPanel from '../components/companion/CompanionHostPanel';
import { CompanionProvider, useCompanion } from '../components/companion/CompanionContext';

describe('ComposerRibbon & CompanionHostPanel integration', () => {
  it('renders Ribbon with Pair phone button', () => {
    const onTogglePair = vi.fn();
    render(
      <CompanionProvider>
        <ComposerRibbon
          dictating={false}
          onDictate={vi.fn()}
          voiceCommandMode={false}
          onToggleVoiceCommands={vi.fn()}
          canValidate={true}
          onValidate={vi.fn()}
          showPrior={false}
          onToggleCompare={vi.fn()}
          showDictationDraft={false}
          onToggleFormatDraft={vi.fn()}
          canEdit={true}
          activeActions={[]}
          onGenerateDraft={vi.fn()}
          onGenerateImpression={vi.fn()}
          rewriteModes={[]}
          sections={[]}
          rewriteSection="findings"
          onRewriteSectionChange={vi.fn()}
          onRewrite={vi.fn()}
          rewriteBusy={false}
          rewriteOpen={false}
          onRewriteOpenChange={vi.fn()}
          stylePanelOpen={false}
          onToggleStylePanel={vi.fn()}
          providerId="test-prov"
          providers={[]}
          rewriteProviderId=""
          onRewriteProviderChange={vi.fn()}
          canSign={false}
          canExport={true}
          showSignSend={false}
          onToggleSignSend={vi.fn()}
          blockers={0}
          enforceBlockers={true}
          onAcknowledge={vi.fn()}
          primarySigned={false}
          onOpenSignoff={vi.fn()}
          pairOpen={false}
          onTogglePair={onTogglePair}
        />
      </CompanionProvider>
    );

    const pairBtn = screen.getByRole('button', { name: /Pair phone/i });
    expect(pairBtn).toBeInTheDocument();
    fireEvent.click(pairBtn);
    expect(onTogglePair).toHaveBeenCalled();
  });

  it('renders CompanionHostPanel when open and shows Start pairing or Manage link', () => {
    render(
      <CompanionProvider>
        <CompanionHostPanel open={true} />
      </CompanionProvider>
    );

    expect(screen.getByRole('dialog', { name: /Phone companion/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Start pairing/i })).toBeInTheDocument();
  });
});
