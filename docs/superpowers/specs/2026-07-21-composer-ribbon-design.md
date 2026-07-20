# Unified Report Composer ribbon — design

**Date:** 2026-07-21
**Status:** Approved by operator, pending implementation plan

## Problem

The Report Composer header currently stacks two separate toolbars:

1. `.rp-composer-tools` — inline JSX in [ReportClient.tsx](../../../frontend/app/(desktop)/reports/[id]/ReportClient.tsx): Dictate, Voice cmds, Validate, Compare, Format draft, Sign & send, Acknowledge & lock, Review & sign. Several of these buttons (Voice cmds, Format draft, Sign & send) have no icon.
2. [AiActionsBar.tsx](../../../frontend/components/reports/AiActionsBar.tsx) (RC-06) — Generate Draft, Generate Impression, Re-write ▾, Make Concise, Patient-Friendly Summary, Referring Summary, In my style, plus a Scope chip.

This reads as two disconnected bars rather than one coherent toolbar, and has an existing redundancy bug: "Make Concise", "Patient-Friendly Summary", and "Referring Summary" exist **both** as standalone buttons and as entries inside the "Re-write ▾" dropdown (which also has a 4th mode, "Formal", found nowhere else).

The operator asked for these to be unified into one Microsoft-Word-style ribbon toolbar, with icons on every item and sub-item.

## Goals

- One visual ribbon replacing both bars, grouped like a Word ribbon (icon-over-label buttons, grouped clusters with captions, divider lines).
- Every action — including dropdown sub-items (rewrite modes) — gets a suitable icon.
- Resolve the Rewrite duplication: fold Concise/Patient-friendly/Referring (+ the orphaned Formal mode) into a single "Rewrite ▾" menu; remove the 3 standalone duplicate buttons.
- Preserve all existing behavior exactly: permission gating (`canEdit`/`canSign`/`canExport`/`canValidate`), disabled states (blockers block Acknowledge, `anyBusy` blocks AI actions), busy/spinner states, toggled/active states (Dictate listening, Voice cmds on, Compare/Format draft/Sign & send panels open), keyboard interactions (Escape closes Rewrite popover, click-outside), and all existing custom events (`radiopad:dictate`, `radiopad:rewrite`, etc.).
- Both light and dark themes, tokens only — no hardcoded colors.

## Non-goals

- The "Pair phone" / `CompanionHostPanel` row stays a separate section below the ribbon — it's a different feature (companion device pairing), not a report-composing action.
- No change to backend behavior, permissions logic, or the actions each button performs — this is a structural/visual consolidation only.
- No change to the Export panel (RC-09) or other inspector tabs.

## Ribbon groups

Three groups, left to right, each with a small uppercase caption underneath and a 1px `--border-soft` divider between groups:

| Group | Items |
|---|---|
| **Review** | Dictate · Voice cmds · Validate · Compare · Format draft |
| **AI Compose** | Generate Draft · Generate Impression · Rewrite ▾ (Concise / Formal / Patient-friendly / Referring summary / Custom edit) · In my style · *Scope: {section}* indicator (read-only chip, not a button) |
| **Sign-off** | Sign & send · Acknowledge & lock · Review & sign |

Conditional rendering (`canValidate`, `canSign && canExport`, `canEdit`, `canSign`) is preserved exactly as today, just applied to ribbon buttons instead of the old flat-row buttons.

## Icons (lucide-react — already the project's icon library)

Existing icons carry over unchanged: Dictate `Mic`, Validate `ShieldCheck`, Compare `GitCompareArrows`, Acknowledge & lock `Lock`, Review & sign `FileSignature`, Generate Draft `Sparkles`, Generate Impression `Wand2`, In my style `PenLine`.

New icons added for items that currently have none:

| Item | Icon | Rationale |
|---|---|---|
| Voice cmds | `AudioLines` | Distinct from Dictate's `Mic`; reads as "listening for commands" |
| Format draft | `AlignLeft` | Text-formatting glyph |
| Sign & send | `Send` | Sign + transmit in one action |
| Rewrite ▾ (top-level) | `Edit3` | Distinct from Generate Impression's `Wand2` |
| → Concise | `Minimize2` | Shrink/compress |
| → Formal | `ScrollText` | Formal register |
| → Patient-friendly | `Smile` | Patient-facing tone |
| → Referring summary | `Stethoscope` | Referring clinician |
| → Custom edit | `MessageSquarePlus` | Free-text instruction |

## Visual anatomy

**Ribbon button:** icon centered above a small label (~15px icon, ~10.5px label), auto-width with a sensible min-width, roughly 60–68px tall. Default state reuses `.ghost` tokens (`--bg-panel` bg, `--border`, hover → `--bg-subtle`/`--border-strong`). Toggled/active states (Dictate listening, Voice cmds on, Compare/Format draft/Sign & send panels open) reuse the existing `.ghost.active` treatment (`--accent-soft` bg, `--accent` border/text — same pattern already used for STT mode and hotkey filters elsewhere in the app). Generate Draft keeps its `.primary` filled-accent treatment as the one visually "loud" button, matching its current role as the primary action. Busy/spinner and disabled states are unchanged (`aria-busy`, `disabled`, existing `.rp-spinner` markup).

**Groups:** flex clusters with a small uppercase caption underneath (`--text-faint`, ~9.5px, letter-spacing) — e.g. "REVIEW", "AI COMPOSE", "SIGN-OFF" — separated by a 1px `--border-soft` vertical divider. The whole ribbon wraps on narrow widths via `flex-wrap`, same as today's bars.

**Rewrite ▾:** keeps its existing popover state machine (open/close on click, click-outside-to-close, Escape-to-close) from `AiActionsBar`, just restyled as a ribbon button trigger. Each mode item in the popover list gets its icon prepended before the label/hint text. The custom free-text edit box and Apply button are unchanged.

**Scope: {section} chip:** stays a small read-only status chip (current `.rp-aibar-scope` styling), placed inline within the AI Compose group next to Rewrite ▾.

**Toggle labels stay fixed-width.** Today, Dictate's label swaps to "Listening…" and Voice cmds' swaps to "Voice cmds: on" while active — fine for flat pill buttons, but a stacked icon-over-short-label ribbon button would jitter in width as the label changes length. Ribbon buttons keep a fixed short label (`Dictate`, `Voice cmds`) at all times; the toggled/active state is conveyed purely through the `.active` visual treatment (accent bg/border/text), same as Compare/Format draft/Sign & send already do today. This does not apply to transient busy states (Generate Draft/Impression's "Generating…" + spinner, Acknowledge's spinner) — those stay as-is since they're temporary loading feedback, not a persistent toggle relabel.

## Component architecture

Extend the existing `AiActionsBar.tsx` — which already owns the Rewrite-popover state machine — to also render the Review and Sign-off groups, and rename it to `ComposerRibbon.tsx` since it's no longer AI-only. New props needed (all wiring/handlers already exist in `ReportClient.tsx`, just currently inlined as JSX instead of passed as props):

```ts
// Review group
dictating: boolean;
onDictate: () => void;               // dispatches radiopad:dictate (unchanged)
voiceCommandMode: boolean;
onToggleVoiceCommands: () => void;
canValidate: boolean;
onValidate: () => void;
showPrior: boolean;
onToggleCompare: () => void;
showDictationDraft: boolean;
onToggleFormatDraft: () => void;

// Sign-off group
canSign: boolean;
canExport: boolean;
showSignSend: boolean;
onToggleSignSend: () => void;
canEdit: boolean;                     // already exists (gates whole bar today)
blockers: number;                     // already available in ReportClient
onAcknowledge: () => void;
primarySigned: boolean;
onOpenSignoff: () => void;            // () => setInspectorTab('signoff')
```

`ReportClient.tsx` drops its inline `.rp-composer-tools` JSX block (lines ~1186–1238) and passes these handlers through to `<ComposerRibbon>` instead. The 3 duplicate rewrite-mode buttons (`Make Concise`, `Patient-Friendly Summary`, `Referring Summary`) and their `onRewrite` wiring are removed as standalone buttons — the same `onRewrite` callback moves entirely into the Rewrite ▾ popover's mode list (which already calls it).

## Docs & tests to update alongside

- [docs/02-design/design.md](../../02-design/design.md) §4.12 and the RC-06 table row — currently describes "AI actions bar — Generate Draft, scope chip, Route/Policy pill" (the Route/Policy pill was already removed in a prior change) and doesn't mention the merged Review/Sign-off groups. Update in the same change per the project's docs rule.
- `frontend/__tests__/aiActionsBarCustom.test.tsx` — rename/update for the new `ComposerRibbon` props (will need the new Review/Sign-off props added to its render call, even if just no-ops for that test's scenario).
- Any other test referencing `.rp-composer-tools` button roles/labels by text — grep before implementing.

## Rollout

This touches `frontend/`, so per DESK-001 a desktop release is required after merging. Both light and dark themes must be verified (via the `verify-both-themes` skill) before considering this done, per the project's mandatory design-lock rule.
