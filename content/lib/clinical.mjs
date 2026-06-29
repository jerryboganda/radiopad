// Clinical building blocks for the RadioPad auto-resolving content library.
// Authored as a "panel of senior consultant radiologists" baseline: structured,
// framework-anchored, conservative. The generator (build-library.mjs) composes
// these into schema-valid templates + rulebooks. The workflow enrichment pass
// then researches current framework versions and deepens the prose per exam.

// ---------------------------------------------------------------------------
// Valid validator rule IDs (the 38 the ReportValidator engine recognises). A
// rulebook may ONLY reference these — unknown IDs are silently ignored by the
// engine, so the lint pass rejects anything outside this set.
// ---------------------------------------------------------------------------
export const VALID_RULES = new Set([
  'acl_pcl_status_documented', 'birads_assessment_in_impression', 'birads_category_required',
  'contrast_phase_documented', 'critical_finding_language', 'critical_result_language',
  'figo_staging_when_oncologic', 'follow_up_language_approved', 'fracture_acuity',
  'gcs_documented', 'impression_bullet_count', 'incidental_findings_listed',
  'index_lesion_localized', 'labrum_described', 'laterality_consistency',
  'level_consistency', 'lirads_category_required', 'lirads_observation_size_required',
  'lungrads_category_mandatory', 'lungrads_category_required', 'measurement_consistency',
  'meniscus_tear_pattern_described', 'midline_shift_measured', 'modality_mismatch',
  'negation_conflict', 'nodule_dimensions_required', 'nodule_measurement_3d',
  'nodule_size_required', 'pirads_category_mandatory', 'pirads_category_required',
  'prior_comparison_required', 'required_organ_coverage', 'rotator_cuff_thickness_described',
  'tirads_category_mandatory', 'unauthorized_followup',
]);

// Rules that apply to essentially every diagnostic report (the integrity core).
export const CORE_RULES = [
  { id: 'laterality_consistency', severity: 'blocker', description: 'Left/right findings must be internally consistent and match the impression.' },
  { id: 'negation_conflict', severity: 'blocker', description: 'A finding denied in Findings must not be asserted in the Impression (and vice versa).' },
  { id: 'measurement_consistency', severity: 'warning', description: 'Measurements must agree across Findings and Impression.' },
  { id: 'modality_mismatch', severity: 'warning', description: 'Do not reference a different modality as the source of these findings.' },
  { id: 'impression_bullet_count', severity: 'warning', description: 'Impression should not exceed style.impression_max_bullets bullets.' },
  { id: 'critical_result_language', severity: 'blocker', description: 'Critical/urgent findings require documented communication and read-back language.' },
  { id: 'unauthorized_followup', severity: 'warning', description: 'Recommendation phrasing should be drawn from style.approved_followups.' },
];

// ---------------------------------------------------------------------------
// Framework registry — maps a framework key to the validator rules it adds and
// the impression guidance the prompt blocks should enforce. Only references
// rule IDs that exist in the engine; frameworks without a dedicated rule are
// enforced through prompt guidance + required sections alone.
// ---------------------------------------------------------------------------
export const FRAMEWORKS = {
  'Lung-RADS': {
    label: 'Lung-RADS v2022',
    rules: [{ id: 'lungrads_category_required', severity: 'blocker', description: 'A Lung-RADS category (0–4X) is required for lung-screening CT.' }],
    guidance: 'Assign a Lung-RADS v2022 category (0, 1, 2, 3, 4A, 4B, 4X) and state the management interval. Report the dominant nodule with 3D size, attenuation (solid/part-solid/ground-glass), and growth versus prior.',
  },
  'Fleischner': {
    label: 'Fleischner Society 2017',
    rules: [{ id: 'nodule_size_required', severity: 'warning', description: 'Incidental pulmonary nodules require a documented size.' }],
    guidance: 'For incidental pulmonary nodules in non-screening studies, apply the Fleischner Society 2017 recommendations by nodule size, solid/subsolid character, and patient risk.',
  },
  'LI-RADS': {
    label: 'ACR LI-RADS v2018',
    rules: [
      { id: 'lirads_category_required', severity: 'blocker', description: 'An LI-RADS category (LR-1 to LR-5, LR-M, LR-TIV) is required in at-risk patients.' },
      { id: 'lirads_observation_size_required', severity: 'warning', description: 'Each LI-RADS observation requires a measured size.' },
    ],
    guidance: 'In patients at risk for HCC, characterise observations with ACR LI-RADS v2018 (major features: size, APHE, washout, capsule, threshold growth) and assign LR-1…LR-5, LR-M or LR-TIV.',
  },
  'PI-RADS': {
    label: 'PI-RADS v2.1',
    rules: [{ id: 'pirads_category_required', severity: 'blocker', description: 'A PI-RADS assessment category (1–5) is required on multiparametric prostate MRI.' }],
    guidance: 'Apply PI-RADS v2.1: score the peripheral zone on DWI and the transition zone on T2, assign an overall category 1–5, localise the index lesion on a sector map, and give size and extraprostatic extension.',
  },
  'TI-RADS': {
    label: 'ACR TI-RADS',
    rules: [{ id: 'tirads_category_mandatory', severity: 'blocker', description: 'An ACR TI-RADS level (TR1–TR5) is required for thyroid nodules.' }],
    guidance: 'Score each thyroid nodule by ACR TI-RADS (composition, echogenicity, shape, margin, echogenic foci), give the points and TR level (TR1–TR5), and state the FNA/follow-up threshold by size.',
  },
  'BI-RADS': {
    label: 'ACR BI-RADS 5th ed.',
    rules: [
      { id: 'birads_category_required', severity: 'blocker', description: 'A BI-RADS final assessment category (0–6) is required.' },
      { id: 'birads_assessment_in_impression', severity: 'blocker', description: 'The BI-RADS category must appear in the impression.' },
    ],
    guidance: 'Use ACR BI-RADS 5th edition lexicon, give a final assessment category (0–6) with management, and state breast composition (a–d).',
  },
  'O-RADS': {
    label: 'ACR O-RADS',
    rules: [], // no dedicated engine rule — enforced via prompt + required sections
    guidance: 'Assign an O-RADS risk category for adnexal lesions (US: O-RADS US 1–5; MRI: O-RADS MRI 1–5) using the lexicon descriptors, and state the management implication.',
  },
  'Bosniak': {
    label: 'Bosniak 2019',
    rules: [],
    guidance: 'Classify renal cystic lesions by the Bosniak 2019 (CT/MRI) categories (I, II, IIF, III, IV) and give the malignancy implication and follow-up.',
  },
  'CAD-RADS': {
    label: 'CAD-RADS 2.0',
    rules: [],
    guidance: 'Report coronary CTA with CAD-RADS 2.0: per-vessel maximal stenosis, an overall CAD-RADS 0–5 category, plaque burden (P1–P4) and any modifiers (N, HRP, I, S, G).',
  },
  'PE-severity': {
    label: 'CTPA / PE',
    rules: [{ id: 'critical_finding_language', severity: 'blocker', description: 'Acute PE is a critical finding requiring documented communication.' }],
    guidance: 'For suspected pulmonary embolism, state presence/absence, the most proximal level (main/lobar/segmental/subsegmental), laterality, and right-heart strain signs (RV:LV ratio, septal bowing, reflux).',
  },
  'AAST': {
    label: 'AAST organ injury grading',
    rules: [{ id: 'critical_finding_language', severity: 'blocker', description: 'High-grade solid-organ injury / active extravasation requires documented communication.' }],
    guidance: 'In trauma, grade solid-organ injury by AAST (spleen, liver, kidney), describe active arterial extravasation, haemoperitoneum, and vascular injury.',
  },
  'NI-RADS': {
    label: 'ACR NI-RADS',
    rules: [],
    guidance: 'For treated head & neck cancer surveillance, assign NI-RADS categories (1–3) for the primary site and neck and give the linked management.',
  },
  'C-RADS': {
    label: 'C-RADS (CT colonography)',
    rules: [],
    guidance: 'Report CT colonography with C-RADS colonic (C0–C4) and extracolonic (E0–E4) categories.',
  },
  'ASPECTS': {
    label: 'ASPECTS / stroke',
    rules: [{ id: 'critical_finding_language', severity: 'blocker', description: 'Acute infarct / large-vessel occlusion / haemorrhage requires documented communication.' }],
    guidance: 'For acute stroke CT, give the ASPECTS score, describe any established infarct, hyperdense vessel sign, and exclude haemorrhage.',
  },
};

// ---------------------------------------------------------------------------
// Section sets. Most diagnostic reports use the standard 5; many add
// Recommendations. Procedure/fluoroscopy reports use a leaner set.
// ---------------------------------------------------------------------------
export const SECTIONS = {
  std: ['Indication', 'Technique', 'Comparison', 'Findings', 'Impression'],
  stdRec: ['Indication', 'Technique', 'Comparison', 'Findings', 'Impression', 'Recommendations'],
  procedure: ['Indication', 'Technique', 'Findings', 'Impression'],
};

// Human labels + ids for the standard sections (template scaffold).
export const SECTION_META = {
  Indication: { id: 'indication', label: 'Indication' },
  Technique: { id: 'technique', label: 'Technique' },
  Comparison: { id: 'comparison', label: 'Comparison' },
  Findings: { id: 'findings', label: 'Findings' },
  Impression: { id: 'impression', label: 'Impression' },
  Recommendations: { id: 'recommendations', label: 'Recommendations' },
};

// Approved follow-up phrasing shared across the library (clinically conservative,
// non-committal, drives the `unauthorized_followup` / approved_followups rule).
export const APPROVED_FOLLOWUPS = [
  'Recommend clinical correlation.',
  'Recommend correlation with laboratory and clinical findings.',
  'Recommend follow-up imaging as clinically indicated.',
  'Recommend comparison with prior imaging when available.',
  'Recommend specialty consultation if clinically warranted.',
];

// Terms a consultant report should avoid (vague / medico-legally weak).
export const AVOID_TERMS = ['unremarkable', 'cannot rule out', 'grossly normal', 'questionable', 'rule out'];
