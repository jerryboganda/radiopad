/**
 * RC-09 export panel — successor to the retired ribbon Export ▾ menu test.
 * Verifies every export format still dispatches through the panel's format
 * selector + Export button, and that the validation gate blocks exporting
 * while blockers exist.
 */
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import ExportPanel from '@/components/reports/ExportPanel';

function baseProps(onExport: (fmt: string) => Promise<void>) {
  return {
    canExport: true,
    exportAllowed: true,
    validated: false,
    blockers: 0,
    warnings: 0,
    onOpenValidation: vi.fn(),
    onExport: onExport as never,
  };
}

describe('report export panel', () => {
  it('dispatches every export format through the format selector', async () => {
    const onExport = vi.fn((_fmt: string) => Promise.resolve());
    render(<ExportPanel {...baseProps(onExport)} />);

    const expected = [
      ['PDF', 'pdf'],
      ['Plain text (.txt)', 'text'],
      ['Word (.docx)', 'docx'],
      ['JSON', 'json'],
      ['FHIR', 'fhir'],
    ] as const;

    for (let i = 0; i < expected.length; i++) {
      const [label, fmt] = expected[i];
      fireEvent.click(screen.getByRole('radio', { name: label }));
      fireEvent.click(screen.getByRole('button', { name: /Export report|Exporting…/ }));
      await waitFor(() => expect(onExport).toHaveBeenCalledTimes(i + 1));
      expect(onExport.mock.calls[i][0]).toBe(fmt);
      // Wait for the Delivered state so the button re-enables for the next pass.
      await waitFor(() =>
        expect((screen.getByRole('button', { name: 'Export report' }) as HTMLButtonElement).disabled).toBe(false),
      );
    }
  });

  it('blocks exporting while validation blockers exist', () => {
    const onExport = vi.fn(() => Promise.resolve());
    render(<ExportPanel {...baseProps(onExport)} blockers={2} validated />);

    const btn = screen.getByRole('button', { name: 'Export report' }) as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(screen.getByText(/Export blocked/)).toBeTruthy();
  });

  it('disables exporting until the report is acknowledged (status gate)', () => {
    const onExport = vi.fn(() => Promise.resolve());
    render(
      <ExportPanel
        {...baseProps(onExport)}
        exportAllowed={false}
        exportBlockedReason="Acknowledge report before exporting"
      />,
    );

    const btn = screen.getByRole('button', { name: 'Export report' }) as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(screen.getByText('Acknowledge report before exporting')).toBeTruthy();
  });
});
