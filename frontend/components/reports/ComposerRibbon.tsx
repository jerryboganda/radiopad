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
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
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
  Smartphone,
  RefreshCw,
  type LucideIcon,
} from 'lucide-react';

export type RibbonAction = 'draft' | 'impression';

// "Regenerate" runs the free-text 'custom' mode with a fixed, non-empty
// instruction the radiologist never has to type — same §5.3 fabrication guard
// as a hand-written custom edit, just without the extra step for the common
// case of "reprocess this through the (possibly newly picked) provider as-is".
const REGENERATE_INSTRUCTION =
  'Regenerate this text, preserving the exact clinical meaning, findings, measurements, laterality, ' +
  'negations, and structure — refine only wording, grammar, and flow.';

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
  /** Enabled providers available to run a rewrite against — lets the radiologist
   * re-run a rewrite through a different engine than the report's own default. */
  providers: Array<{ id: string; name: string }>;
  /** Empty string means "use the report's own provider" (`providerId`). */
  rewriteProviderId: string;
  onRewriteProviderChange: (id: string) => void;

  // Sign-off group
  canSign: boolean;
  canExport: boolean;
  showSignSend: boolean;
  onToggleSignSend: () => void;
  blockers: number;
  /**
   * Mirrors the tenant's `RequireZeroBlockers`. When false the organization
   * has deliberately turned the blocker gate off server-side, so the client
   * must not keep Acknowledge disabled — findings still show, they just stop
   * being a hard stop.
   */
  enforceBlockers: boolean;
  onAcknowledge: () => void;
  primarySigned: boolean;
  onOpenSignoff: () => void;

  // Companion pairing (RC-06 — moved from a standalone below-ribbon trigger
  // into the ribbon itself so it sits alongside the other report actions)
  pairOpen: boolean;
  onTogglePair: () => void;
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
  short,
  className = '',
  title,
  ...rest
}: {
  icon: ReactNode;
  /** Full action name — the accessible name and the hover tooltip. */
  label: string;
  /**
   * Shorter text actually painted in the button. The ribbon has to fit 13
   * actions across the composer's middle column (~800px on a 1536px screen),
   * which full names like "Generate Impression" cannot do. `label` stays the
   * accessible name via aria-label, so screen readers and getByRole queries
   * still see the real action name — only the pixels get abbreviated.
   */
  short?: string;
  'data-testid'?: string;
} & ButtonHTMLAttributes<HTMLButtonElement>) {
  // A caller-supplied title (e.g. a disabled-reason) always wins over the
  // default full-label tooltip.
  return (
    <button
      type="button"
      className={`rp-composer-ribbon-btn ${className}`.trim()}
      title={title ?? label}
      aria-label={label}
      {...rest}
    >
      <span className="rp-composer-ribbon-btn-icon" aria-hidden>{icon}</span>
      <span className="rp-composer-ribbon-btn-label">{short ?? label}</span>
    </button>
  );
}

export default function ComposerRibbon(p: ComposerRibbonProps) {
  const rewriteOpen = p.rewriteOpen;
  const setRewriteOpen = p.onRewriteOpenChange;
  const rewriteRef = useRef<HTMLDivElement | null>(null);
  const rewritePopoverRef = useRef<HTMLDivElement | null>(null);
  const [customInstruction, setCustomInstruction] = useState('');
  // The ribbon clips vertically (overflow-y: hidden, needed so the icon-only
  // collapse never grows a second row), so an absolutely-positioned popover
  // anchored inside it renders fully invisible. Portal it to <body> and
  // position it with fixed coordinates from the trigger's own rect instead.
  const [popoverPos, setPopoverPos] = useState<{ top: number; left: number } | null>(null);

  useEffect(() => {
    if (!rewriteOpen) setCustomInstruction('');
  }, [rewriteOpen]);

  useLayoutEffect(() => {
    if (!rewriteOpen) {
      setPopoverPos(null);
      return;
    }
    const place = () => {
      const rect = rewriteRef.current?.getBoundingClientRect();
      if (rect) setPopoverPos({ top: rect.bottom + 4, left: rect.left });
    };
    place();
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    return () => {
      window.removeEventListener('resize', place);
      window.removeEventListener('scroll', place, true);
    };
  }, [rewriteOpen]);

  useEffect(() => {
    if (!rewriteOpen) return;
    const onDocClick = (e: MouseEvent) => {
      const target = e.target as Node;
      const inTrigger = rewriteRef.current?.contains(target);
      const inPopover = rewritePopoverRef.current?.contains(target);
      if (!inTrigger && !inPopover) setRewriteOpen(false);
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
          short="Voice"
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
          short="Format"
          className={p.showDictationDraft ? 'active' : ''}
          aria-expanded={p.showDictationDraft}
          onClick={p.onToggleFormatDraft}
        />
        <RibbonButton
          icon={<Smartphone size={16} />}
          label="Pair phone"
          short="Phone"
          className={p.pairOpen ? 'active' : ''}
          aria-expanded={p.pairOpen}
          title="Pair your phone as a dictation companion"
          onClick={p.onTogglePair}
        />
      </RibbonGroup>

      {p.canEdit && (
        <RibbonGroup caption="AI Compose">
          <RibbonButton
            icon={busy('draft') ? <span className="rp-spinner sm" /> : <Sparkles size={16} />}
            label={busy('draft') ? 'Generating…' : 'Generate Draft'}
            short={busy('draft') ? 'Working…' : 'Draft'}
            className="primary"
            disabled={busy('draft') || !p.providerId}
            aria-busy={busy('draft')}
            onClick={p.onGenerateDraft}
          />
          <RibbonButton
            icon={busy('impression') ? <span className="rp-spinner sm" /> : <Wand2 size={16} />}
            label={busy('impression') ? 'Generating…' : 'Generate Impression'}
            short={busy('impression') ? 'Working…' : 'Impression'}
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
            {rewriteOpen && popoverPos && typeof document !== 'undefined' && createPortal(
              <div
                className="rp-rewrite-popover"
                role="menu"
                ref={rewritePopoverRef}
                style={{ position: 'fixed', top: popoverPos.top, left: popoverPos.left }}
              >
                <div className="section-block">
                  <label>Section</label>
                  <select
                    className="rp-input"
                    value={p.rewriteSection}
                    onChange={(e) => p.onRewriteSectionChange(e.target.value)}
                  >
                    <option value="full">Full report</option>
                    {p.sections.map((s) => (
                      <option key={s.key} value={s.key}>{s.label}</option>
                    ))}
                  </select>
                </div>
                {p.providers.length > 0 && (
                  <div className="section-block">
                    <label>Provider</label>
                    <select
                      className="rp-input"
                      value={p.rewriteProviderId}
                      onChange={(e) => p.onRewriteProviderChange(e.target.value)}
                    >
                      <option value="">Report default{p.providerId ? '' : ' (none set)'}</option>
                      {p.providers.map((pr) => (
                        <option key={pr.id} value={pr.id}>{pr.name}</option>
                      ))}
                    </select>
                  </div>
                )}
                <button
                  type="button"
                  className="primary"
                  disabled={p.rewriteBusy}
                  style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6, marginBottom: 10 }}
                  onClick={() => {
                    setRewriteOpen(false);
                    p.onRewrite('custom', REGENERATE_INSTRUCTION);
                  }}
                >
                  <RefreshCw size={14} aria-hidden />
                  Regenerate {p.rewriteSection === 'full' ? 'report' : 'section'}
                </button>
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
              </div>,
              document.body,
            )}
          </div>

          <RibbonButton
            icon={<PenLine size={16} />}
            label="In my style"
            short="My style"
            className={p.stylePanelOpen ? 'active' : ''}
            aria-expanded={p.stylePanelOpen}
            onClick={p.onToggleStylePanel}
          />

        </RibbonGroup>
      )}

      {showSignoffGroup && (
        <RibbonGroup caption="Sign-off">
          {p.canSign && p.canExport && (
            <RibbonButton
              icon={<Send size={16} />}
              label="Sign & send"
              short="Send"
              className={p.showSignSend ? 'active' : ''}
              aria-expanded={p.showSignSend}
              onClick={p.onToggleSignSend}
            />
          )}
          {p.canEdit && (
            <RibbonButton
              icon={<Lock size={16} />}
              label="Acknowledge & lock"
              short="Acknowledge"
              disabled={p.enforceBlockers && p.blockers > 0}
              title={p.enforceBlockers && p.blockers > 0 ? 'Resolve blockers before acknowledging' : undefined}
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
