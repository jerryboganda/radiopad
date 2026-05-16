# iter31 — Agent C deliverables

## Rulebook YAML files (8, status `approved`, version `1.0.0`)

- [rulebooks/thyroid_us_v1.yaml](rulebooks/thyroid_us_v1.yaml)
- [rulebooks/prostate_mri_v1.yaml](rulebooks/prostate_mri_v1.yaml)
- [rulebooks/lung_screening_ct_v1.yaml](rulebooks/lung_screening_ct_v1.yaml)
- [rulebooks/head_ct_trauma_v1.yaml](rulebooks/head_ct_trauma_v1.yaml)
- [rulebooks/knee_mri_v1.yaml](rulebooks/knee_mri_v1.yaml)
- [rulebooks/shoulder_mri_v1.yaml](rulebooks/shoulder_mri_v1.yaml)
- [rulebooks/abdomen_ct_v1.yaml](rulebooks/abdomen_ct_v1.yaml)
- [rulebooks/pelvis_mri_v1.yaml](rulebooks/pelvis_mri_v1.yaml)

## Golden-case JSON fixtures (16, two per rulebook)

For each rulebook:
- `case-1.json` — clean report, `expectFlagged: []`.
- `case-2.json` — adversarial report with one detected built-in finding.

Paths:

- rulebooks/_tests/thyroid_us_v1/{case-1,case-2}.json — case-2 triggers `laterality_consistency`
- rulebooks/_tests/prostate_mri_v1/{case-1,case-2}.json — case-2 triggers `measurement_consistency`
- rulebooks/_tests/lung_screening_ct_v1/{case-1,case-2}.json — case-2 triggers `required_section:comparison`
- rulebooks/_tests/head_ct_trauma_v1/{case-1,case-2}.json — case-2 triggers `critical_result_language`
- rulebooks/_tests/knee_mri_v1/{case-1,case-2}.json — case-2 triggers `laterality_consistency`
- rulebooks/_tests/shoulder_mri_v1/{case-1,case-2}.json — case-2 triggers `laterality_consistency`
- rulebooks/_tests/abdomen_ct_v1/{case-1,case-2}.json — case-2 triggers `negation_conflict`
- rulebooks/_tests/pelvis_mri_v1/{case-1,case-2}.json — case-2 triggers `laterality_consistency`

Schema: each fixture is `{ name, report: { study, indication, technique, comparison?, findings, impression, recommendations? }, expectFlagged: [ruleId, …] }` — matches the in-tree convention enforced by `cli/RadioPad.Cli/Program.cs:BuildRulebookTestCommand` (the alternative `expectedFindings` key documented in `testing.instructions.md` is **not** what the CLI actually parses — `expectFlagged` is the live key).

## Custom rule ids declared at severity `info` (no engine handler — Agent J / D should add resolvers)

`ReportValidator` only recognises: `laterality_consistency`, `measurement_consistency`, `negation_conflict`, `modality_mismatch`, `impression_bullet_count`, `critical_result_language`, plus the implicit `required_section:<name>` and `style:avoid_term`. Any other rule id falls through to the default branch and emits a single `Info` finding with the literal rule id and message "Rule '<id>' is declared but has no built-in resolver."

The following custom rule ids are declared in the new rulebooks at severity `info` so they document clinical intent without breaking the engine. They are candidates for new resolvers:

| Rule id | Rulebook(s) | Intended severity | Suggested check |
| --- | --- | --- | --- |
| `tirads_category_mandatory` | thyroid_us_v1 | blocker | regex `\bTR[1-5]\b` in Impression |
| `nodule_size_required` | thyroid_us_v1 | warning | TR3+ nodule line must contain `\d+\s*mm` |
| `follow_up_language_approved` | thyroid_us_v1 | warning | Recommendations must contain one of `FNA`, `follow-up US`, `no follow-up` |
| `pirads_category_mandatory` | prostate_mri_v1 | blocker | regex `\bPI-?RADS\s*[1-5]\b` in Impression |
| `index_lesion_localized` | prostate_mri_v1 | warning | Impression must reference sector / clock face / `(PZ\|TZ\|CZ)` |
| `lungrads_category_mandatory` | lung_screening_ct_v1 | blocker | regex `\bLung-?RADS\s*(0\|1\|2\|3\|4A\|4B\|4X)\b` in Impression |
| `prior_comparison_required` | lung_screening_ct_v1 | blocker | Comparison section non-empty (already enforced by `required_section:comparison`) |
| `nodule_measurement_3d` | lung_screening_ct_v1 | warning | each `nodule` mention should have two `\d+\s*mm` measurements within 80 chars |
| `critical_finding_language` | head_ct_trauma_v1 | blocker | alias of `critical_result_language` — could simply route to the same handler |
| `midline_shift_measured` | head_ct_trauma_v1 | warning | `midline shift` mention must be followed by `\d+\s*mm` |
| `gcs_documented` | head_ct_trauma_v1 | warning | Indication must contain `GCS\s*\d+` for trauma reports |
| `meniscus_tear_pattern_described` | knee_mri_v1 | warning | `meniscal tear` line must contain a pattern keyword (`radial\|horizontal\|oblique\|bucket-handle\|complex`) |
| `acl_pcl_status_documented` | knee_mri_v1 | warning | Findings must contain both `ACL` and `PCL` |
| `rotator_cuff_thickness_described` | shoulder_mri_v1 | warning | rotator cuff tear line must contain `partial`/`full` thickness |
| `labrum_described` | shoulder_mri_v1 | warning | Findings must mention `labrum` or `labral` |
| `contrast_phase_documented` | abdomen_ct_v1 | info / warning | Technique must reference an enhancement phase keyword |
| `incidental_findings_listed` | abdomen_ct_v1 | info | impression must summarise incidental findings if any are noted in Findings |
| `figo_staging_when_oncologic` | pelvis_mri_v1 | warning | when indication mentions `cancer`/`oncologic`, Impression must reference a FIGO stage |

All eighteen of these currently surface as Info findings via the default branch; they do not break golden tests. Until a resolver lands they are documentation-only.

## Terminology refs

`rulebooks/_terminology/rads.yaml` already includes curated rows for `bi_rads`, `li_rads`, `pi_rads`, `lung_rads`, `tirads`, `c_rads`. **No edits required** for iter31-C — the three RADS modules (TI-RADS, PI-RADS, Lung-RADS) referenced by the new rulebooks are already present. Each new rulebook with a RADS dimension carries a `terminology_refs:` block that points to the matching `system:` key.

## CI workflow lines added (`.github/workflows/ci.yml`, job `cli`)

Added 8 `rulebook validate …` lines to the existing "Validate seed rulebooks" step:

```yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/thyroid_us_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/prostate_mri_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/lung_screening_ct_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/head_ct_trauma_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/knee_mri_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/shoulder_mri_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/abdomen_ct_v1.yaml
          dotnet run --project cli/RadioPad.Cli -c Release -- rulebook validate rulebooks/pelvis_mri_v1.yaml
```

Added 8 new "Run golden cases" steps mirroring the existing pattern for `chest_ct_v1` etc., one per new rulebook (`thyroid_us_v1`, `prostate_mri_v1`, `lung_screening_ct_v1`, `head_ct_trauma_v1`, `knee_mri_v1`, `shoulder_mri_v1`, `abdomen_ct_v1`, `pelvis_mri_v1`).

## Files NOT touched (per instructions)

- CHANGELOG.md
- PROGRESS.md
- docs/03-architecture/traceability-matrix.md
