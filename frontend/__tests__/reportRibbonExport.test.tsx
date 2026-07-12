import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ReportRibbon, { type ReportRibbonProps } from '@/app/(desktop)/reports/[id]/ReportRibbon';

vi.mock('@/app/(desktop)/reports/[id]/CopyToRisButton', () => ({ default: () => null }));

function props(onExport: ReportRibbonProps['onExport']): ReportRibbonProps {
  const noop = vi.fn();
  return {
    tab: 'export', onTabChange: noop,
    canEdit: true, canValidate: true, canExport: true, canSign: true,
    providers: [], providerId: '', onProviderChange: noop,
    rulebooks: [], rulebookId: null, onRulebookChange: noop,
    aiBusy: false, onGenerate: noop,
    rewriteOpen: false, onToggleRewrite: noop, rewriteBusy: false,
    rewriteSection: 'findings', onRewriteSectionChange: noop, onRewrite: noop,
    stylePanelOpen: false, onToggleStylePanel: noop,
    onDictate: noop, dictating: false,
    voiceCommandMode: false, onToggleVoiceCommand: noop, voiceCommandPills: [],
    onValidate: noop, showPrior: false, onTogglePrior: noop,
    reportId: 'report-1', exportAllowed: true, exportMenuOpen: true,
    onToggleExportMenu: noop, onCloseExportMenu: noop, onExport,
    blockers: 0, onAcknowledge: noop, primarySigned: false, onGoToSignoff: noop,
  };
}

describe('report ribbon exports', () => {
  it('dispatches every export format from the dropdown', () => {
    const onExport = vi.fn();
    render(<ReportRibbon {...props(onExport)} />);

    const expected = [
      ['Plain text (.txt)', 'text'],
      ['JSON', 'json'],
      ['FHIR', 'fhir'],
      ['PDF', 'pdf'],
      ['Word (.docx)', 'docx'],
    ] as const;
    for (const [label] of expected) fireEvent.click(screen.getByRole('menuitem', { name: label }));

    expect(onExport.mock.calls.map(([format]) => format)).toEqual(expected.map(([, format]) => format));
  });
});
