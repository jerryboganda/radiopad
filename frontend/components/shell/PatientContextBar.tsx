'use client';

// RC patient context bar (PRD §20.9, RC-01…RC-09). Sticky identity strip that
// sits under the shell topbar on the report composer: patient/study identity,
// procedure, clinical indication, priors, review + save state, and the Export
// action. Purely presentational and reusable — every value arrives via props;
// segments with no data are simply not rendered (no fabricated fields).
import type { ReactNode } from 'react';
import { ArrowLeft, CircleUser, Upload, CheckCircle2, RefreshCw, AlertTriangle } from 'lucide-react';

export interface PatientContextSegment {
  /** Small muted caption above the value (e.g. "Procedure"). */
  label?: string;
  value: ReactNode;
  /** Renders the value as a link-style button. */
  onClick?: () => void;
}

export interface PatientContextBarProps {
  /** Primary identity line (e.g. "CT Chest — Chest" or a patient label). */
  title: ReactNode;
  /** Mono identity code (accession number). */
  accession?: string;
  /** Secondary identity meta (e.g. "59Y · M"). */
  meta?: ReactNode;
  /** Chips shown next to the identity (status / priority / signed). */
  chips?: ReactNode;
  /** Additional divided info segments (procedure, indication, priors…). */
  segments?: PatientContextSegment[];
  /** Amber "Requires review" chip. */
  requiresReview?: boolean;
  /** Persistence state — drives the saved / autosaving / retry-sync chip. */
  saveState?: 'saved' | 'saving' | 'error';
  /** e.g. "Saved 2 min ago". */
  savedLabel?: string;
  onRetrySync?: () => void;
  onBack?: () => void;
  /** Export action (opens the export rail). */
  onExport?: () => void;
  exportDisabled?: boolean;
  exportTitle?: string;
}

export default function PatientContextBar(p: PatientContextBarProps) {
  return (
    <div className="rp-patientbar" role="region" aria-label="Study context">
      {p.onBack && (
        <button className="icon-btn rp-patientbar-back" type="button" onClick={p.onBack} aria-label="Back to worklist">
          <ArrowLeft size={15} aria-hidden />
        </button>
      )}

      <div className="rp-patientbar-identity">
        <span className="rp-patientbar-avatar" aria-hidden>
          <CircleUser size={18} />
        </span>
        <div className="rp-patientbar-idtext">
          <div className="rp-patientbar-title">
            {p.title}
            {p.chips}
          </div>
          <div className="rp-patientbar-meta">
            {p.accession && <code className="rp-patientbar-accession">{p.accession}</code>}
            {p.meta && <span>{p.meta}</span>}
          </div>
        </div>
      </div>

      {(p.segments ?? []).map((seg, i) => (
        <div className="rp-patientbar-seg" key={i}>
          {seg.label && <span className="rp-patientbar-seg-label">{seg.label}</span>}
          {seg.onClick ? (
            <button type="button" className="rp-patientbar-seg-link" onClick={seg.onClick}>
              {seg.value}
            </button>
          ) : (
            <span className="rp-patientbar-seg-value">{seg.value}</span>
          )}
        </div>
      ))}

      <div className="rp-patientbar-spacer" aria-hidden />

      {p.requiresReview && (
        <span className="badge warn rp-patientbar-review">
          <AlertTriangle size={11} aria-hidden /> Requires review
        </span>
      )}

      {p.saveState === 'saving' && (
        <span className="rp-patientbar-save is-saving" role="status">
          <span className="rp-spinner sm" aria-hidden /> Autosaving…
        </span>
      )}
      {p.saveState === 'saved' && (
        <span className="rp-patientbar-save is-saved" role="status">
          <CheckCircle2 size={13} aria-hidden /> {p.savedLabel ?? 'Saved'}
        </span>
      )}
      {p.saveState === 'error' && (
        <span className="rp-patientbar-save is-error" role="alert">
          <AlertTriangle size={13} aria-hidden /> Not synced
          {p.onRetrySync && (
            <button type="button" className="rp-patientbar-retry" onClick={p.onRetrySync}>
              <RefreshCw size={11} aria-hidden /> Retry sync
            </button>
          )}
        </span>
      )}

      {p.onExport && (
        <button
          className="primary rp-patientbar-export"
          type="button"
          onClick={p.onExport}
          disabled={p.exportDisabled}
          title={p.exportTitle}
        >
          <Upload size={14} aria-hidden /> Export
        </button>
      )}
    </div>
  );
}
