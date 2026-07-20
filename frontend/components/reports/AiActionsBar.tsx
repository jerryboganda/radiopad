'use client';

// RC-06 AI actions bar — the row of AI verbs above the section cards.
// Every button maps to an EXISTING backend action: Generate Draft →
// POST /generate/jobs, Generate Impression → ai jobs (mode: impression),
// Re-write / Make Concise / Patient-Friendly / Referring summary → the
// rewrite endpoint's four modes, "In my style" → rewriteInMyStyle. Actions
// with no backend (e.g. Translate) are deliberately absent. The scope chip
// mirrors the rewrite target section. Provider selection happens at report
// creation (NewReportWizard) and is remembered via providerPref — this bar
// only needs to know whether a provider is resolved, not offer a picker.
import type { RewriteMode } from '@/lib/api';
import { Sparkles, Wand2, PenLine, ChevronDown } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';

export type AiBarAction =
  | 'draft'
  | 'impression'
  | 'rewrite'
  | 'concise'
  | 'patient_friendly'
  | 'referring_summary';

export interface AiActionsBarProps {
  canEdit: boolean;
  /** Which action is currently running (drives per-button spinners). */
  busyAction: AiBarAction | null;

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
}

export default function AiActionsBar(p: AiActionsBarProps) {
  const rewriteOpen = p.rewriteOpen;
  const setRewriteOpen = p.onRewriteOpenChange;
  const rewriteRef = useRef<HTMLDivElement | null>(null);
  const [customInstruction, setCustomInstruction] = useState('');

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

  const busy = (a: AiBarAction) => p.busyAction === a;
  const anyBusy = p.busyAction !== null || p.rewriteBusy;
  const scopeLabel = p.sections.find((s) => s.key === p.rewriteSection)?.label ?? p.rewriteSection;

  if (!p.canEdit) return null;

  return (
    <div className="rp-aibar" role="toolbar" aria-label="AI actions">
      <div className="rp-aibar-actions">
        <button
          className="primary rp-aibar-generate"
          type="button"
          disabled={anyBusy || !p.providerId}
          aria-busy={busy('draft')}
          onClick={p.onGenerateDraft}
        >
          {busy('draft') ? <span className="rp-spinner sm" aria-hidden /> : <Sparkles size={14} aria-hidden />}
          {busy('draft') ? 'Generating…' : 'Generate Draft'}
        </button>

        <button
          className="primary-ghost"
          type="button"
          disabled={anyBusy || !p.providerId}
          aria-busy={busy('impression')}
          onClick={p.onGenerateImpression}
        >
          {busy('impression') ? <span className="rp-spinner sm" aria-hidden /> : <Wand2 size={14} aria-hidden />}
          {busy('impression') ? 'Generating…' : 'Generate Impression'}
        </button>

        <div className="rp-rewrite-menu" ref={rewriteRef}>
          <button
            className="ghost"
            type="button"
            disabled={anyBusy}
            aria-busy={p.rewriteBusy}
            aria-haspopup="menu"
            aria-expanded={rewriteOpen}
            onClick={() => setRewriteOpen(!rewriteOpen)}
          >
            {p.rewriteBusy && <span className="rp-spinner sm" aria-hidden />}
            {p.rewriteBusy ? 'Rewriting…' : 'Re-write'}
            <ChevronDown size={13} aria-hidden />
          </button>
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
                {p.rewriteModes.map((m) => (
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
                      <span className="rp-rewrite-option-label">{m.label}</span>
                      <span className="rp-rewrite-option-hint">{m.hint}</span>
                    </button>
                  </li>
                ))}
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
                  }}
                >
                  Apply custom edit
                </button>
              </div>
            </div>
          )}
        </div>

        <button
          className="ghost"
          type="button"
          disabled={anyBusy}
          aria-busy={busy('concise')}
          onClick={() => p.onRewrite('concise')}
        >
          {busy('concise') && <span className="rp-spinner sm" aria-hidden />}
          Make Concise
        </button>
        <button
          className="ghost"
          type="button"
          disabled={anyBusy}
          aria-busy={busy('patient_friendly')}
          onClick={() => p.onRewrite('patient_friendly')}
        >
          {busy('patient_friendly') && <span className="rp-spinner sm" aria-hidden />}
          Patient-Friendly Summary
        </button>
        <button
          className="ghost"
          type="button"
          disabled={anyBusy}
          aria-busy={busy('referring_summary')}
          onClick={() => p.onRewrite('referring_summary')}
        >
          {busy('referring_summary') && <span className="rp-spinner sm" aria-hidden />}
          Referring Summary
        </button>
        <button
          className="ghost"
          type="button"
          onClick={p.onToggleStylePanel}
          aria-expanded={p.stylePanelOpen}
        >
          <PenLine size={13} aria-hidden />
          {p.stylePanelOpen ? 'Close style' : 'In my style'}
        </button>
      </div>

      <div className="rp-aibar-meta">
        <span className="rp-aibar-scope" title="Section the rewrite actions apply to">
          Scope: {scopeLabel}
        </span>
      </div>
    </div>
  );
}
