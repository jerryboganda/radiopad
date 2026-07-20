# Unified Report Composer Ribbon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the Report Composer's two stacked toolbars (`.rp-composer-tools` inline in `ReportClient.tsx`, and the `AiActionsBar` component) into one Word-style ribbon (`ComposerRibbon`), with a suitable icon on every action and dropdown sub-item, and with the pre-existing "Make Concise / Patient-Friendly / Referring Summary" duplication (standalone buttons that duplicated Rewrite-menu entries) resolved into a single Rewrite ▾ menu.

**Architecture:** `AiActionsBar.tsx` already owns the Rewrite-popover state machine, so it's extended (not replaced) into `ComposerRibbon.tsx` — it grows three new prop groups (Review, Sign-off, on top of its existing AI Compose responsibilities) and renders three grouped clusters of icon-over-label ribbon buttons instead of one flat AI action row. `ReportClient.tsx` drops its inline toolbar JSX and passes the same handlers it already has through as props instead. No backend, permission, or state-management changes — this is a rendering/structure change only.

**Tech Stack:** Next.js (App Router) + TypeScript + Tailwind (RC design tokens) + Vitest/Testing Library, `lucide-react` icons (already a dependency, no new packages).

## Global Constraints

- Tokens only — no hardcoded hex/rgb/hsl colors in any CSS added (`docs/02-design/design.md` design-lock rule).
- Every UI change must work in both light and dark themes (`html[data-theme="dark"]` override in `tokens.css`).
- Preserve all existing behavior exactly: permission gating (`canEdit`/`canSign`/`canExport`/`canValidate`), disabled states (blockers block Acknowledge, `anyBusy` blocks AI actions while a generation/rewrite is in flight), busy/spinner states, toggled/active states, keyboard interactions (Escape closes the Rewrite popover, click-outside closes it), and all existing custom events (`radiopad:dictate`, etc.). This is a structural/visual consolidation, not a behavior change.
- Ribbon toggle buttons (Dictate, Voice cmds, Compare, Format draft, Sign & send, In my style) keep a **fixed** label at all times; the active/toggled state is shown only via the `.active` visual treatment — never by swapping label text (that would jitter a fixed-width icon-over-label button). This does NOT apply to transient busy-spinner labels (Generate Draft/Impression's "Generating…", Rewrite's "Rewriting…") or to Review & sign's persistent signed-state label swap (Review & sign ↔ Sign-off) — both of those stay dynamic exactly as today.
- The Review group (Dictate/Voice cmds/Validate/Compare/Format draft) and the Sign-off group (Sign & send/Acknowledge & lock/Review & sign) must render regardless of `canEdit` — only the AI Compose group is gated on `canEdit`. This matches current behavior, where those tools work independently of AI-drafting permission; merging everything behind one `canEdit` gate (as the old `AiActionsBar`'s top-level `if (!p.canEdit) return null` effectively would, if left as the single gate for a merged component) would be a functional regression.
- `frontend/`-only change → per DESK-001 a desktop release (`pnpm release:desktop`) is required after this merges, as the final step.

Reference: [docs/superpowers/specs/2026-07-21-composer-ribbon-design.md](../specs/2026-07-21-composer-ribbon-design.md) — approved design this plan implements.

---

### Task 1: Create `ComposerRibbon.tsx` (new component, not yet wired in)

**Files:**
- Create: `frontend/components/reports/ComposerRibbon.tsx`
- Create: `frontend/__tests__/composerRibbon.test.tsx`
- Modify: `frontend/app/radiopad.css` (additive only — new ribbon CSS + rewrite-option icon layout; nothing removed yet)

**Interfaces:**
- Produces: `ComposerRibbon` (default export) and `ComposerRibbonProps`/`RibbonAction` (named exports) from `frontend/components/reports/ComposerRibbon.tsx` — consumed by Task 2.
- This task does NOT touch `AiActionsBar.tsx` or `ReportClient.tsx`. The app keeps working exactly as before until Task 2 rewires it — `ComposerRibbon` exists but is unused by the app after this task, so nothing regresses if this task is reviewed/committed on its own.

- [ ] **Step 1: Write the failing test**

Create `frontend/__tests__/composerRibbon.test.tsx`:

```tsx
// F12 — the composer ribbon must expose a free-text "Custom edit" that fires onRewrite('custom',
// instruction). The instruction is trimmed and the button is disabled until there is one.
// Also locks down the group-gating rule: Review renders regardless of canEdit; AI Compose is
// canEdit-gated; Sign-off disappears entirely (caption included) when neither canEdit nor canSign
// is set, so no orphan group caption with zero buttons underneath it.
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import ComposerRibbon, { type ComposerRibbonProps } from '@/components/reports/ComposerRibbon';
import type { RewriteMode } from '@/lib/api';

function baseProps(onRewrite: (mode: RewriteMode, instruction?: string) => void): ComposerRibbonProps {
  return {
    dictating: false,
    onDictate: vi.fn(),
    voiceCommandMode: false,
    onToggleVoiceCommands: vi.fn(),
    canValidate: false,
    onValidate: vi.fn(),
    showPrior: false,
    onToggleCompare: vi.fn(),
    showDictationDraft: false,
    onToggleFormatDraft: vi.fn(),
    canEdit: true,
    busyAction: null,
    onGenerateDraft: vi.fn(),
    onGenerateImpression: vi.fn(),
    rewriteModes: [{ mode: 'concise', label: 'Concise', hint: 'shorter' }],
    sections: [{ key: 'impression', label: 'Impression' }],
    rewriteSection: 'impression',
    onRewriteSectionChange: vi.fn(),
    onRewrite,
    rewriteBusy: false,
    rewriteOpen: true,
    onRewriteOpenChange: vi.fn(),
    stylePanelOpen: false,
    onToggleStylePanel: vi.fn(),
    providerId: '',
    canSign: false,
    canExport: false,
    showSignSend: false,
    onToggleSignSend: vi.fn(),
    blockers: 0,
    onAcknowledge: vi.fn(),
    primarySigned: false,
    onOpenSignoff: vi.fn(),
  };
}

function renderBar(
  onRewrite: (mode: RewriteMode, instruction?: string) => void,
  overrides: Partial<ComposerRibbonProps> = {},
) {
  return render(<ComposerRibbon {...baseProps(onRewrite)} {...overrides} />);
}

describe('ComposerRibbon — custom edit (F12)', () => {
  it('fires onRewrite("custom", instruction) with the trimmed instruction', () => {
    const onRewrite = vi.fn();
    renderBar(onRewrite);

    const box = screen.getByLabelText(/custom edit/i);
    fireEvent.change(box, { target: { value: '  make the impression more concise  ' } });
    fireEvent.click(screen.getByRole('button', { name: /apply custom edit/i }));

    expect(onRewrite).toHaveBeenCalledWith('custom', 'make the impression more concise');
  });

  it('disables Apply until there is a non-empty instruction', () => {
    renderBar(vi.fn());
    const apply = screen.getByRole('button', { name: /apply custom edit/i });
    expect(apply).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/custom edit/i), { target: { value: 'x' } });
    expect(apply).not.toBeDisabled();
  });
});

describe('ComposerRibbon — group gating', () => {
  it('keeps the Review group visible even when canEdit is false', () => {
    renderBar(vi.fn(), { canEdit: false });
    expect(screen.getByRole('button', { name: /dictate/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /compare/i })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /generate draft/i })).toBeNull();
  });

  it('hides the Sign-off group entirely when neither canEdit nor canSign is set', () => {
    renderBar(vi.fn(), { canEdit: false, canSign: false });
    expect(screen.queryByText('Sign-off')).toBeNull();
    expect(screen.queryByRole('button', { name: /acknowledge . lock/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /review . sign/i })).toBeNull();
  });

  it('shows Acknowledge & lock when canEdit is true even without sign permission', () => {
    renderBar(vi.fn(), { canEdit: true, canSign: false });
    expect(screen.getByRole('button', { name: /acknowledge . lock/i })).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm vitest run frontend/__tests__/composerRibbon.test.tsx`
Expected: FAIL — `Cannot find module '@/components/reports/ComposerRibbon'` (the component doesn't exist yet).

- [ ] **Step 3: Write the component**

Create `frontend/components/reports/ComposerRibbon.tsx`:

```tsx
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
  /** Which action is currently running (drives per-button spinners). */
  busyAction: RibbonAction | null;
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
    <div className="rp-ribbon-group">
      <div className="rp-ribbon-group-items">{children}</div>
      <span className="rp-ribbon-group-caption">{caption}</span>
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
    <button type="button" className={`rp-ribbon-btn ${className}`.trim()} {...rest}>
      <span className="rp-ribbon-btn-icon" aria-hidden>{icon}</span>
      <span className="rp-ribbon-btn-label">{label}</span>
    </button>
  );
}

export default function ComposerRibbon(p: ComposerRibbonProps) {
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

  const busy = (a: RibbonAction) => p.busyAction === a;
  const anyBusy = p.busyAction !== null || p.rewriteBusy;
  const scopeLabel = p.sections.find((s) => s.key === p.rewriteSection)?.label ?? p.rewriteSection;
  const showSignoffGroup = p.canEdit || p.canSign;

  return (
    <div className="rp-ribbon" role="toolbar" aria-label="Report composer actions">
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
            disabled={anyBusy || !p.providerId}
            aria-busy={busy('draft')}
            onClick={p.onGenerateDraft}
          />
          <RibbonButton
            icon={busy('impression') ? <span className="rp-spinner sm" /> : <Wand2 size={16} />}
            label={busy('impression') ? 'Generating…' : 'Generate Impression'}
            className="primary-ghost"
            disabled={anyBusy || !p.providerId}
            aria-busy={busy('impression')}
            onClick={p.onGenerateImpression}
          />

          <div className="rp-rewrite-menu" ref={rewriteRef}>
            <RibbonButton
              icon={p.rewriteBusy ? <span className="rp-spinner sm" /> : <Edit3 size={16} />}
              label={p.rewriteBusy ? 'Rewriting…' : 'Rewrite'}
              disabled={anyBusy}
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

          <span className="rp-ribbon-scope" title="Section the rewrite actions apply to">
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
```

- [ ] **Step 4: Add the ribbon CSS**

In `frontend/app/radiopad.css`, find the existing `/* ---- AI actions bar (RC-06) ---- */` block (search for `.rp-aibar {`). Leave it untouched for now (still used by `AiActionsBar.tsx` until Task 2) and instead **append** this new block directly after it:

```css
/* ---- Composer ribbon (RC-06/RC-02 unified toolbar) ---- */
.rp-ribbon {
  display: flex;
  align-items: flex-start;
  gap: 4px;
  flex-wrap: wrap;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-xs);
  padding: 8px 10px 6px;
}
.rp-ribbon-group {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 0 10px;
}
.rp-ribbon-group + .rp-ribbon-group { border-left: 1px solid var(--border-soft); }
.rp-ribbon-group-items { display: flex; align-items: stretch; gap: 4px; flex-wrap: wrap; }
.rp-ribbon-group-caption {
  font: 600 9.5px/1.2 var(--sans);
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-faint);
}
.rp-ribbon-btn {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-width: 64px;
  padding: 8px 8px 6px;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  color: var(--text);
  cursor: pointer;
  font: inherit;
}
.rp-ribbon-btn:hover:not(:disabled) { background: var(--bg-subtle); border-color: var(--border-strong); }
.rp-ribbon-btn:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.rp-ribbon-btn:active:not(:disabled) { transform: scale(0.98); }
.rp-ribbon-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.rp-ribbon-btn.active {
  background: var(--accent-soft);
  border-color: var(--accent);
  color: var(--accent);
}
.rp-ribbon-btn.primary {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-fg);
  font-weight: 500;
}
.rp-ribbon-btn.primary:hover:not(:disabled) { background: var(--accent-hover); border-color: var(--accent-hover); }
.rp-ribbon-btn.primary-ghost {
  background: var(--bg-panel);
  border-color: var(--accent);
  color: var(--accent);
}
.rp-ribbon-btn.primary-ghost:hover:not(:disabled) { background: var(--accent-tint); }
.rp-ribbon-btn-icon { display: grid; place-items: center; height: 18px; }
.rp-ribbon-btn-label { font: 500 10.5px/1.15 var(--sans); text-align: center; white-space: nowrap; }
.rp-ribbon-scope {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  padding: 3px 10px;
  margin-top: 4px;
  border-radius: var(--radius-pill);
  background: var(--bg-subtle);
  border: 1px solid var(--border-soft);
  font: 600 11px/1.3 var(--sans);
  color: var(--text-muted);
}
```

Then find `.rp-rewrite-option > button.subtle { ... }` (search for `.rp-rewrite-option-hint`) and replace that whole rule plus the two label/hint rules — from:

```css
.rp-rewrite-option { padding: 0; margin: 0; }
.rp-rewrite-option > button.subtle {
  width: 100%;
  text-align: left;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  padding: 8px 10px;
}
.rp-rewrite-option-label { font: 500 13px/1.3 var(--sans); color: var(--text); }
.rp-rewrite-option-hint  { font: 400 12px/1.3 var(--sans); color: var(--text-muted); }
```

to:

```css
.rp-rewrite-option { padding: 0; margin: 0; }
.rp-rewrite-option > button.subtle {
  width: 100%;
  text-align: left;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 10px;
}
.rp-rewrite-option-icon { flex: none; color: var(--text-soft); margin-top: 1px; }
.rp-rewrite-option-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.rp-rewrite-option-label { font: 500 13px/1.3 var(--sans); color: var(--text); }
.rp-rewrite-option-hint  { font: 400 12px/1.3 var(--sans); color: var(--text-muted); }
```

(This is safe to change now even though `AiActionsBar.tsx` still renders the old two-span markup without the icon wrapper — the old component keeps compiling and rendering fine, just without an icon in that slot until Task 2 deletes it.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `pnpm vitest run frontend/__tests__/composerRibbon.test.tsx`
Expected: PASS — all 5 tests green.

- [ ] **Step 6: Commit**

```bash
git add frontend/components/reports/ComposerRibbon.tsx frontend/__tests__/composerRibbon.test.tsx frontend/app/radiopad.css
git commit -m "feat(reports): add unified ComposerRibbon component (not yet wired)"
```

---

### Task 2: Wire `ComposerRibbon` into `ReportClient.tsx`, remove `AiActionsBar`

**Files:**
- Modify: `frontend/app/(desktop)/reports/[id]/ReportClient.tsx`
- Delete: `frontend/components/reports/AiActionsBar.tsx`
- Delete: `frontend/__tests__/aiActionsBarCustom.test.tsx` (superseded by `composerRibbon.test.tsx` from Task 1)
- Modify: `frontend/app/radiopad.css` (remove now-dead CSS)

**Interfaces:**
- Consumes: `ComposerRibbon`, `ComposerRibbonProps`, `RibbonAction` from `frontend/components/reports/ComposerRibbon.tsx` (Task 1).
- Produces: the app's Report Composer page now renders the unified ribbon. No new exports for later tasks.

- [ ] **Step 1: Update the lucide-react icon import block**

In `frontend/app/(desktop)/reports/[id]/ReportClient.tsx`, find:

```tsx
import {
  ClipboardList,
  Settings2,
  ListChecks,
  Star,
  Lightbulb,
  Mic,
  ShieldCheck,
  GitCompareArrows,
  Lock,
  FileSignature,
} from 'lucide-react';
```

Replace with (the 5 removed icons now live inside `ComposerRibbon.tsx`; the other 5 are used elsewhere in this file and stay):

```tsx
import {
  ClipboardList,
  Settings2,
  ListChecks,
  Star,
  Lightbulb,
} from 'lucide-react';
```

- [ ] **Step 2: Swap the `AiActionsBar` import for `ComposerRibbon`**

Find:

```tsx
import AiActionsBar, { type AiBarAction } from '@/components/reports/AiActionsBar';
```

Replace with:

```tsx
import ComposerRibbon, { type RibbonAction } from '@/components/reports/ComposerRibbon';
```

- [ ] **Step 3: Rename the `busyAiAction` state type**

Find:

```tsx
const [busyAiAction, setBusyAiAction] = useState<AiBarAction | null>(null);
```

Replace with:

```tsx
const [busyAiAction, setBusyAiAction] = useState<RibbonAction | null>(null);
```

- [ ] **Step 4: Simplify `runRewrite` — drop the now-dead per-mode `busyAction` tracking**

The `barAction`/`setBusyAiAction(barAction)` computation in `runRewrite` existed only to drive the 3 standalone Make-Concise/Patient-Friendly/Referring-Summary buttons' own spinners — those buttons no longer exist (folded into the Rewrite ▾ menu in Task 1), and `rewriteBusy` already independently covers the `anyBusy` gate inside `ComposerRibbon`, so this block is dead code. Find:

```tsx
    setRewriteOpen(false);
    setRewriteBusy(true);
    const barAction: AiBarAction =
      mode === 'concise' ? 'concise'
        : mode === 'patient_friendly' ? 'patient_friendly'
          : mode === 'referring_summary' ? 'referring_summary'
            : 'rewrite';
    setBusyAiAction(barAction);
    setError(null);
```

Replace with:

```tsx
    setRewriteOpen(false);
    setRewriteBusy(true);
    setError(null);
```

- [ ] **Step 5: Remove the inline `.rp-composer-tools` toolbar and replace the `AiActionsBar` call**

Find the whole block from the `rp-composer-head` div through the end of the `<AiActionsBar ... />` call:

```tsx
        <div className="rp-composer-main">
          <div className="rp-composer-head">
            <h1 className="rp-composer-title">Report Composer</h1>
            <span className={`rp-status ${statusTone(report.status)}`}>{statusLabel(report.status)}</span>
            <div className="rp-composer-tools" role="toolbar" aria-label="Report tools">
              <button className="ghost" type="button" onClick={() => window.dispatchEvent(new CustomEvent('radiopad:dictate'))} aria-pressed={dictating}>
                <Mic size={13} aria-hidden />
                {dictating ? 'Listening…' : 'Dictate'}
              </button>
              <button
                className="ghost"
                type="button"
                onClick={() => setVoiceCommandMode((v) => !v)}
                aria-pressed={voiceCommandMode}
                data-testid="voice-command-toggle"
              >
                {voiceCommandMode ? 'Voice cmds: on' : 'Voice cmds'}
              </button>
              {canValidate && (
                <button className="ghost" type="button" onClick={validate}>
                  <ShieldCheck size={13} aria-hidden /> Validate
                </button>
              )}
              <button className="ghost" type="button" onClick={() => setShowPrior((v) => !v)} aria-expanded={showPrior}>
                <GitCompareArrows size={13} aria-hidden />
                {showPrior ? 'Hide compare' : 'Compare'}
              </button>
              <button className="ghost" type="button" onClick={() => setShowDictationDraft((v) => !v)} aria-expanded={showDictationDraft}>
                {showDictationDraft ? 'Hide draft' : 'Format draft'}
              </button>
              {/* Sign & send validates, signs and exports in one action, so it needs the same
                  permissions as the individual buttons beside it — it was the only one of the row
                  rendered unconditionally, offering a user without reports.sign an action that
                  cannot succeed while the plain "Review & sign" button correctly stayed hidden. */}
              {canSign && canExport && (
                <button className="ghost" type="button" onClick={() => setShowSignSend((v) => !v)} aria-expanded={showSignSend}>
                  {showSignSend ? 'Hide sign & send' : 'Sign & send'}
                </button>
              )}
              {canEdit && (
                <button
                  className="ghost"
                  type="button"
                  disabled={blockers > 0}
                  title={blockers > 0 ? 'Resolve blockers before acknowledging' : undefined}
                  onClick={acknowledge}
                >
                  <Lock size={13} aria-hidden /> Acknowledge &amp; lock
                </button>
              )}
              {canSign && (
                <button className="primary-ghost" type="button" onClick={() => setInspectorTab('signoff')}>
                  <FileSignature size={13} aria-hidden />
                  {primarySigned ? 'Sign-off' : 'Review & sign'}
                </button>
              )}
            </div>
          </div>

          {voiceCommandPills.length > 0 && (
            <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 8 }}>
              {voiceCommandPills.map((pill) => (
                <span key={pill.id} className="badge" data-testid="voice-command-pill">{pill.command}</span>
              ))}
            </div>
          )}

          <AiActionsBar
            canEdit={canEdit}
            busyAction={busyAiAction}
            onGenerateDraft={() => { void runGenerateDraft(); }}
            onGenerateImpression={() => { void runAi('impression'); }}
            rewriteModes={REWRITE_MODES}
            sections={REWRITABLE_KEYS.map((k) => ({
              key: k as string,
              label: SECTIONS.find((s) => s.key === k)?.label ?? (k as string),
            }))}
            rewriteSection={rewriteSection as string}
            onRewriteSectionChange={(k) => setRewriteSection(k as keyof Report)}
            onRewrite={(mode, instruction) => { void runRewrite(mode, undefined, instruction); }}
            rewriteBusy={rewriteBusy}
            rewriteOpen={rewriteOpen}
            onRewriteOpenChange={setRewriteOpen}
            stylePanelOpen={stylePanelOpen}
            onToggleStylePanel={() => setStylePanelOpen((v) => !v)}
            providerId={providerId}
          />
```

Replace with:

```tsx
        <div className="rp-composer-main">
          <div className="rp-composer-head">
            <h1 className="rp-composer-title">Report Composer</h1>
            <span className={`rp-status ${statusTone(report.status)}`}>{statusLabel(report.status)}</span>
          </div>

          {voiceCommandPills.length > 0 && (
            <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 8 }}>
              {voiceCommandPills.map((pill) => (
                <span key={pill.id} className="badge" data-testid="voice-command-pill">{pill.command}</span>
              ))}
            </div>
          )}

          <ComposerRibbon
            dictating={dictating}
            onDictate={() => window.dispatchEvent(new CustomEvent('radiopad:dictate'))}
            voiceCommandMode={voiceCommandMode}
            onToggleVoiceCommands={() => setVoiceCommandMode((v) => !v)}
            canValidate={canValidate}
            onValidate={validate}
            showPrior={showPrior}
            onToggleCompare={() => setShowPrior((v) => !v)}
            showDictationDraft={showDictationDraft}
            onToggleFormatDraft={() => setShowDictationDraft((v) => !v)}
            canEdit={canEdit}
            busyAction={busyAiAction}
            onGenerateDraft={() => { void runGenerateDraft(); }}
            onGenerateImpression={() => { void runAi('impression'); }}
            rewriteModes={REWRITE_MODES}
            sections={REWRITABLE_KEYS.map((k) => ({
              key: k as string,
              label: SECTIONS.find((s) => s.key === k)?.label ?? (k as string),
            }))}
            rewriteSection={rewriteSection as string}
            onRewriteSectionChange={(k) => setRewriteSection(k as keyof Report)}
            onRewrite={(mode, instruction) => { void runRewrite(mode, undefined, instruction); }}
            rewriteBusy={rewriteBusy}
            rewriteOpen={rewriteOpen}
            onRewriteOpenChange={setRewriteOpen}
            stylePanelOpen={stylePanelOpen}
            onToggleStylePanel={() => setStylePanelOpen((v) => !v)}
            providerId={providerId}
            canSign={canSign}
            canExport={canExport}
            showSignSend={showSignSend}
            onToggleSignSend={() => setShowSignSend((v) => !v)}
            blockers={blockers}
            onAcknowledge={acknowledge}
            primarySigned={primarySigned}
            onOpenSignoff={() => setInspectorTab('signoff')}
          />
```

(Note `canSign`, `canExport`, `showSignSend`, `blockers`, `acknowledge`, `primarySigned` were all already in scope and already used by the deleted block — this just passes the same identifiers through as props instead of inline JSX.)

- [ ] **Step 6: Delete the superseded component and test**

```bash
git rm frontend/components/reports/AiActionsBar.tsx frontend/__tests__/aiActionsBarCustom.test.tsx
```

- [ ] **Step 7: Remove now-dead CSS**

In `frontend/app/radiopad.css`:

1. Find and delete the `.rp-composer-tools` and `.rp-composer-tools > button` rules (directly after `.rp-composer-title`):

```css
.rp-composer-tools {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-left: auto;
}
.rp-composer-tools > button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
```

2. Find and delete the old `/* ---- AI actions bar (RC-06) ---- */` block (now fully superseded by the `/* ---- Composer ribbon ... ---- */` block added in Task 1):

```css
/* ---- AI actions bar (RC-06) ---- */
.rp-aibar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: var(--bg-panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-xs);
  padding: 10px 12px;
}
.rp-aibar-actions { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.rp-aibar-actions > button { display: inline-flex; align-items: center; gap: 6px; }
.rp-aibar-generate { flex: none; }
.rp-aibar-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
}
.rp-aibar-scope {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  border-radius: var(--radius-pill);
  background: var(--bg-subtle);
  border: 1px solid var(--border-soft);
  font: 600 11px/1.3 var(--sans);
  color: var(--text-muted);
}
```

- [ ] **Step 8: Typecheck and run the new + existing report tests**

Run: `pnpm vitest run frontend/__tests__/composerRibbon.test.tsx frontend/__tests__/reportGenerateFlush.test.tsx`
Expected: PASS — both files green (confirms the ribbon renders correctly standalone AND that Generate Impression still fires through `ReportClient.tsx` end-to-end via the new wiring).

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json` (single-file-scale typecheck, not a full build — flags any leftover `AiActionsBar`/`AiBarAction` references or prop mismatches)
Expected: no errors referencing `ComposerRibbon.tsx`, `ReportClient.tsx`, or the deleted `AiActionsBar.tsx`.

- [ ] **Step 9: Commit**

```bash
git add "frontend/app/(desktop)/reports/[id]/ReportClient.tsx" frontend/app/radiopad.css
git commit -m "feat(reports): wire ComposerRibbon into Report Composer, remove AiActionsBar"
```

---

### Task 3: Update the design doc (RC-06)

**Files:**
- Modify: `docs/02-design/design.md`

**Interfaces:** None — documentation only.

- [ ] **Step 1: Update the RC anatomy table row**

Find:

```
| RC-06 | AI actions bar — Generate Draft, scope chip, Route/Policy pill | AI activity rail |
```

Replace with:

```
| RC-06 | Composer ribbon — Review / AI Compose / Sign-off grouped icon buttons, Rewrite ▾ menu, scope chip | AI activity rail |
```

- [ ] **Step 2: Update the §4.12 anatomy bullet**

Find:

```
- **AI actions bar** (RC-06) — Generate Draft (blue filled) + action row,
  scope chip, Route/Policy pill, AI activity rail.
```

Replace with:

```
- **Composer ribbon** (RC-06) — unified Word-style ribbon merging report
  tools and AI actions into three icon-button groups: Review (Dictate/Voice
  cmds/Validate/Compare/Format draft), AI Compose (Generate Draft, blue
  filled/Generate Impression/Rewrite ▾ — Concise/Formal/Patient-friendly/
  Referring summary/Custom edit, each iconed/In my style/scope chip), and
  Sign-off (Sign & send/Acknowledge & lock/Review & sign). AI activity rail.
```

- [ ] **Step 3: Commit**

```bash
git add docs/02-design/design.md
git commit -m "docs: update RC-06 design doc for the unified composer ribbon"
```

---

### Task 4: Verify both themes, then ship

**Files:** None (verification + release only).

- [ ] **Step 1: Run the dev server and view the Report Composer**

Run: `cd frontend && pnpm dev` (do not run a full build — this is the allowed-locally "run the app to look at a change" case)
Open a report in the browser. Confirm:
- One ribbon renders (not two stacked bars), with 3 visible group captions (Review / AI Compose / Sign-off) separated by dividers.
- Every button has an icon above its label.
- Dictate/Voice cmds/Compare/Format draft/Sign & send/In my style show the `.active` highlight when toggled on, with their label staying fixed (no jitter).
- The Rewrite ▾ popover opens, each mode row shows its icon, and the custom-edit box + Apply button work.
- Generate Draft/Generate Impression still fire (spinner + "Generating…" while busy).

- [ ] **Step 2: Verify both light and dark themes**

Use the `verify-both-themes` skill (or manually toggle the theme switcher) to screenshot the ribbon in both `light` and `data-theme="dark"` — confirm no hardcoded colors leaked (everything should re-theme cleanly since all new CSS uses `var(--...)` tokens) and the `.active`/`.primary`/`.primary-ghost` states are legible in both.

- [ ] **Step 3: Push and cut the desktop release**

This touches `frontend/`, so per DESK-001 a desktop release is part of the task:

```bash
git push
pnpm release:desktop
```

Then stop — per project rules, do not watch or poll the resulting CI/release run; the operator monitors it.
