'use client';

// RC-07 citation & provenance modal — the chain behind one AI action: prompt
// version, model route, rulebook + template bindings, human edits. Values are
// exactly what the AI job results and the report bindings expose; anything
// the backend did not record renders an explicit "Not recorded" row instead
// of an invented value. Includes the source-unavailable state for failed jobs.
import { useEffect, type ReactNode } from 'react';
import { X, FileCode2, BookOpenCheck, Route, User, AlertTriangle } from 'lucide-react';
import type { AiActivityEntry } from './AiActivityPanel';

export interface ProvenanceContext {
  rulebook?: { name: string; version: string } | null;
  template?: { name: string } | null;
}

export interface ProvenanceModalProps {
  open: boolean;
  onClose: () => void;
  entry: AiActivityEntry | null;
  context: ProvenanceContext;
}

export default function ProvenanceModal(p: ProvenanceModalProps) {
  useEffect(() => {
    if (!p.open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') p.onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [p.open, p.onClose]);

  if (!p.open) return null;

  const entry = p.entry;
  const route = entry ? [entry.provider, entry.model].filter(Boolean).join(' / ') : '';

  return (
    <div className="rp-provenance-scrim" role="presentation" onClick={p.onClose}>
      <div
        className="rp-provenance"
        role="dialog"
        aria-modal="true"
        aria-label="Citation and provenance"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="rp-provenance-head">
          <span className="rp-provenance-title">Citation &amp; provenance</span>
          <button className="icon-btn" type="button" onClick={p.onClose} aria-label="Close provenance">
            <X size={15} aria-hidden />
          </button>
        </header>

        {entry === null ? (
          <div className="rp-provenance-body">
            <p className="rp-page-sub">No AI action selected.</p>
          </div>
        ) : (
          <div className="rp-provenance-body">
            <div className="rp-provenance-claim">
              <span className="rp-provenance-claim-label">Action</span>
              <span className="rp-provenance-claim-value">
                {entry.action} <span className="badge ai">✨ generated</span>
              </span>
              {entry.scope && <span className="rp-provenance-claim-scope">Wrote into: {entry.scope}</span>}
            </div>

            {entry.status === 'failed' && (
              <div className="banner danger rp-provenance-unavailable">
                <AlertTriangle size={14} aria-hidden />
                <span>
                  This action failed before completing — its provenance chain is partial.
                  {entry.error ? ` ${entry.error}` : ''}
                </span>
              </div>
            )}

            <div className="rp-provenance-chain">
              <ChainRow
                icon={<FileCode2 size={14} aria-hidden />}
                label="Prompt version"
                value={entry.promptVersion || null}
                hint="The prompt revision used for this generation."
              />
              <ChainRow
                icon={<Route size={14} aria-hidden />}
                label="Model route"
                value={route || null}
                hint="The approved provider route used for this action."
              />
              <ChainRow
                icon={<BookOpenCheck size={14} aria-hidden />}
                label="Rulebook"
                value={p.context.rulebook ? `${p.context.rulebook.name} · v${p.context.rulebook.version}` : null}
                hint="Clinical validation rules bound to this report."
              />
              <ChainRow
                icon={<FileCode2 size={14} aria-hidden />}
                label="Template"
                value={p.context.template ? p.context.template.name : null}
                hint="Report scaffolding bound to this report."
              />
              <ChainRow
                icon={<User size={14} aria-hidden />}
                label="Human review"
                value="AI text stays marked until a radiologist accepts or edits it."
              />
            </div>

            <div className="rp-provenance-what">
              <div className="rp-provenance-what-title">What this means</div>
              <p>
                This content was generated with the approved route above and reviewed under the
                report&apos;s rulebook. It remains visually marked until a radiologist accepts or
                edits it — RadioPad never signs a report automatically.
              </p>
            </div>
          </div>
        )}

        <footer className="rp-provenance-foot">
          <button className="ghost" type="button" onClick={p.onClose}>Close</button>
        </footer>
      </div>
    </div>
  );
}

function ChainRow({
  icon,
  label,
  value,
  hint,
}: {
  icon: ReactNode;
  label: string;
  value: string | null;
  hint?: string;
}) {
  return (
    <div className="rp-provenance-row">
      <span className="rp-provenance-row-icon" aria-hidden>{icon}</span>
      <div className="rp-provenance-row-main">
        <span className="rp-provenance-row-label">{label}</span>
        {value ? (
          <span className="rp-provenance-row-value">{value}</span>
        ) : (
          <span className="rp-provenance-row-missing">Not recorded for this action</span>
        )}
        {hint && <span className="rp-provenance-row-hint">{hint}</span>}
      </div>
    </div>
  );
}
