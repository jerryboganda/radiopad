# Wireframes (text)

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

> Visual wireframes live in design tooling (Figma, planned). This file uses ASCII to describe the locked layouts so they can travel with the repo.

## Dashboard `/`

```
┌─ topbar ──────────────────────────────────────────────────────────────┐
│ R  RadioPad   Dashboard | Templates | Rulebooks | Providers | Audit │
└──────────────────────────────────────────────────────────────────────┘
┌─ split ──────────────────────────────────────────────────────────────┐
│ ┌─ pane ───────────────────────────────────────────────────────────┐ │
│ │  Filters: [Modality ▾] [Status ▾] [Search _____________]  total: 132 │
│ │  ┌──────────────────────────────────────────────────────────────┐ │ │
│ │  │ Accession  Modality  Body Part   Status     Updated          │ │ │
│ │  │ 2026-...   CT        Chest       Validated  2 min ago        │ │ │
│ │  │ 2026-...   MRI       Brain       Draft      14 min ago       │ │ │
│ │  └──────────────────────────────────────────────────────────────┘ │ │
│ │  « prev   3 / 6   next »                                            │
│ └─────────────────────────────────────────────────────────────────────┘
└────────────────────────────────────────────────────────────────────────┘
```

## Report editor `/reports/:id`

```
┌─ topbar ──────────────────────────────────────────────────────────────┐
└──────────────────────────────────────────────────────────────────────┘
┌─ split ──────────────────────────────────────────────────────────────┐
│ ┌─ pane (composer) ─────────────────────┐ ┌─ pane (sidecar) ────────┐ │
│ │ § Indication ____________              │ │ Validation              │ │
│ │ § Technique  ____________              │ │  · 1 blocker            │ │
│ │ § Comparison ____________              │ │  · 2 warnings           │ │
│ │ § Findings   _________ (ai-mark)       │ │  ─────────              │ │
│ │ § Impression __________                 │ │ AI assist               │ │
│ │ § Recommendations ______               │ │  [Provider ▾]           │ │
│ │                                        │ │  [ ] contains PHI       │ │
│ │ [Validate] [Ask AI ▾] [Acknowledge] [⤓]│ │  [Ask impression]       │ │
│ └────────────────────────────────────────┘ └─────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

## Templates `/templates`

```
┌─ pane ─────────────────────────────────────────────────────────────────┐
│ Modality filter: [All ▾]                                  [+ New]      │
│  • chest-ct (CT, Chest)         · 6 sections                Edit Delete │
│  • brain-mri (MRI, Brain)       · 5 sections                Edit Delete │
│  ...                                                                    │
└────────────────────────────────────────────────────────────────────────┘
[modal] Section editor: id / label / placeholder / required (per row)
```

## Rulebooks `/rulebooks`

```
┌─ pane ─────────────────────────────────────────────────────────────────┐
│ Filters: [Status ▾]                                       [+ New]      │
│  • chest_ct_v1 v1.0.0 approved          [Validate YAML] [Deprecate]    │
│  • brain_mri_v1 v1.0.0 approved         ...                            │
│ [textarea: raw YAML]                                                   │
└────────────────────────────────────────────────────────────────────────┘
```

## Providers `/providers`

```
┌─ pane ─────────────────────────────────────────────────────────────────┐
│  • Mock         (LocalOnly)              [Test] Edit                   │
│  • Anthropic    (Sandbox)                [Test] Edit                   │
│  • Ollama       (LocalOnly)              [Test] Edit                   │
│ [+ Add provider]                                                        │
└────────────────────────────────────────────────────────────────────────┘
[modal] name / adapter / compliance / apiKeySecretRef ("env:NAME")
```
