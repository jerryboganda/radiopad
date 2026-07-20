'use client';

// RC-09 export panel — destinations, per-destination format choice, the
// validation gate, a data-boundary notice, and Sending / Delivered / Failed
// states. Destinations are the REAL export paths this build ships: local
// file downloads (the existing export endpoints) and the RIS clipboard copy.
// No fake PACS/FHIR-gateway destinations are invented.
import { useState, type ReactNode } from 'react';
import { HardDriveDownload, ClipboardCopy, ShieldCheck, ShieldAlert, AlertTriangle, CheckCircle2 } from 'lucide-react';

export type ExportFormat = 'text' | 'json' | 'fhir' | 'pdf' | 'docx';

export const EXPORT_FORMATS: Array<{ fmt: ExportFormat; label: string }> = [
  { fmt: 'pdf', label: 'PDF' },
  { fmt: 'text', label: 'Plain text (.txt)' },
  { fmt: 'docx', label: 'Word (.docx)' },
  { fmt: 'json', label: 'JSON' },
  { fmt: 'fhir', label: 'FHIR' },
];

export interface ExportPanelProps {
  canExport: boolean;
  /** Existing status gate — exports require an acknowledged report. */
  exportAllowed: boolean;
  exportBlockedReason?: string;
  /** Validation result summary (when validation has been run). */
  validated: boolean;
  blockers: number;
  warnings: number;
  onOpenValidation: () => void;
  /** Existing download path — must reject on failure. */
  onExport: (fmt: ExportFormat) => Promise<void>;
  /** Slot for the existing Copy-for-RIS control. */
  risSlot?: ReactNode;
}

type DeliveryState =
  | { status: 'idle' }
  | { status: 'sending'; fmt: ExportFormat }
  | { status: 'delivered'; fmt: ExportFormat }
  | { status: 'failed'; fmt: ExportFormat; error: string };

export default function ExportPanel(p: ExportPanelProps) {
  const [fmt, setFmt] = useState<ExportFormat>('pdf');
  const [delivery, setDelivery] = useState<DeliveryState>({ status: 'idle' });

  const gateBlocked = p.blockers > 0;
  const disabled = !p.canExport || !p.exportAllowed || gateBlocked || delivery.status === 'sending';

  async function run(selected: ExportFormat) {
    setDelivery({ status: 'sending', fmt: selected });
    try {
      await p.onExport(selected);
      setDelivery({ status: 'delivered', fmt: selected });
    } catch (e) {
      setDelivery({
        status: 'failed',
        fmt: selected,
        error: (e as Error).message || 'Export failed.',
      });
    }
  }

  return (
    <div className="rp-exportpanel">
      {/* 1 — Destinations */}
      <div className="rp-panel-title">Destinations</div>
      <ul className="rp-exportpanel-dests">
        <li>
          <button
            type="button"
            className="rp-exportpanel-dest"
            onClick={() => run(fmt)}
            disabled={disabled}
            title={disabled ? (p.exportBlockedReason ?? 'Resolve blockers before exporting.') : `Download as ${labelOf(fmt)}`}
          >
            <span className="rp-exportpanel-dest-icon" aria-hidden><HardDriveDownload size={15} /></span>
            <div className="rp-exportpanel-dest-main">
              <span className="rp-exportpanel-dest-name">Download to this device</span>
              <span className="rp-exportpanel-dest-sub">Saved locally as the selected format</span>
            </div>
            <span className="badge ok">Ready</span>
          </button>
        </li>
        <li>
          <button
            type="button"
            className="rp-exportpanel-dest"
            onClick={() => window.dispatchEvent(new CustomEvent('radiopad:copy-to-ris'))}
            title="Copy report as plain text for RIS"
          >
            <span className="rp-exportpanel-dest-icon" aria-hidden><ClipboardCopy size={15} /></span>
            <div className="rp-exportpanel-dest-main">
              <span className="rp-exportpanel-dest-name">RIS clipboard</span>
              <span className="rp-exportpanel-dest-sub">Plain text · clipboard auto-clears in 30s</span>
            </div>
            <span className="badge ok">Ready</span>
          </button>
        </li>
      </ul>

      {/* 2 — Format */}
      <div className="rp-panel-title">Format</div>
      <div className="rp-exportpanel-formats" role="radiogroup" aria-label="Export format">
        {EXPORT_FORMATS.map((f) => (
          <button
            key={f.fmt}
            type="button"
            role="radio"
            aria-checked={fmt === f.fmt}
            className={`rp-exportpanel-format${fmt === f.fmt ? ' is-active' : ''}`}
            onClick={() => setFmt(f.fmt)}
          >
            {f.label}
          </button>
        ))}
      </div>

      {/* 3 — Validation gate */}
      {gateBlocked ? (
        <div className="banner danger rp-exportpanel-gate">
          <ShieldAlert size={14} aria-hidden />
          <span>
            Export blocked — resolve {p.blockers} blocker{p.blockers === 1 ? '' : 's'} first.{' '}
            <button type="button" className="rp-subtle-link" onClick={p.onOpenValidation}>
              Review blockers
            </button>
          </span>
        </div>
      ) : p.validated ? (
        <div className="banner ok rp-exportpanel-gate">
          <ShieldCheck size={14} aria-hidden />
          <span>
            All blockers resolved
            {p.warnings > 0 ? ` · ${p.warnings} warning${p.warnings === 1 ? '' : 's'} reviewed` : ' · 0 issues found'}.
          </span>
        </div>
      ) : (
        <div className="banner info rp-exportpanel-gate">
          <ShieldCheck size={14} aria-hidden />
          <span>
            Validation has not been run yet.{' '}
            <button type="button" className="rp-subtle-link" onClick={p.onOpenValidation}>
              Run validation
            </button>
          </span>
        </div>
      )}

      {!p.exportAllowed && (
        <div className="banner warn rp-exportpanel-gate">
          <AlertTriangle size={14} aria-hidden />
          <span>{p.exportBlockedReason ?? 'Acknowledge the report before exporting.'}</span>
        </div>
      )}

      {/* 4 — Data boundary */}
      <div className="rp-exportpanel-boundary">
        <AlertTriangle size={13} aria-hidden />
        <span>
          Exports are generated on this device. Downloads save locally; the RIS copy goes to your
          clipboard and auto-clears in 30 seconds.
        </span>
      </div>

      {/* 5 — Action + delivery states */}
      {delivery.status === 'sending' && (
        <div className="rp-exportpanel-status is-sending" role="status">
          <span className="rp-spinner sm" aria-hidden /> Exporting {labelOf(delivery.fmt)}…
        </div>
      )}
      {delivery.status === 'delivered' && (
        <div className="rp-exportpanel-status is-delivered" role="status">
          <CheckCircle2 size={14} aria-hidden /> Exported as {labelOf(delivery.fmt)}.
        </div>
      )}
      {delivery.status === 'failed' && (
        <div className="rp-exportpanel-status is-failed" role="alert">
          <AlertTriangle size={14} aria-hidden />
          <span>{delivery.error}</span>
          <button type="button" className="rp-subtle-link" onClick={() => run(delivery.fmt)}>
            Retry
          </button>
        </div>
      )}

      {p.canExport && (
        <button
          className="primary rp-exportpanel-run"
          type="button"
          disabled={disabled}
          aria-busy={delivery.status === 'sending'}
          title={!p.exportAllowed ? p.exportBlockedReason : undefined}
          onClick={() => run(fmt)}
        >
          {delivery.status === 'sending' ? 'Exporting…' : 'Export report'}
        </button>
      )}

      {p.risSlot && <div className="rp-exportpanel-ris">{p.risSlot}</div>}
    </div>
  );
}

function labelOf(fmt: ExportFormat): string {
  return EXPORT_FORMATS.find((f) => f.fmt === fmt)?.label ?? fmt.toUpperCase();
}
