// Shared constants, pure helpers, and small presentational pieces for the
// report editor. Extracted from ReportClient so the ribbon + inspector
// components can reuse them without import cycles.
import type { Report, ValidationFinding, RewriteMode } from '@/lib/api';

export const SECTIONS: Array<{ key: keyof Report; label: string; cls?: string }> = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'comparison', label: 'Comparison' },
  { key: 'findings', label: 'Findings', cls: 'findings' },
  { key: 'impression', label: 'Impression', cls: 'impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

export const REWRITE_MODES: Array<{ mode: RewriteMode; label: string; hint: string }> = [
  { mode: 'concise', label: 'Concise', hint: 'Shorter, denser prose' },
  { mode: 'formal', label: 'Formal', hint: 'Strict radiology register' },
  { mode: 'patient_friendly', label: 'Patient-friendly', hint: 'Plain-language summary for the patient' },
  { mode: 'referring_summary', label: 'Referring summary', hint: 'Brief note for the referring clinician' },
];

export const REWRITABLE_KEYS: Array<keyof Report> = ['findings', 'impression', 'recommendations'];

export function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

// Maps a report status to a `.rp-status` tone variant for the header pill.
export function statusTone(s: Report['status']): 'neutral' | 'info' | 'success' {
  const label = statusLabel(s);
  if (label === 'Acknowledged' || label === 'Exported') return 'success';
  if (label === 'Validated') return 'info';
  return 'neutral';
}

export function severityClass(s: ValidationFinding['severity']): string {
  const v = typeof s === 'number' ? ['info', 'warning', 'blocker'][s] : String(s).toLowerCase();
  return v;
}

export function groupBySeverity(findings: ValidationFinding[]) {
  const groups = { blocker: [] as ValidationFinding[], warning: [] as ValidationFinding[], info: [] as ValidationFinding[] };
  for (const f of findings) {
    const k = severityClass(f.severity) as 'blocker' | 'warning' | 'info';
    if (groups[k]) groups[k].push(f);
  }
  return groups;
}

export function normalizeRole(role: string | number): string {
  if (typeof role === 'number') return ['Primary', 'CoSigner', 'Addendum'][role] ?? String(role);
  return role;
}

export function roleBadge(role: string | number): string {
  const r = normalizeRole(role);
  if (r === 'Primary') return 'ok';
  if (r === 'CoSigner') return 'info';
  if (r === 'Addendum') return 'ai';
  return '';
}

export function fmtDateTime(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

/**
 * AI-007 — render an `ai:unsupported_claim` finding with the offending
 * sentence quoted in `.ai-mark`, an "Unsupported claim" warning badge, and
 * an "Edit Findings" button that scrolls to the Findings section.
 */
export function UnsupportedClaimFinding({ finding }: { finding: ValidationFinding }) {
  const sentence = finding.snippet?.trim() || finding.message;
  function scrollToFindings() {
    const el = document.getElementById('rp-findings-section');
    if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    const ta = el.querySelector('textarea');
    if (ta && ta instanceof HTMLTextAreaElement) ta.focus();
  }
  return (
    <div className="finding warning">
      <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
        <span className="badge warn">Unsupported claim</span>
        {finding.section && <span className="badge">{finding.section}</span>}
      </div>
      <blockquote className="ai-mark" style={{ margin: '4px 0', padding: '6px 10px' }}>
        “{sentence}”
      </blockquote>
      <div className="rp-row" style={{ gap: 8, marginTop: 6 }}>
        <button className="subtle" onClick={scrollToFindings}>Edit Findings</button>
        <span className="rule"><code>{finding.ruleId}</code></span>
      </div>
    </div>
  );
}
