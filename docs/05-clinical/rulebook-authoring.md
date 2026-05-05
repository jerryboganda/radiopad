# Authoring rulebooks

**Status:** Current  ·  **Owner:** Clinical  ·  **Last Updated:** 2026-05-05

A **rulebook** is a YAML document that tells the validation engine which structural and clinical checks to apply to a given study type. Rulebooks live under [rulebooks/](../../rulebooks/) at repo root and are loaded into the database on first run by `DevSeed`.

> Approved rulebooks are clinical content. They MUST go through human review (see [the human-review file list in AGENTS.md](../../AGENTS.md)). Update tests in the same PR.

## Required top-level fields

| Field | Purpose |
| --- | --- |
| `rulebook_id` | Stable snake_case id. Never change once published. |
| `name` | Human label. |
| `version` | Semver. Increment on every published change. |
| `owner` | The committee or author responsible. |
| `status` | `draft`, `in_review`, `approved`, or `deprecated`. |
| `applies_to.modalities` | List (e.g. `[CT, MRI]`). |
| `applies_to.body_parts` | List of body part strings. |
| `applies_to.report_types` | E.g. `[diagnostic, follow_up, screening]`. |
| `style.tone` | Free-form; the prompt blocks may reference it. |
| `style.impression_max_bullets` | Drives the `impression_bullet_count` rule. |
| `style.avoid_terms` | Drives the `avoid_terms` rule. |
| `style.approved_followups` | **Iter-32 AI-008.** Allow-list of follow-up phrases. The `unauthorized_followup` validator rule emits a Warning per Recommendation line that doesn't appear in this list (case-insensitive trim equality). `ReportingService.SuggestFollowUpAsync` also drops AI suggestions absent from the allow-list and audits a `PolicyViolation` with only a SHA-256 `suggestionHash`, never the rejected prose. Leaving the field empty disables the check entirely. |
| `required_sections` | Sections the report must contain. |
| `rules` | The actual validation rules — see below. |
| `prompt_blocks` | `system`, `findings_to_impression`, `cleanup`, and **iter-32** `dictation_cleanup` (drives `POST /api/reports/{id}/dictation/cleanup`). |

## Built-in rules

The engine recognises these rule ids out of the box. Add or omit them per rulebook:

| Rule id | Severity (recommended) | What it checks |
| --- | --- | --- |
| `laterality_consistency`   | `blocker`  | Left/right mentions agree across Findings and Impression. |
| `level_consistency`        | `blocker`  | Spine pathology levels tied to disc protrusion, stenosis, foraminal narrowing, or facet arthropathy agree between Findings and Impression. |
| `measurement_consistency`  | `warning`  | Numeric measurements match between sections. |
| `negation_conflict`        | `blocker`  | A finding denied in Findings is not asserted in Impression. |
| `modality_mismatch`        | `warning`  | No CT/X-ray cross-talk in an MRI report (etc.). |
| `impression_bullet_count`  | `warning`  | Bullets ≤ `style.impression_max_bullets`. |
| `critical_result_language` | `blocker`  | Critical findings (cord compression, perforation, …) require documented communication. |
| `birads_category_required` | `blocker` | BI-RADS mammography impressions must state a final category. |
| `birads_assessment_in_impression` | `warning` | BI-RADS category should appear on its own impression line or bullet. |
| `lungrads_category_required` | `blocker` | Lung cancer screening CT impressions must state a Lung-RADS category. |
| `nodule_dimensions_required` | `warning` | Asserted pulmonary nodules must include dimensions tied to the nodule clause. |
| `lirads_category_required` | `warning` | At-risk liver studies must state a LI-RADS category. |
| `lirads_observation_size_required` | `warning` | Asserted focal liver observations must include dimensions tied to the observation clause. |
| `pirads_category_required` | `blocker` | Prostate MRI impressions must state a PI-RADS category. |
| `index_lesion_localized` | `warning` | Prostate index lesions must be localized by zone/sector or anatomic position. |
| `avoid_terms`              | `warning`  | Flags style.avoid_terms occurrences. |
| `unauthorized_followup`    | `warning`  | **Iter-32 AI-008.** Each non-empty line in `Recommendations` must appear in `style.approved_followups`. |

Every rule needs `id`, `severity` (`info` / `warning` / `blocker`), and a `description`.

## Golden cases

Every approved rulebook MUST have at least one passing golden case under [rulebooks/_tests/<rulebook_id>/](../../rulebooks/_tests/). Each case is a JSON file:

```jsonc
{
  "name": "human-readable label",
  "report": { /* a Report payload — same shape the API accepts */ },
  "expectFlagged": ["rule_id_a", "rule_id_b"]    // empty array for clean cases
}
```

Run them locally with the CLI:

```powershell
dotnet run --project cli/RadioPad.Cli -- rulebook test rulebooks/chest_ct_v1.yaml --cases rulebooks/_tests/chest_ct_v1
```

Golden cases are strict: a case fails when an expected rule id is missing or when the validator emits an unexpected rule id. CI runs every suite on every PR.

## Promotion workflow

1. Draft the rulebook + at least one golden case in a single PR.
2. Push for review by the owning committee.
3. Once merged, run `radiopad rulebook approve --id <guid>` from a tenant admin shell. The action is audited as `RulebookApproved`.
4. To retire: `radiopad rulebook deprecate --id <guid>`. Existing reports retain their snapshot; new reports use the next-approved version.

## Bundled rulebooks

The repository ships the following approved rulebooks under [rulebooks/](../../rulebooks/). Each has at least one passing golden-case suite under `rulebooks/_tests/<rulebook_id>/`.

| Rulebook id | Modality | Body part | Status | Iter | Notes |
| --- | --- | --- | --- | --- | --- |
| `chest_ct_v1` | CT | Chest | approved | 1 | Foundational rulebook. |
| `brain_mri_v1` | MRI | Brain | approved | 2 | Disc-level + laterality checks. |
| `cardiac_mri_v1` | MRI | Heart | approved | 30 | LV/RV size + ejection fraction wording. |
| `mammography_v1` | Mammography | Breast | approved | 30 | BI-RADS terminology refs (`terminology_refs:` block). |
| `paediatric_chest_xray_v1` | XR | Chest | approved | 30 | Age-appropriate phrasing; cardiothoracic ratio. |
| `liver_mri_v1` | MRI | Liver | approved | 30 | LI-RADS terminology refs (`terminology_refs:` block). |
| `mammo_birads_v1` | MG | Breast | approved | 36 | BI-RADS final-assessment categories (0, 1, 2, 3, 4A, 4B, 4C, 5, 6) with `output_schema.birads_category`. |
| `lung_lungrads_v1` | CT | Chest / Lung | approved | 36 | Lung-RADS screening categories (0, 1, 2, 3, 4A, 4B, 4X) with `output_schema.lungrads_category`. |
| `liver_lirads_v1` | MRI / CT | Liver | approved | 36 | LI-RADS observation categories (LR-1..5, LR-M, LR-TIV, LR-NC) with `output_schema.lirads_category`. |
| `prostate_pirads_v1` | MRI | Prostate | approved | 36 | PI-RADS v2.1 categories (1-5) with `output_schema.pirads_category`; sector / zone localisation rule. |
| `chest_xray_v1` | XR | Chest | approved | 36 | Adult chest x-ray template; no RADS field; `modality_mismatch` warning catches CT / MRI / US cross-talk. |

### `terminology_refs:` block (Iter-30)

Rulebooks that lean on standardised terminology declare references the engine
can resolve at validation time. Both ACR RADS systems and the curated RadLex
subset are addressable via `/api/terminology/...`:

```yaml
terminology_refs:
  rads:
    - system: bi_rads
      categories: ["1", "2", "3", "4", "5", "6"]
  radlex:
    - rid: RID1301
      preferredLabel: "lung"
```

The terminology API exposes these references for authoring and review. Runtime
RADS enforcement is implemented by explicit rule ids such as
`birads_category_required`, `lungrads_category_required`,
`lirads_category_required`, and `pirads_category_required`; the validator does
not run a general JSON-schema or terminology-ref engine for arbitrary custom
refs. RadLex® is a registered trademark of RSNA; only a curated subset is
bundled with RadioPad.

## Iteration 30 additions

Iteration 30 added four new approved rulebooks. Each ships at least one
passing golden case under [rulebooks/_tests/<rulebook_id>/](../../rulebooks/_tests/);
CI runs all suites on every PR.

| Rulebook id | YAML source | Golden cases | Iter-30 notes |
| --- | --- | --- | --- |
| `cardiac_mri_v1` | [rulebooks/cardiac_mri_v1.yaml](../../rulebooks/cardiac_mri_v1.yaml) | [rulebooks/_tests/cardiac_mri_v1/](../../rulebooks/_tests/cardiac_mri_v1/) | LV/RV size and ejection-fraction phrasing; reuses `measurement_consistency` and `impression_bullet_count`. |
| `mammography_v1` | [rulebooks/mammography_v1.yaml](../../rulebooks/mammography_v1.yaml) | [rulebooks/_tests/mammography_v1/](../../rulebooks/_tests/mammography_v1/) | BI-RADS terminology refs via the `terminology_refs:` block (`system: bi_rads`). |
| `paediatric_chest_xray_v1` | [rulebooks/paediatric_chest_xray_v1.yaml](../../rulebooks/paediatric_chest_xray_v1.yaml) | [rulebooks/_tests/paediatric_chest_xray_v1/](../../rulebooks/_tests/paediatric_chest_xray_v1/) | Age-appropriate phrasing; cardiothoracic-ratio guidance via `style.avoid_terms`. |
| `liver_mri_v1` | [rulebooks/liver_mri_v1.yaml](../../rulebooks/liver_mri_v1.yaml) | [rulebooks/_tests/liver_mri_v1/](../../rulebooks/_tests/liver_mri_v1/) | LI-RADS terminology refs (`system: li_rads`); pairs with the curated RadLex subset. |

These rulebooks are tracked in the regulatory dossier under
[docs/09-regulatory/traceability-matrix.md](../09-regulatory/traceability-matrix.md)
(RB-006 modality / subspecialty coverage).

## Iteration 32 additions

### Rulebook inheritance (RB-007)

RadioPad resolves the rulebook for a given report through a four-level chain:

1. **User-level prompt overrides** — `PromptOverride` rows (iter-31 AI-009) replace specific rulebook prompt blocks (`system`, `impression`, `dictation_cleanup`, `follow_up`, …). The override is keyed by `(tenantId, rulebookId, blockKey)`.
2. **Department-level rulebooks** — when both `Report.DepartmentTag` and a sibling `Rulebook.DepartmentTag` (same `RulebookId`) match (case-insensitive), the department-scoped row wins. This lets neuro / msk / cardiac sub-teams override the tenant-wide rulebook without forking it.
3. **Tenant-level rulebook** — the row pinned by `Report.RulebookId` (a tenant-scoped row, never crossing tenants).
4. **Built-in YAML rulebooks** — the seed under [rulebooks/](../../rulebooks/) loaded at first run.

Resolution lives in [`ReportingService.ResolveRulebookEntityAsync`](../../backend/RadioPad.Api/src/RadioPad.Application/Services/ReportingService.cs) and the prompt-block layer is composed by `RulebookSpec.WithPromptOverrides`. Inheritance never crosses tenant boundaries.

### Rollback UI (RB-008)

The rulebook detail page (`/rulebooks/[id]`) now lists prior approved versions of the same `rulebookId` in a dropdown and POSTs the chosen version to `POST /api/rulebooks/{id}/rollback`. The endpoint materialises a new approved row whose version is `<prior>+rollback-<timestamp>` — historical rows are never mutated.

### Visual rulebook editor (RB-002)

The rulebook detail page is now tabbed: a YAML source mode (existing) and a Visual mode that renders `required_sections`, `style.avoid_terms`, `style.approved_followups`, `rules` (id / severity / description), and `prompt_blocks` keys. The visual mode is read-only — edits round-trip through the YAML source and `RulebookSpec.FromYaml` server-side validation.

## Iteration 32 additions

### Rulebook inheritance (RB-007)

RadioPad resolves the rulebook for a given report through a four-level chain:

1. **User-level prompt overrides** — PromptOverride rows (iter-31 AI-009) replace specific rulebook prompt blocks (system, impression, dictation_cleanup, ollow_up, …). The override is keyed by (tenantId, rulebookId, blockKey).
2. **Department-level rulebooks** — when both `Report.DepartmentTag` and a sibling `Rulebook.DepartmentTag` (same `RulebookId`) match (case-insensitive), the department-scoped row wins. This lets neuro / msk / cardiac sub-teams override the tenant-wide rulebook without forking it.
3. **Tenant-level rulebook** — the row pinned by `Report.RulebookId` (a tenant-scoped row, never crossing tenants).
4. **Built-in YAML rulebooks** — the seed under [rulebooks/](../../rulebooks/) loaded at first run.

Resolution lives in [ReportingService.ResolveRulebookEntityAsync](../../backend/RadioPad.Api/src/RadioPad.Application/Services/ReportingService.cs) and the prompt-block layer is composed by `RulebookSpec.WithPromptOverrides`. Inheritance never crosses tenant boundaries.

### Rollback UI (RB-008)

The rulebook detail page (`/rulebooks/[id]`) now lists prior approved versions of the same `rulebookId` in a dropdown and POSTs the chosen version to `POST /api/rulebooks/{id}/rollback`. The endpoint materialises a new approved row whose version is `<prior>+rollback-<timestamp>` — historical rows are never mutated.

### Visual rulebook editor (RB-002)

The rulebook detail page is now tabbed: a YAML source mode (existing) and a Visual mode that renders `required_sections`, `style.avoid_terms`, `style.approved_followups`, `rules` (id / severity / description), and `prompt_blocks` keys. The visual mode is read-only — edits round-trip through the YAML source and RulebookSpec.FromYaml server-side validation.


## Validation packs (Iter-35)

A **validation pack** is a tenant-scoped, versioned bundle of golden test
cases use `{report, expectFlagged}` objects matching the on-disk format
already used under [rulebooks/_tests/<rulebook_id>/](../../rulebooks/_tests/).
Packs let an organisation freeze a clinical certification suite alongside
each rulebook version and re-run it on demand from the admin UI, the API,
or the CLI. Lifecycle: **Draft ? Approved ? Deprecated** (terminal).

- Packs are surfaced from the API at `/api/validation-packs` (see
  [api-reference.md](../03-architecture/api-reference.md#validation-packs-iter-35)).
- A Medical Director (or IT Admin) approves a Draft pack to mark it as
  the certification suite for a rulebook version. Re-approving a
  Deprecated pack is rejected with `409 kind:"validation_packs"`.
- `POST /api/validation-packs/{id}/run` executes the pack against the
  latest rulebook with the matching `rulebookId` in the tenant and
  returns `{passed, failed, totalCases, failures}`. Audited as
  `ValidationPackRun` with the pass/fail counts.
- The CLI exposes `radiopad packs list|import|export|run` for offline
  authoring (see [cli-guide.md](../08-user-docs/cli-guide.md#validation-packs-iter-35)).
- The admin page lives at `/admin/validation-packs` and uses only locked
  Open Design tokens.
