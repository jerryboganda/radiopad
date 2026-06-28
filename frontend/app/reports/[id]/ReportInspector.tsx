'use client';

// Tabbed right-hand inspector for the report editor. Folds the former three
// stacked right-pane panels (study context, validation, sign & addendum) into
// one persistent panel with Context / Checks / Sign-off tabs so they never sit
// at uneven heights. Purely presentational — state + handlers come from props.
import type { Report, ValidationFinding, ReportTemplate, ReportSignature, CatalogItem, Rulebook } from '@/lib/api';
import {
  groupBySeverity,
  normalizeRole,
  roleBadge,
  fmtDateTime,
  UnsupportedClaimFinding,
} from './reportShared';

export type InspectorTab = 'context' | 'checks' | 'signoff';

export interface ReportInspectorProps {
  tab: InspectorTab;
  onTabChange: (t: InspectorTab) => void;

  report: Report;

  // Context
  templates: ReportTemplate[];
  // Iter-36 — admin-managed catalogs drive the modality/body-part dropdowns;
  // rulebooks are used to label the auto-matched rulebook (prompts).
  modalities: CatalogItem[];
  bodyParts: CatalogItem[];
  rulebooks: Rulebook[];
  onStudyChange: (patch: { modality?: string; bodyPart?: string; age?: number | null; gender?: string }) => void;

  // Checks
  findings: ValidationFinding[];
  qualityScore: number | null;
  blockers: number;
  canValidate: boolean;
  onValidate: () => void;

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
    { id: 'context', label: 'Context' },
    { id: 'checks', label: 'Checks' },
  ];
  if (p.canSign) tabs.push({ id: 'signoff', label: 'Sign-off' });

  return (
    <aside className="rp-inspector">
      <div className="rp-inspector-tabbar" role="tablist" aria-label="Report inspector">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={p.tab === t.id}
            className={`rp-inspector-tab${p.tab === t.id ? ' is-active' : ''}`}
            onClick={() => p.onTabChange(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="rp-inspector-body" role="tabpanel">
        {p.tab === 'context' && <ContextPanel {...p} />}
        {p.tab === 'checks' && <ChecksPanel {...p} />}
        {p.tab === 'signoff' && p.canSign && <SignoffPanel {...p} />}
      </div>
    </aside>
  );
}

function ContextPanel(p: ReportInspectorProps) {
  const study = p.report.study;
  // Modality + body part are the single selection key. They are admin-managed
  // dropdowns; changing either re-binds the matching template (scaffolding) and
  // rulebook (prompts) server-side, shown read-only below.
  const matchedTemplate = p.templates.find((t) => t.id === p.report.templateId);
  const matchedRulebook = p.rulebooks.find((r) => r.id === p.report.rulebookId);

  // Keep a PACS/legacy value selectable even if it's no longer in the catalog.
  const modalityOptions = study.modality && !p.modalities.some((m) => m.code === study.modality)
    ? [{ id: `_${study.modality}`, code: study.modality, name: study.modality, active: true, sortOrder: -1 } as CatalogItem, ...p.modalities]
    : p.modalities;
  const bodyPartOptions = study.bodyPart && !p.bodyParts.some((b) => b.code === study.bodyPart)
    ? [{ id: `_${study.bodyPart}`, code: study.bodyPart, name: study.bodyPart, active: true, sortOrder: -1 } as CatalogItem, ...p.bodyParts]
    : p.bodyParts;

  return (
    <div className="rp-panel">
      <div className="rp-panel-title">Study context</div>
      <div className="section-block">
        <label>Modality</label>
        <select
          className="rp-input"
          aria-label="Modality"
          value={study.modality}
          onChange={(e) => p.onStudyChange({ modality: e.target.value })}
        >
          <option value="">— select —</option>
          {modalityOptions.map((m) => (
            <option key={m.id} value={m.code}>{m.name || m.code}</option>
          ))}
        </select>
      </div>
      <div className="section-block">
        <label>Body part</label>
        <select
          className="rp-input"
          aria-label="Body part"
          value={study.bodyPart}
          onChange={(e) => p.onStudyChange({ bodyPart: e.target.value })}
        >
          <option value="">— select —</option>
          {bodyPartOptions.map((b) => (
            <option key={b.id} value={b.code}>{b.name || b.code}</option>
          ))}
        </select>
      </div>
      <div className="rp-row rp-gap-sm">
        <div className="section-block" style={{ flex: 1 }}>
          <label>Age</label>
          <input
            className="rp-input"
            type="number"
            min={0}
            max={150}
            aria-label="Patient age"
            value={study.age ?? ''}
            onChange={(e) => {
              const v = e.target.value;
              p.onStudyChange({ age: v === '' ? null : Number(v) });
            }}
          />
        </div>
        <div className="section-block" style={{ flex: 1 }}>
          <label>Gender</label>
          <select
            className="rp-input"
            aria-label="Patient gender"
            value={study.gender || ''}
            onChange={(e) => p.onStudyChange({ gender: e.target.value })}
          >
            <option value="">— select —</option>
            <option value="Male">Male</option>
            <option value="Female">Female</option>
            <option value="Other">Other</option>
            <option value="Unknown">Unknown</option>
          </select>
        </div>
      </div>
      <div className="section-block">
        <label>Matched template</label>
        <input className="rp-input" value={matchedTemplate ? matchedTemplate.name : '— none —'} readOnly />
      </div>
      <div className="section-block">
        <label>Matched rulebook (prompts)</label>
        <input
          className="rp-input"
          value={matchedRulebook ? `${matchedRulebook.name} · v${matchedRulebook.version}` : '— none —'}
          readOnly
        />
      </div>
    </div>
  );
}

function ChecksPanel(p: ReportInspectorProps) {
  return (
    <div className="rp-panel">
      <div className="rp-panel-title">
        Validation
        {p.qualityScore !== null && (
          <span className={`badge ${p.qualityScore >= 80 ? 'ok' : p.qualityScore >= 50 ? 'warn' : 'danger'}`}>
            Quality: {p.qualityScore}/100
          </span>
        )}
      </div>
      {p.findings.length === 0 && (
        <>
          <p style={{ color: 'var(--text-muted)' }}>Run rulebook checks to see findings here.</p>
          {p.canValidate && (
            <button className="primary-ghost rp-mt-sm" type="button" onClick={p.onValidate}>Validate now</button>
          )}
        </>
      )}
      {p.findings.length > 0 && (() => {
        const groups = groupBySeverity(p.findings);
        return (
          <>
            <div className="rp-row" style={{ gap: 6, marginBottom: 8, flexWrap: 'wrap' }}>
              {groups.blocker.length > 0 && <span className="badge danger">{groups.blocker.length} blocker{groups.blocker.length === 1 ? '' : 's'}</span>}
              {groups.warning.length > 0 && <span className="badge warn">{groups.warning.length} warning{groups.warning.length === 1 ? '' : 's'}</span>}
              {groups.info.length > 0 && <span className="badge info">{groups.info.length} info</span>}
              {p.blockers === 0 && <span className="badge ok">No blockers</span>}
            </div>
            {(['blocker', 'warning', 'info'] as const).map((sev) => groups[sev].length > 0 && (
              <div key={sev} style={{ marginTop: 8 }}>
                <div className="rp-severity-label">{sev}</div>
                {groups[sev].map((f, i) =>
                  f.ruleId === 'ai:unsupported_claim' ? (
                    <UnsupportedClaimFinding key={`${sev}-${i}`} finding={f} />
                  ) : (
                    <div key={`${sev}-${i}`} className={`finding ${sev}`}>
                      <div>{f.message}</div>
                      <div className="rule"><code>{f.ruleId}</code>{f.section ? ` · ${f.section}` : ''}</div>
                    </div>
                  ),
                )}
              </div>
            ))}
          </>
        );
      })()}
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
