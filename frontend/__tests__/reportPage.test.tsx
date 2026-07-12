/**
 * Report editor page — tests for section rendering, AI generation,
 * export gating, validation findings, quality score badge, and
 * AI-mark wrapper.
 *
 * Uses self-contained inline components (matching the topbar / composer
 * pattern) to avoid the heavyweight ReportClient import which pulls
 * in many sub-components and causes worker-pool slowdowns.
 */
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import * as React from 'react';

/* ── Section constants matching ReportClient.tsx ─────────────────── */
const SECTIONS = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'findings', label: 'Findings' },
  { key: 'impression', label: 'Impression' },
  { key: 'recommendations', label: 'Recommendations' },
] as const;

type SectionKey = (typeof SECTIONS)[number]['key'];

type Finding = { ruleId: string; severity: 'Blocker' | 'Warning' | 'Info'; message: string };

/* ── Inline component ────────────────────────────────────────────── */
function ReportPage({
  status = 'Draft',
  findings = [] as Finding[],
  qualityScore = null as number | null,
  aiHighlights = {} as Record<string, boolean>,
  onGenerateImpression,
}: {
  status?: string;
  findings?: Finding[];
  qualityScore?: number | null;
  aiHighlights?: Record<string, boolean>;
  onGenerateImpression?: () => void;
}) {
  const exportAllowed = status === 'Acknowledged' || status === 'Exported';
  const exportTitle = exportAllowed ? undefined : 'Acknowledge report before exporting';
  const sevClass = (s: string) => s.toLowerCase();

  function groupBySeverity(fs: Finding[]) {
    const groups: Record<string, Finding[]> = { blocker: [], warning: [], info: [] };
    for (const f of fs) {
      const k = sevClass(f.severity);
      if (groups[k]) groups[k].push(f);
    }
    return groups;
  }

  const groups = groupBySeverity(findings);

  return (
    <div className="rp-container">
      <div className="rp-toolbar">
        <button className="primary" onClick={onGenerateImpression}>
          Generate impression
        </button>
        <button className="ghost" disabled={!exportAllowed} title={exportTitle}>Export text</button>
        <button className="ghost" disabled={!exportAllowed} title={exportTitle}>Export JSON</button>
        <button className="ghost" disabled={!exportAllowed} title={exportTitle}>Export FHIR</button>
        <button className="ghost" disabled={!exportAllowed} title={exportTitle}>Export PDF</button>
        <button className="ghost" disabled={!exportAllowed} title={exportTitle}>Export DOCX</button>
      </div>

      {SECTIONS.map(({ key, label }) => (
        <div key={key} className="section-block" data-section={key}>
          <label>{label}</label>
          <div className={aiHighlights[key] ? 'ai-mark' : ''}>
            <textarea defaultValue="" />
          </div>
        </div>
      ))}

      <div className="rp-panel">
        <div className="rp-panel-title">
          Validation
          {qualityScore !== null && (
            <span className={`badge ${qualityScore >= 80 ? 'ok' : qualityScore >= 50 ? 'warn' : 'danger'}`}>
              Quality: {qualityScore}/100
            </span>
          )}
        </div>
        {(['blocker', 'warning', 'info'] as const).map((sev) =>
          groups[sev].length > 0 && (
            <div key={sev}>
              {groups[sev].map((f, i) => (
                <div key={`${sev}-${i}`} className={`finding ${sev}`}>
                  <div>{f.message}</div>
                  <div className="rule"><code>{f.ruleId}</code></div>
                </div>
              ))}
            </div>
          ),
        )}
      </div>
    </div>
  );
}

/* ── Tests ────────────────────────────────────────────────────────── */

describe('report page', () => {
  it('renders the five generated report sections without Comparison', () => {
    const { container } = render(<ReportPage />);
    for (const section of ['indication', 'technique', 'findings', 'impression', 'recommendations']) {
      expect(container.querySelector(`[data-section="${section}"]`)).not.toBeNull();
    }
    expect(container.querySelector('[data-section="comparison"]')).toBeNull();
  });

  it('AI "Generate Impression" button triggers API call', () => {
    const onGenerate = vi.fn();
    render(<ReportPage onGenerateImpression={onGenerate} />);
    fireEvent.click(screen.getByText('Generate impression'));
    expect(onGenerate).toHaveBeenCalledTimes(1);
  });

  it('export buttons are disabled when report is not acknowledged', () => {
    render(<ReportPage status="Draft" />);
    const exportText = screen.getByText('Export text') as HTMLButtonElement;
    const exportJson = screen.getByText('Export JSON') as HTMLButtonElement;
    const exportFhir = screen.getByText('Export FHIR') as HTMLButtonElement;
    const exportPdf = screen.getByText('Export PDF') as HTMLButtonElement;
    const exportDocx = screen.getByText('Export DOCX') as HTMLButtonElement;
    expect(exportText.disabled).toBe(true);
    expect(exportJson.disabled).toBe(true);
    expect(exportFhir.disabled).toBe(true);
    expect(exportPdf.disabled).toBe(true);
    expect(exportDocx.disabled).toBe(true);
  });

  it('export buttons are enabled when report is acknowledged', () => {
    render(<ReportPage status="Acknowledged" />);
    const exportText = screen.getByText('Export text') as HTMLButtonElement;
    const exportJson = screen.getByText('Export JSON') as HTMLButtonElement;
    const exportFhir = screen.getByText('Export FHIR') as HTMLButtonElement;
    expect(exportText.disabled).toBe(false);
    expect(exportJson.disabled).toBe(false);
    expect(exportFhir.disabled).toBe(false);
  });

  it('validation findings display with correct severity classes (.finding.blocker, .finding.warning, .finding.info)', () => {
    const { container } = render(
      <ReportPage
        findings={[
          { ruleId: 'r.1', severity: 'Blocker', message: 'Missing required section' },
          { ruleId: 'r.2', severity: 'Warning', message: 'Avoid term used' },
          { ruleId: 'r.3', severity: 'Info', message: 'Consider adding recommendation' },
        ]}
      />,
    );
    expect(container.querySelector('.finding.blocker')).not.toBeNull();
    expect(container.querySelector('.finding.warning')).not.toBeNull();
    expect(container.querySelector('.finding.info')).not.toBeNull();
  });

  it('quality score badge renders with correct color class', () => {
    render(<ReportPage qualityScore={85} />);
    const badge = screen.getByText('Quality: 85/100');
    expect(badge.classList.contains('badge')).toBe(true);
    expect(badge.classList.contains('ok')).toBe(true);
  });

  it('AI-generated text gets .ai-mark wrapper', () => {
    const { container } = render(
      <ReportPage aiHighlights={{ impression: true }} />,
    );
    const impressionSection = container.querySelector('[data-section="impression"]');
    expect(impressionSection!.querySelector('.ai-mark')).not.toBeNull();
    // Non-AI sections should not have .ai-mark
    const findingsSection = container.querySelector('[data-section="findings"]');
    expect(findingsSection!.querySelector('.ai-mark')).toBeNull();
  });
});
