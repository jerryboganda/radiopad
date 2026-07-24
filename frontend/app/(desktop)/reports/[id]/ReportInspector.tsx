'use client';

// RC right rail for the report composer — tabbed panel with Checklist /
// Details / Validation (RC-04) / AI activity (RC-06) / Export (RC-09) /
// Sign-off. Purely presentational: state + handlers arrive from ReportClient.
// The old Context tab moved to the left StudyContextPanel (RC-01).
import type { ReactNode } from 'react';
import type { Report, ValidationFinding, ReportTemplate, ReportSignature, Rulebook, Provider } from '@/lib/api';
import {
  groupBySeverity,
  normalizeRole,
  roleBadge,
  fmtDateTime,
  statusLabel,
  statusTone,
  UnsupportedClaimFinding,
} from './reportShared';
import ChecklistPanel from '@/components/reports/ChecklistPanel';
import AiActivityPanel, { type AiActivityEntry } from '@/components/reports/AiActivityPanel';
import ExportPanel, { type ExportFormat } from '@/components/reports/ExportPanel';
import ErrorState from '@/components/ui/ErrorState';
import EmptyState from '@/components/ui/EmptyState';
import Skeleton from '@/components/ui/Skeleton';
import { ShieldCheck, CornerDownRight, RefreshCw } from 'lucide-react';

export type InspectorTab = 'checklist' | 'details' | 'validation' | 'activity' | 'export' | 'signoff';

export interface ReportInspectorProps {
  tab: InspectorTab;
  onTabChange: (t: InspectorTab) => void;

  report: Report;

  // Checklist
  hasAiText: boolean;
  onAcknowledge: () => void;

  // Details
  templates: ReportTemplate[];
  rulebooks: Rulebook[];

  // Validation (RC-04)
  findings: ValidationFinding[];
  qualityScore: number | null;
  blockers: number;
  /** Tenant's `RequireZeroBlockers` — gates the export button. */
  enforceBlockers: boolean;
  validationState: 'idle' | 'running' | 'done' | 'error';
  validationError: string | null;
  lastValidatedAt: Date | null;
  canValidate: boolean;
  onValidate: () => void;
  /** `severity` tints the flash the target section plays on arrival (RC-04). */
  onJumpToSection: (section: string, severity?: string) => void;

  // AI activity (RC-06)
  aiActivity: AiActivityEntry[];
  provider: Provider | null;
  onShowProvenance: (entry: AiActivityEntry) => void;

  // Export (RC-09)
  canExport: boolean;
  exportAllowed: boolean;
  exportTitle?: string;
  onExport: (fmt: ExportFormat) => Promise<void>;
  risSlot?: ReactNode;

  // Permissions
  canEdit: boolean;

  // Sign-off
  canSign: boolean;
  primarySigned: boolean;
  signatures: ReportSignature[];
  signBusy: boolean;
  signNote: string;
  onSignNoteChange: (v: string) => void;
  addendumBody: string;
  onAddendumBodyChange: (v: string) => void;
  addendumOpen: boolean;
  onToggleAddendum: () => void;
  onSignPrimary: () => void;
  onAddCoSigner: () => void;
  onSubmitAddendum: () => void;
}

export default function ReportInspector(p: ReportInspectorProps) {
  const tabs: Array<{ id: InspectorTab; label: string }> = [
    { id: 'checklist', label: 'Checklist' },
    { id: 'details', label: 'Details' },
    { id: 'validation', label: 'Validation' },
    { id: 'activity', label: 'AI activity' },
    { id: 'export', label: 'Export' },
  ];
  if (p.canSign) tabs.push({ id: 'signoff', label: 'Sign-off' });

  return (
    <aside className="rp-inspector rp-rail">
      <div className="rp-rail-tabbar" role="tablist" aria-label="Report inspector">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={p.tab === t.id}
            className={`rp-rail-tab${p.tab === t.id ? ' is-active' : ''}`}
            onClick={() => p.onTabChange(t.id)}
          >
            {t.label}
            {t.id === 'validation' && p.blockers > 0 && (
              <span className="rp-rail-tab-count" aria-label={`${p.blockers} blockers`}>{p.blockers}</span>
            )}
          </button>
        ))}
      </div>

      <div className="rp-inspector-body" role="tabpanel">
        {p.tab === 'checklist' && (
          <ChecklistPanel
            report={p.report}
            hasAiText={p.hasAiText}
            validated={p.validationState === 'done'}
            blockers={p.blockers}
            primarySigned={p.primarySigned}
            canEdit={p.canEdit}
            onAcknowledge={p.onAcknowledge}
          />
        )}
        {p.tab === 'details' && <DetailsPanel {...p} />}
        {p.tab === 'validation' && <ValidationPanel {...p} />}
        {p.tab === 'activity' && (
          <AiActivityPanel entries={p.aiActivity} provider={p.provider} onShowProvenance={p.onShowProvenance} />
        )}
        {p.tab === 'export' && (
          <ExportPanel
            canExport={p.canExport}
            exportAllowed={p.exportAllowed}
            exportBlockedReason={p.exportTitle}
            validated={p.validationState === 'done'}
            blockers={p.blockers}
            enforceBlockers={p.enforceBlockers}
            warnings={groupBySeverity(p.findings).warning.length}
            onOpenValidation={() => {
              p.onTabChange('validation');
              if (p.validationState === 'idle' && p.canValidate) p.onValidate();
            }}
            onExport={p.onExport}
            risSlot={p.risSlot}
          />
        )}
        {p.tab === 'signoff' && p.canSign && <SignoffPanel {...p} />}
      </div>
    </aside>
  );
}

function DetailsPanel(p: ReportInspectorProps) {
  const template = p.templates.find((t) => t.id === p.report.templateId);
  const rulebook = p.rulebooks.find((r) => r.id === p.report.rulebookId);
  const primary = p.signatures.find((s) => normalizeRole(s.role) === 'Primary');
  return (
    <div className="rp-panel">
      <div className="rp-panel-title">Details</div>
      <dl className="rp-rail-facts">
        {primary && (
          <div><dt>Report author</dt><dd><code>{primary.radiologistEmail}</code></dd></div>
        )}
        <div>
          <dt>Status</dt>
          <dd><span className={`rp-status ${statusTone(p.report.status)}`}>{statusLabel(p.report.status)}</span></dd>
        </div>
        <div><dt>Last edited</dt><dd>{fmtDateTime(p.report.updatedAt)}</dd></div>
        <div><dt>Report ID</dt><dd><code>{p.report.id.slice(0, 8)}</code></dd></div>
        {p.report.study.accessionNumber && (
          <div><dt>Accession</dt><dd><code>{p.report.study.accessionNumber}</code></dd></div>
        )}
        <div><dt>Template</dt><dd>{template ? template.name : '—'}</dd></div>
        <div><dt>Rulebook</dt><dd>{rulebook ? `${rulebook.name} · v${rulebook.version}` : '—'}</dd></div>
      </dl>
    </div>
  );
}

/** RC-04 — severity stat tiles, grouped issue cards with jump links, and the
 * all-clear / running / engine-offline states. Accept/override per finding is
 * not part of the current validation flow, so no such controls are faked. */
function ValidationPanel(p: ReportInspectorProps) {
  if (p.validationState === 'running') {
    return (
      <div className="rp-valpanel" role="status" aria-busy="true">
        <div className="banner info">Validating report… your current draft is preserved.</div>
        <Skeleton variant="row" />
        <Skeleton variant="row" />
        <Skeleton variant="row" />
        <span className="rp-sr-only">Validation running…</span>
      </div>
    );
  }

  if (p.validationState === 'error') {
    return (
      <div className="rp-valpanel">
        <ErrorState
          title="Validation engine unavailable"
          message={
            <>
              {p.validationError || 'The validation service could not be reached.'}
              <br />
              Your draft is safe — no data was lost.
            </>
          }
          onRetry={p.canValidate ? p.onValidate : undefined}
          retryLabel="Retry validation"
        />
      </div>
    );
  }

  if (p.validationState === 'idle') {
    return (
      <div className="rp-valpanel">
        <EmptyState
          icon={<ShieldCheck size={18} aria-hidden />}
          title="Not validated yet"
          description="Run rulebook checks to see blockers, warnings and style notes here."
          action={
            p.canValidate ? (
              <button className="primary-ghost" type="button" onClick={p.onValidate}>Validate now</button>
            ) : undefined
          }
        />
      </div>
    );
  }

  const groups = groupBySeverity(p.findings);
  const total = p.findings.length;

  return (
    <div className="rp-valpanel">
      <div className="rp-valpanel-head">
        <span className="rp-panel-title">Validation summary</span>
        {p.canValidate && (
          <button className="ghost rp-valpanel-rerun" type="button" onClick={p.onValidate}>
            <RefreshCw size={12} aria-hidden /> Re-run
          </button>
        )}
      </div>

      <div className="rp-valpanel-tiles">
        <div className="rp-valpanel-tile is-blocker">
          <span className="rp-valpanel-tile-value">{groups.blocker.length}</span>
          <span className="rp-valpanel-tile-label">Blockers</span>
        </div>
        <div className="rp-valpanel-tile is-warning">
          <span className="rp-valpanel-tile-value">{groups.warning.length}</span>
          <span className="rp-valpanel-tile-label">Warnings</span>
        </div>
        <div className="rp-valpanel-tile is-style">
          <span className="rp-valpanel-tile-value">{groups.info.length}</span>
          <span className="rp-valpanel-tile-label">Style</span>
        </div>
        <div className="rp-valpanel-tile">
          <span className="rp-valpanel-tile-value">{total}</span>
          <span className="rp-valpanel-tile-label">Total</span>
        </div>
      </div>

      {p.qualityScore !== null && (
        <div className="rp-valpanel-quality">
          <span className={`badge ${p.qualityScore >= 80 ? 'ok' : p.qualityScore >= 50 ? 'warn' : 'danger'}`}>
            Quality: {p.qualityScore}/100
          </span>
        </div>
      )}

      {total === 0 ? (
        <div className="banner ok rp-valpanel-clear">
          <ShieldCheck size={14} aria-hidden />
          <span>
            <strong>All checks passed.</strong> 0 issues found — this report is ready for review.
          </span>
        </div>
      ) : (
        (['blocker', 'warning', 'info'] as const).map((sev) =>
          groups[sev].length > 0 ? (
            <div key={sev} className="rp-valpanel-group">
              <div className="rp-severity-label">
                {sev === 'info' ? 'style' : sev} ({groups[sev].length})
              </div>
              {groups[sev].map((f, i) =>
                f.ruleId === 'ai:unsupported_claim' ? (
                  <UnsupportedClaimFinding key={`${sev}-${i}`} finding={f} />
                ) : (
                  <div key={`${sev}-${i}`} className={`finding ${sev}`}>
                    <div>{f.message}</div>
                    <div className="rule"><code>{f.ruleId}</code></div>
                    {f.section && (
                      <button
                        type="button"
                        className="rp-valpanel-jump"
                        onClick={() => p.onJumpToSection(f.section as string, sev)}
                      >
                        <CornerDownRight size={11} aria-hidden /> Jump to {f.section}
                      </button>
                    )}
                  </div>
                ),
              )}
            </div>
          ) : null,
        )
      )}

      {p.lastValidatedAt && (
        <p className="rp-valpanel-when">Last validated {p.lastValidatedAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</p>
      )}
    </div>
  );
}

function SignoffPanel(p: ReportInspectorProps) {
  return (
    <div className="rp-panel">
      <div className="rp-panel-title">
        Sign &amp; addendum
        {p.primarySigned ? (
          <span className="badge ok">Signed</span>
        ) : (
          <span className="badge warn">Unsigned</span>
        )}
      </div>

      {!p.primarySigned ? (
        <>
          <div className="section-block">
            <label>Note (optional)</label>
            <input
              className="rp-input"
              value={p.signNote}
              onChange={(e) => p.onSignNoteChange(e.target.value)}
              placeholder="e.g. preliminary read"
            />
          </div>
          <div className="rp-toolbar">
            <button className="primary" type="button" disabled={p.signBusy} onClick={p.onSignPrimary}>
              {p.signBusy ? '…' : 'Sign as Primary'}
            </button>
          </div>
        </>
      ) : (
        <>
          <div className="rp-toolbar">
            <button className="primary-ghost" type="button" disabled={p.signBusy} onClick={p.onAddCoSigner}>
              Add Co-Signer
            </button>
            <button
              className="primary-ghost"
              type="button"
              disabled={p.signBusy}
              onClick={p.onToggleAddendum}
            >
              {p.addendumOpen ? 'Cancel addendum' : 'Add Addendum'}
            </button>
          </div>
          {p.addendumOpen && (
            <div className="composer-shell">
              <textarea
                className="rp-input"
                placeholder="Addendum text…"
                value={p.addendumBody}
                onChange={(e) => p.onAddendumBodyChange(e.target.value)}
                rows={4}
              />
              <div className="section-block">
                <label>Note (optional)</label>
                <input
                  className="rp-input"
                  value={p.signNote}
                  onChange={(e) => p.onSignNoteChange(e.target.value)}
                />
              </div>
              <div className="rp-toolbar">
                <button
                  className="primary"
                  type="button"
                  disabled={p.signBusy || !p.addendumBody.trim()}
                  onClick={p.onSubmitAddendum}
                >
                  {p.signBusy ? '…' : 'Submit addendum'}
                </button>
              </div>
            </div>
          )}
        </>
      )}

      <ul className="rp-list rp-mt-sm">
        <li className="rp-row between rp-divider-row">
          <span className="rp-stat-label rp-cell f2">Radiologist</span>
          <span className="rp-stat-label rp-cell f1">Role</span>
          <span className="rp-stat-label rp-cell f1 r">Signed</span>
        </li>
        {p.signatures.length === 0 && (
          <li className="rp-page-sub rp-divider-row">No signatures yet.</li>
        )}
        {p.signatures.map((s) => (
          <li key={s.id} className="rp-row between rp-divider-row">
            <span className="rp-cell f2"><code>{s.radiologistEmail}</code></span>
            <span className="rp-cell f1">
              <span className={`badge ${roleBadge(s.role)}`}>{normalizeRole(s.role)}</span>
            </span>
            <span className="rp-cell f1 r">{fmtDateTime(s.signedAt)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
