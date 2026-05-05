'use client';

// Iter-36 MOB — sign-acknowledgement screen. Renders the report
// read-only with `.ai-mark` styling preserved, lists outstanding
// validation findings (Blocker→red / Warning→amber / Info→blue per
// the locked severity map), and gates the "Acknowledge & Export"
// action behind two checkboxes:
//   1. "I have reviewed all AI-generated text" (mandatory)
//   2. "I acknowledge any unresolved warnings" (only if any warnings)
// The action calls `api.reports.acknowledge(reportId)` followed by
// the export of choice. RadioPad never auto-signs; this screen only
// records the acknowledgement and unlocks export.
//
// Locked design system: `.rp-mobile`, `.rp-panel`, `.finding.*`,
// `.rp-ack-row`, `.ai-mark`, button variants.

import { useCallback, useEffect, useState } from 'react';
import { api, type Report, type ValidationFinding } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';

type ExportFormat = 'text' | 'json' | 'fhir' | 'pdf';

const SECTIONS: Array<{ key: keyof Report; label: string }> = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'comparison', label: 'Comparison' },
  { key: 'findings', label: 'Findings' },
  { key: 'impression', label: 'Impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

function severityClass(sev: ValidationFinding['severity']): 'blocker' | 'warning' | 'info' {
  if (sev === 'Blocker') return 'blocker';
  if (sev === 'Warning') return 'warning';
  return 'info';
}

export default function MobileSignPage() {
  const [reportId, setReportId] = useState<string | null>(null);

  const [report, setReport] = useState<Report | null>(null);
  const [findings, setFindings] = useState<ValidationFinding[]>([]);
  const [aiSections, setAiSections] = useState<Record<string, boolean>>({});
  const [reviewedAi, setReviewedAi] = useState(false);
  const [reviewedWarnings, setReviewedWarnings] = useState(false);
  const [exportFormat, setExportFormat] = useState<ExportFormat>('text');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);

  useEffect(() => {
    setReportId(readQueryParam('reportId'));
  }, []);

  useEffect(() => {
    if (!reportId) return;
    let cancelled = false;
    (async () => {
      try {
        const r = await api.reports.get(reportId);
        if (cancelled) return;
        setReport(r);
        try {
          setAiSections(JSON.parse(r.aiHighlightsJson || '{}'));
        } catch {
          /* noop */
        }
        const v = await api.reports.validate(reportId);
        if (!cancelled) setFindings(v.findings);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load report');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [reportId]);

  const blockers = findings.filter((f) => f.severity === 'Blocker');
  const warnings = findings.filter((f) => f.severity === 'Warning');
  const requireWarningAck = warnings.length > 0;
  const canAcknowledge =
    !!report && blockers.length === 0 && reviewedAi && (!requireWarningAck || reviewedWarnings);

  const onAcknowledgeAndExport = useCallback(async () => {
    if (!reportId) { setError('Missing report id.'); return; }
    if (!canAcknowledge) return;
    setBusy(true);
    setError(null);
    setDone(null);
    try {
      await api.reports.acknowledge(reportId);
      if (exportFormat === 'text') {
        const text = await api.reports.exportText(reportId);
        triggerDownload(`${reportId}.txt`, new Blob([text], { type: 'text/plain' }));
      } else if (exportFormat === 'json') {
        const json = await api.reports.exportJson(reportId);
        triggerDownload(`${reportId}.json`, new Blob([JSON.stringify(json, null, 2)], { type: 'application/json' }));
      } else if (exportFormat === 'fhir') {
        const fhir = await api.reports.exportFhir(reportId);
        triggerDownload(`${reportId}.fhir.json`, new Blob([JSON.stringify(fhir, null, 2)], { type: 'application/fhir+json' }));
      } else {
        const blob = await api.reports.exportPdf(reportId);
        triggerDownload(`${reportId}.pdf`, blob);
      }
      setDone(`Acknowledged. Exported as ${exportFormat.toUpperCase()}.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not acknowledge / export');
    } finally {
      setBusy(false);
    }
  }, [canAcknowledge, exportFormat, reportId]);

  return (
    <section className="rp-mobile" aria-label="Acknowledge and export">
      <h1 className="rp-page-title">Acknowledge &amp; export</h1>
      <p className="rp-page-sub">
        RadioPad never auto-signs. Sign in your RIS/EHR; this screen records your
        acknowledgement and unlocks export.
      </p>

      {error && (
        <div className="banner danger" role="alert">
          {error}
        </div>
      )}
      {reportId === '' && (
        <div className="banner warn" role="alert">
          Missing report id.
        </div>
      )}
      {done && (
        <div className="banner info" role="status">
          {done}
        </div>
      )}

      {!report && !error && <div className="rp-page-sub">Loading…</div>}

      {report && (
        <>
          <div className="rp-panel">
            <div className="rp-panel-title">Report</div>
            {SECTIONS.map(({ key, label }) => {
              const body = String(report[key] ?? '');
              if (!body.trim()) return null;
              const isAi = !!aiSections[key];
              return (
                <div className="section-block" key={key as string}>
                  <label>{label}</label>
                  {isAi ? (
                    <div className="ai-mark" data-testid={`section-${key as string}`}>
                      <div className="rp-narrative">{body}</div>
                    </div>
                  ) : (
                    <div className="rp-narrative" data-testid={`section-${key as string}`}>
                      {body}
                    </div>
                  )}
                </div>
              );
            })}
          </div>

          <div className="rp-panel">
            <div className="rp-panel-title">Validation findings ({findings.length})</div>
            {findings.length === 0 && <div className="rp-page-sub">No outstanding findings.</div>}
            {findings.map((f, i) => (
              <div className={`finding ${severityClass(f.severity)}`} key={`${f.ruleId}-${i}`} data-testid="finding">
                <div>{f.message}</div>
                <div className="rule">
                  <code>{f.ruleId}</code>
                  {f.section ? ` · ${f.section}` : ''}
                </div>
              </div>
            ))}
          </div>

          {blockers.length > 0 && (
            <div className="banner danger" role="alert">
              Resolve {blockers.length} blocker{blockers.length === 1 ? '' : 's'} before acknowledging.
            </div>
          )}

          <div className="rp-ack-row">
            <input
              id="ack-ai"
              type="checkbox"
              checked={reviewedAi}
              onChange={(e) => setReviewedAi(e.target.checked)}
              data-testid="ack-ai"
            />
            <label htmlFor="ack-ai">I have reviewed all AI-generated text.</label>
          </div>

          {requireWarningAck && (
            <div className="rp-ack-row">
              <input
                id="ack-warn"
                type="checkbox"
                checked={reviewedWarnings}
                onChange={(e) => setReviewedWarnings(e.target.checked)}
                data-testid="ack-warn"
              />
              <label htmlFor="ack-warn">
                I acknowledge {warnings.length} unresolved warning{warnings.length === 1 ? '' : 's'}.
              </label>
            </div>
          )}

          <div className="rp-row between rp-mt-sm">
            <label className="rp-row rp-gap-sm">
              <span className="rp-stat-label">Format</span>
              <select
                value={exportFormat}
                onChange={(e) => setExportFormat(e.target.value as ExportFormat)}
                aria-label="Export format"
              >
                <option value="text">Text</option>
                <option value="json">JSON</option>
                <option value="fhir">FHIR</option>
                <option value="pdf">PDF</option>
              </select>
            </label>
            <button
              type="button"
              className="primary"
              onClick={onAcknowledgeAndExport}
              disabled={!canAcknowledge || busy}
              data-testid="ack-export"
            >
              {busy ? 'Working…' : 'Acknowledge & Export'}
            </button>
          </div>
        </>
      )}
    </section>
  );
}

function triggerDownload(name: string, blob: Blob): void {
  if (typeof window === 'undefined') return;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
