'use client';

// Word-style ribbon toolbar for the report editor. Purely presentational:
// every handler lives in ReportClient and is passed in as a prop. Tools are
// grouped onto four tabs (Home / Review / Export / Finalize) with Word-like
// group labels and dividers. RBAC gating mirrors the backend — a tab whose
// every control is hidden for the current role is not rendered.
import type { Report, Provider, Rulebook, RewriteMode } from '@/lib/api';
import CopyToRisButton from './CopyToRisButton';
import SearchableSelect from '@/components/ui/SearchableSelect';
import { REWRITE_MODES, REWRITABLE_KEYS, SECTIONS } from './reportShared';

export type RibbonTab = 'home' | 'review' | 'export' | 'finalize';

export type ExportFormat = 'text' | 'json' | 'fhir' | 'pdf' | 'docx';

const EXPORT_FORMATS: Array<{ fmt: ExportFormat; label: string }> = [
  { fmt: 'text', label: 'Plain text (.txt)' },
  { fmt: 'json', label: 'JSON' },
  { fmt: 'fhir', label: 'FHIR' },
  { fmt: 'pdf', label: 'PDF' },
  { fmt: 'docx', label: 'Word (.docx)' },
];

export interface ReportRibbonProps {
  tab: RibbonTab;
  onTabChange: (t: RibbonTab) => void;

  canEdit: boolean;
  canValidate: boolean;
  canExport: boolean;
  canSign: boolean;

  // Home — setup selectors
  providers: Provider[];
  providerId: string;
  onProviderChange: (id: string) => void;
  rulebooks: Rulebook[];
  rulebookId: string | null;
  onRulebookChange: (id: string | null) => void;

  // Home — AI actions
  aiBusy: boolean;
  onGenerate: () => void;
  rewriteOpen: boolean;
  onToggleRewrite: () => void;
  rewriteBusy: boolean;
  rewriteSection: keyof Report;
  onRewriteSectionChange: (k: keyof Report) => void;
  onRewrite: (mode: RewriteMode) => void;
  stylePanelOpen: boolean;
  onToggleStylePanel: () => void;
  onDictate: () => void;
  dictating: boolean;
  voiceCommandMode: boolean;
  onToggleVoiceCommand: () => void;
  voiceCommandPills: Array<{ id: number; command: string }>;

  // Review
  onValidate: () => void;
  showPrior: boolean;
  onTogglePrior: () => void;

  // Export
  reportId: string;
  exportAllowed: boolean;
  exportTitle?: string;
  exportMenuOpen: boolean;
  onToggleExportMenu: () => void;
  onCloseExportMenu: () => void;
  onExport: (fmt: ExportFormat) => void;

  // Finalize
  blockers: number;
  onAcknowledge: () => void;
  primarySigned: boolean;
  onGoToSignoff: () => void;
}

export default function ReportRibbon(props: ReportRibbonProps) {
  const { tab, onTabChange } = props;

  const tabs: Array<{ id: RibbonTab; label: string; visible: boolean }> = [
    { id: 'home', label: 'Home', visible: true },
    { id: 'review', label: 'Review', visible: true },
    { id: 'export', label: 'Export', visible: true },
    { id: 'finalize', label: 'Finalize', visible: props.canEdit || props.canSign },
  ];

  return (
    <div className="rp-ribbon">
      <div className="rp-ribbon-tabbar" role="tablist" aria-label="Report tools">
        {tabs.filter((t) => t.visible).map((t) => (
          <button
            key={t.id}
            role="tab"
            type="button"
            aria-selected={tab === t.id}
            className={`rp-ribbon-tab${tab === t.id ? ' is-active' : ''}`}
            onClick={() => onTabChange(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'home' && <HomeTab {...props} />}
      {tab === 'review' && <ReviewTab {...props} />}
      {tab === 'export' && <ExportTab {...props} />}
      {tab === 'finalize' && <FinalizeTab {...props} />}
    </div>
  );
}

function HomeTab(p: ReportRibbonProps) {
  return (
    <div className="rp-ribbon-surface rp-anim-fade-in" role="tabpanel">
      <div className="rp-ribbon-group">
        <div className="rp-ribbon-group-controls">
          <div className="rp-ribbon-field">
            <label htmlFor="rp-ribbon-provider">AI provider</label>
            <SearchableSelect
              id="rp-ribbon-provider"
              className="rp-ribbon-combobox"
              value={p.providerId}
              onChange={(v) => p.onProviderChange(v ?? '')}
              placeholder="Select provider…"
              searchPlaceholder="Search providers…"
              options={p.providers.map((pr) => ({
                value: pr.id,
                label: pr.name,
                searchText: `${pr.adapter} ${pr.model}`,
                disabled: !pr.enabled,
              }))}
            />
          </div>
          <div className="rp-ribbon-field">
            <label htmlFor="rp-ribbon-rulebook">Rulebook</label>
            <SearchableSelect
              id="rp-ribbon-rulebook"
              className="rp-ribbon-combobox"
              value={p.rulebookId}
              onChange={p.onRulebookChange}
              includeNone
              noneLabel="— none —"
              placeholder="— none —"
              searchPlaceholder="Search rulebooks…"
              options={p.rulebooks.map((rb) => ({
                value: rb.id,
                label: `${rb.name} (${rb.version})`,
                searchText: `${rb.appliesToModalities} ${rb.appliesToBodyParts}`,
              }))}
            />
          </div>
        </div>
        <div className="rp-ribbon-group-label">Setup</div>
      </div>

      {p.canEdit && <div className="rp-ribbon-divider" aria-hidden="true" />}

      {p.canEdit && (
        <div className="rp-ribbon-group">
          <div className="rp-ribbon-group-controls">
            <button className="primary" type="button" disabled={p.aiBusy || !p.providerId} aria-busy={p.aiBusy} onClick={p.onGenerate}>
              {p.aiBusy && <span className="rp-spinner sm" aria-hidden />}
              {p.aiBusy ? 'Generating…' : 'Generate impression'}
            </button>
          </div>
          <div className="rp-ribbon-group-label">Generate</div>
        </div>
      )}

      {p.canEdit && <div className="rp-ribbon-divider" aria-hidden="true" />}

      {p.canEdit && (
        <div className="rp-ribbon-group">
          <div className="rp-ribbon-group-controls">
            <div className="rp-rewrite-menu">
              <button
                className="primary-ghost"
                type="button"
                disabled={p.rewriteBusy}
                aria-busy={p.rewriteBusy}
                aria-haspopup="menu"
                aria-expanded={p.rewriteOpen}
                onClick={p.onToggleRewrite}
              >
                {p.rewriteBusy && <span className="rp-spinner sm" aria-hidden />}
                {p.rewriteBusy ? 'Rewriting…' : 'Rewrite ▾'}
              </button>
              {p.rewriteOpen && (
                <div className="rp-rewrite-popover" role="menu">
                  <div className="section-block">
                    <label>Section</label>
                    <select
                      className="rp-input"
                      value={p.rewriteSection as string}
                      onChange={(e) => p.onRewriteSectionChange(e.target.value as keyof Report)}
                    >
                      {REWRITABLE_KEYS.map((k) => (
                        <option key={k as string} value={k as string}>
                          {SECTIONS.find((s) => s.key === k)?.label ?? (k as string)}
                        </option>
                      ))}
                    </select>
                  </div>
                  <ul className="rp-list">
                    {REWRITE_MODES.map((m) => (
                      <li key={m.mode} className="rp-rewrite-option">
                        <button className="subtle" type="button" role="menuitem" onClick={() => p.onRewrite(m.mode)}>
                          <span className="rp-rewrite-option-label">{m.label}</span>
                          <span className="rp-rewrite-option-hint">{m.hint}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
            <button className="primary-ghost" type="button" onClick={p.onToggleStylePanel} aria-expanded={p.stylePanelOpen}>
              {p.stylePanelOpen ? 'Close style' : 'In my style'}
            </button>
          </div>
          <div className="rp-ribbon-group-label">Rewrite</div>
        </div>
      )}

      <div className="rp-ribbon-divider" aria-hidden="true" />

      <div className="rp-ribbon-group">
        <div className="rp-ribbon-group-controls">
          <button className="ghost" type="button" onClick={p.onDictate} aria-pressed={p.dictating}>
            {p.dictating ? 'Listening…' : 'Dictate'}
          </button>
          <button
            className="ghost"
            type="button"
            onClick={p.onToggleVoiceCommand}
            aria-pressed={p.voiceCommandMode}
            data-testid="voice-command-toggle"
          >
            {p.voiceCommandMode ? 'Voice cmds: on' : 'Voice cmds'}
          </button>
          {p.voiceCommandPills.map((pill) => (
            <span key={pill.id} className="badge" data-testid="voice-command-pill">{pill.command}</span>
          ))}
        </div>
        <div className="rp-ribbon-group-label">Dictation</div>
      </div>
    </div>
  );
}

function ReviewTab(p: ReportRibbonProps) {
  return (
    <div className="rp-ribbon-surface rp-anim-fade-in" role="tabpanel">
      <div className="rp-ribbon-group">
        <div className="rp-ribbon-group-controls">
          {p.canValidate && (
            <button className="primary-ghost" type="button" onClick={p.onValidate}>Validate</button>
          )}
          <button className="ghost" type="button" onClick={p.onTogglePrior} aria-expanded={p.showPrior}>
            {p.showPrior ? 'Hide prior' : 'Compare prior'}
          </button>
        </div>
        <div className="rp-ribbon-group-label">Quality</div>
      </div>
    </div>
  );
}

function ExportTab(p: ReportRibbonProps) {
  return (
    <div className="rp-ribbon-surface rp-anim-fade-in" role="tabpanel">
      <div className="rp-ribbon-group">
        <div className="rp-ribbon-group-controls">
          <CopyToRisButton reportId={p.reportId} />
        </div>
        <div className="rp-ribbon-group-label">Clipboard</div>
      </div>

      {p.canExport && <div className="rp-ribbon-divider" aria-hidden="true" />}

      {p.canExport && (
        <div className="rp-ribbon-group">
          <div className="rp-ribbon-group-controls">
            <div className="rp-menu">
              <button
                className="ghost"
                type="button"
                disabled={!p.exportAllowed}
                title={p.exportTitle}
                aria-haspopup="menu"
                aria-expanded={p.exportMenuOpen}
                onClick={p.onToggleExportMenu}
              >
                Export ▾
              </button>
              {p.exportMenuOpen && (
                <div className="rp-menu-popover" role="menu">
                  {EXPORT_FORMATS.map(({ fmt, label }) => (
                    <button
                      key={fmt}
                      type="button"
                      role="menuitem"
                      className="rp-menu-item"
                      disabled={!p.exportAllowed}
                      onClick={() => { p.onExport(fmt); p.onCloseExportMenu(); }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
          <div className="rp-ribbon-group-label">Export</div>
        </div>
      )}
    </div>
  );
}

function FinalizeTab(p: ReportRibbonProps) {
  return (
    <div className="rp-ribbon-surface rp-anim-fade-in" role="tabpanel">
      {p.canEdit && (
        <div className="rp-ribbon-group">
          <div className="rp-ribbon-group-controls">
            <button
              className="primary-ghost"
              type="button"
              disabled={p.blockers > 0}
              title={p.blockers > 0 ? 'Resolve blockers before acknowledging' : undefined}
              onClick={p.onAcknowledge}
            >
              Acknowledge &amp; lock
            </button>
          </div>
          <div className="rp-ribbon-group-label">Acknowledge</div>
        </div>
      )}

      {p.canEdit && p.canSign && <div className="rp-ribbon-divider" aria-hidden="true" />}

      {p.canSign && (
        <div className="rp-ribbon-group">
          <div className="rp-ribbon-group-controls">
            <button className="primary" type="button" onClick={p.onGoToSignoff}>
              {p.primarySigned ? 'Sign-off' : 'Review & sign'}
            </button>
          </div>
          <div className="rp-ribbon-group-label">Sign</div>
        </div>
      )}
    </div>
  );
}
