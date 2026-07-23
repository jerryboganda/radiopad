'use client';

// RC-06 Composer ribbon — the unified Word-style toolbar above the section
// cards. The Review group (Dictate/Voice cmds/Validate/Compare/Format draft)
// and Sign-off group (Sign & send/Acknowledge & lock/Review & sign) render
// regardless of AI-edit permission — only the AI Compose group is gated on
// canEdit, matching the pre-ribbon behavior where those tools worked
// independently of AI drafting rights. Every button maps to an EXISTING
// backend action: Generate Draft -> POST /generate/jobs, Generate Impression
// -> ai jobs (mode: impression), Rewrite modes -> the rewrite endpoint's four
// modes + custom free-text edit, "In my style" -> rewriteInMyStyle. Provider
// selection happens at report creation (NewReportWizard) and is remembered
// via providerPref -- this bar only needs to know whether a provider is
// resolved, not offer a picker.
import type { ReactNode, ButtonHTMLAttributes } from 'react';
import { useEffect, useRef, useState } from 'react';
import type { RewriteMode } from '@/lib/api';
import {
  Sparkles,
  Wand2,
  PenLine,
  Mic,
  AudioLines,
  ShieldCheck,
  GitCompareArrows,
  AlignLeft,
  Edit3,
  Minimize2,
  ScrollText,
  Smile,
  Stethoscope,
  MessageSquarePlus,
  Send,
  Lock,
  FileSignature,
  type LucideIcon,
} from 'lucide-react';

export type RibbonAction = 'draft' | 'impression';

const REWRITE_MODE_ICONS: Partial<Record<RewriteMode, LucideIcon>> = {
  concise: Minimize2,
  formal: ScrollText,
  patient_friendly: Smile,
  referring_summary: Stethoscope,
};

export interface ComposerRibbonProps {
  // Review group
  dictating: boolean;
  onDictate: () => void;
  voiceCommandMode: boolean;
  onToggleVoiceCommands: () => void;
  canValidate: boolean;
  onValidate: () => void;
  showPrior: boolean;
  onToggleCompare: () => void;
  showDictationDraft: boolean;
  onToggleFormatDraft: () => void;

  // AI Compose group
  canEdit: boolean;
  /**
   * Async actions with a non-terminal tracked job for this report (Phase 6.1)
   * — drives that button's own spinner/disabled state. Draft and Impression
   * gate independently (concurrent by design, no blanket "one AI action at a
   * time"); the rewrite family keeps its own `rewriteBusy` gate below.
   */
  activeActions: RibbonAction[];
  onGenerateDraft: () => void;
  onGenerateImpression: () => void;
  rewriteModes: Array<{ mode: RewriteMode; label: string; hint: string }>;
  sections: Array<{ key: string; label: string }>;
  rewriteSection: string;
  onRewriteSectionChange: (key: string) => void;
  /** F12 — `instruction` is supplied only for mode: 'custom' (free-text NL edit). */
  onRewrite: (mode: RewriteMode, instruction?: string) => void;
  rewriteBusy: boolean;
  /** Controlled popover state — the global `radiopad:rewrite` event opens it. */
  rewriteOpen: boolean;
  onRewriteOpenChange: (open: boolean) => void;
  stylePanelOpen: boolean;
  onToggleStylePanel: () => void;
  providerId: string;

  // Sign-off group
  canSign: boolean;
  canExport: boolean;
  showSignSend: boolean;
  onToggleSignSend: () => void;
  blockers: number;
  onAcknowledge: () => void;
  primarySigned: boolean;
  onOpenSignoff: () => void;
}

function RibbonGroup({ caption, children }: { caption: string; children: ReactNode }) {
  return (
    <div className="rp-composer-ribbon-group">
      <div className="rp-composer-ribbon-group-items">{children}</div>
      <span className="rp-composer-ribbon-group-caption">{caption}</span>
    </div>
  );
}

function RibbonButton({
  icon,
  label,
  className = '',
  ...rest
}: {
  icon: ReactNode;
  label: string;
  'data-testid'?: string;
} & ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button type="button" className={`rp-composer-ribbon-btn ${className}`.trim()} {...rest}>
      <span className="rp-composer-ribbon-btn-icon" aria-hidden>{icon}</span>
      <span className="rp-composer-ribbon-btn-label">{label}</span>
    </button>
  );
}

export default function ComposerRibbon(p: ComposerRibbonProps) {
  const rewriteOpen = p.rewriteOpen;
  const setRewriteOpen = p.onRewriteOpenChange;
  const rewriteRef = useRef<HTMLDivElement | null>(null);
  const [customInstruction, setCustomInstruction] = useState('');

  useEffect(() => {
    if (!rewriteOpen) setCustomInstruction('');
  }, [rewriteOpen]);

  useEffect(() => {
    if (!rewriteOpen) return;
    const onDocClick = (e: MouseEvent) => {
      if (rewriteRef.current && !rewriteRef.current.contains(e.target as Node)) setRewriteOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setRewriteOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [rewriteOpen, setRewriteOpen]);

  const busy = (a: RibbonAction) => p.activeActions.includes(a);
  const scopeLabel = p.sections.find((s) => s.key === p.rewriteSection)?.label ?? p.rewriteSection;
  const showSignoffGroup = p.canEdit || p.canSign;

  return (
    <div className="rp-composer-ribbon" role="toolbar" aria-label="Report composer actions">
      <RibbonGroup caption="Review">
        <RibbonButton
          icon={<Mic size={16} />}
          label="Dictate"
          className={p.dictating ? 'active' : ''}
          aria-pressed={p.dictating}
          onClick={p.onDictate}
        />
        <RibbonButton
          icon={<AudioLines size={16} />}
          label="Voice cmds"
          className={p.voiceCommandMode ? 'active' : ''}
          aria-pressed={p.voiceCommandMode}
          data-testid="voice-command-toggle"
          onClick={p.onToggleVoiceCommands}
        />
        {p.canValidate && (
          <RibbonButton icon={<ShieldCheck size={16} />} label="Validate" onClick={p.onValidate} />
        )}
        <RibbonButton
          icon={<GitCompareArrows size={16} />}
          label="Compare"
          className={p.showPrior ? 'active' : ''}
          aria-expanded={p.showPrior}
          onClick={p.onToggleCompare}
        />
        <RibbonButton
          icon={<AlignLeft size={16} />}
          label="Format draft"
          className={p.showDictationDraft ? 'active' : ''}
          aria-expanded={p.showDictationDraft}
          onClick={p.onToggleFormatDraft}
        />
      </RibbonGroup>

      {p.canEdit && (
        <RibbonGroup caption="AI Compose">
          <RibbonButton
            icon={busy('draft') ? <span className="rp-spinner sm" /> : <Sparkles size={16} />}
            label={busy('draft') ? 'Generating…' : 'Generate Draft'}
            className="primary"
            disabled={busy('draft') || !p.providerId}
            aria-busy={busy('draft')}
            onClick={p.onGenerateDraft}
          />
          <RibbonButton
            icon={busy('impression') ? <span className="rp-spinner sm" /> : <Wand2 size={16} />}
            label={busy('impression') ? 'Generating…' : 'Generate Impression'}
            className="primary-ghost"
            disabled={busy('impression') || !p.providerId}
            aria-busy={busy('impression')}
            onClick={p.onGenerateImpression}
          />

          <div className="rp-rewrite-menu" ref={rewriteRef}>
            <RibbonButton
              icon={p.rewriteBusy ? <span className="rp-spinner sm" /> : <Edit3 size={16} />}
              label={p.rewriteBusy ? 'Rewriting…' : 'Rewrite'}
              disabled={p.rewriteBusy}
              aria-haspopup="menu"
              aria-expanded={rewriteOpen}
              onClick={() => setRewriteOpen(!rewriteOpen)}
            />
            {rewriteOpen && (
              <div className="rp-rewrite-popover" role="menu">
                <div className="section-block">
                  <label>Section</label>
                  <select
                    className="rp-input"
                    value={p.rewriteSection}
                    onChange={(e) => p.onRewriteSectionChange(e.target.value)}
                  >
                    {p.sections.map((s) => (
                      <option key={s.key} value={s.key}>{s.label}</option>
                    ))}
                  </select>
                </div>
                <ul className="rp-list">
                  {p.rewriteModes.map((m) => {
                    const Icon = REWRITE_MODE_ICONS[m.mode] ?? Edit3;
                    return (
                      <li key={m.mode} className="rp-rewrite-option">
                        <button
                          className="subtle"
                          type="button"
                          role="menuitem"
                          onClick={() => {
                            setRewriteOpen(false);
                            p.onRewrite(m.mode);
                          }}
                        >
                          <span className="rp-rewrite-option-icon" aria-hidden><Icon size={15} /></span>
                          <span className="rp-rewrite-option-text">
                            <span className="rp-rewrite-option-label">{m.label}</span>
                            <span className="rp-rewrite-option-hint">{m.hint}</span>
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>

                {/* F12 — free-text natural-language edit. The backend hard-guards this with the §5.3
                    fabrication check, so it can rephrase but can't invent a measurement/number/date. */}
                <div className="section-block" style={{ marginTop: 8 }}>
                  <label htmlFor="rp-custom-rewrite">Custom edit</label>
                  <textarea
                    id="rp-custom-rewrite"
                    className="rp-input"
                    rows={2}
                    placeholder="e.g. make the impression more concise and add a 6-month follow-up recommendation"
                    value={customInstruction}
                    onChange={(e) => setCustomInstruction(e.target.value)}
                    style={{ width: '100%', resize: 'vertical' }}
                  />
                  <button
                    type="button"
                    className="primary"
                    disabled={p.rewriteBusy || customInstruction.trim().length === 0}
                    style={{ marginTop: 6 }}
                    onClick={() => {
                      const instruction = customInstruction.trim();
                      if (!instruction) return;
                      setRewriteOpen(false);
                      p.onRewrite('custom', instruction);
                      setCustomInstruction('');
                    }}
                  >
                    <MessageSquarePlus size={13} aria-hidden /> Apply custom edit
                  </button>
                </div>
              </div>
            )}
          </div>

          <RibbonButton
            icon={<PenLine size={16} />}
            label="In my style"
            className={p.stylePanelOpen ? 'active' : ''}
            aria-expanded={p.stylePanelOpen}
            onClick={p.onToggleStylePanel}
          />

          <span className="rp-composer-ribbon-scope" title="Section the rewrite actions apply to">
            Scope: {scopeLabel}
          </span>
        </RibbonGroup>
      )}

      {showSignoffGroup && (
        <RibbonGroup caption="Sign-off">
          {p.canSign && p.canExport && (
            <RibbonButton
              icon={<Send size={16} />}
              label="Sign & send"
              className={p.showSignSend ? 'active' : ''}
              aria-expanded={p.showSignSend}
              onClick={p.onToggleSignSend}
            />
          )}
          {p.canEdit && (
            <RibbonButton
              icon={<Lock size={16} />}
              label="Acknowledge & lock"
              disabled={p.blockers > 0}
              title={p.blockers > 0 ? 'Resolve blockers before acknowledging' : undefined}
              onClick={p.onAcknowledge}
            />
          )}
          {p.canSign && (
            <RibbonButton
              icon={<FileSignature size={16} />}
              label={p.primarySigned ? 'Sign-off' : 'Review & sign'}
              className="primary-ghost"
              onClick={p.onOpenSignoff}
            />
          )}
        </RibbonGroup>
      )}
    </div>
  );
}
