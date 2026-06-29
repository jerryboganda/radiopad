// RadioPad auto-resolving content-library generator.
// Emits, for the full exam taxonomy: one template JSON per exam variant, one
// rulebook YAML per (modality, body part) that isn't already covered by an
// existing rulebook, and a manifest (content/exam-catalog.json).
//
// Deterministic + idempotent: re-running overwrites only the files it generates
// (library templates + library rulebooks), never the hand-authored existing
// rulebooks or the legacy demo templates. Everything is schema-valid, references
// only real validator rule IDs, uses catalog body-part codes verbatim, and is
// status: approved so it resolves out-of-the-box.
//
//   node content/build-library.mjs

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  VALID_RULES, CORE_RULES, FRAMEWORKS, SECTIONS, SECTION_META,
  APPROVED_FOLLOWUPS, AVOID_TERMS,
} from './lib/clinical.mjs';
import { technique, scaffold } from './lib/scaffolds.mjs';
import { GROUPS } from './lib/taxonomy.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const TPL_DIR = path.join(ROOT, 'templates');
const RB_DIR = path.join(ROOT, 'rulebooks');
const MANIFEST = path.join(ROOT, 'content', 'exam-catalog.json');

const CONTRAST_TAG = { None: 'nc', With: 'c', WithAndWithout: 'mp', '': '' };

const slug = (s) => s.toLowerCase().replace(/&/g, 'and').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const rbSlug = (s) => s.toLowerCase().replace(/&/g, 'and').replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');

// --- Parse existing rulebooks → coverage map (reuse instead of duplicate) ----
function loadExistingRulebooks() {
  const out = [];
  const ids = new Set();
  for (const f of fs.readdirSync(RB_DIR).filter((x) => x.endsWith('.yaml'))) {
    const t = fs.readFileSync(path.join(RB_DIR, f), 'utf8');
    const id = (t.match(/rulebook_id:\s*(.+)/) || [])[1]?.trim();
    if (!id) continue;
    ids.add(id);
    const a = { modalities: [], body_parts: [] };
    const lines = t.split(/\r?\n/);
    let inApplies = false, cur = null;
    for (const ln of lines) {
      if (/^applies_to:/.test(ln)) { inApplies = true; continue; }
      if (inApplies) {
        if (/^\S/.test(ln)) { inApplies = false; continue; }
        const sub = ln.match(/^\s{2}(\w+):\s*$/); if (sub) { cur = sub[1]; continue; }
        const it = ln.match(/^\s{4}-\s+(.+)$/); if (it && a[cur]) a[cur].push(it[1].trim());
      }
    }
    out.push({ id, file: f, ...a });
  }
  return { books: out, ids };
}

const ci = (arr, v) => arr.some((x) => x.toLowerCase() === v.toLowerCase());

// --- Region/scaffold-specific validator rules (valid IDs only) ---------------
const SCAFFOLD_RULES = {
  knee: [
    { id: 'meniscus_tear_pattern_described', severity: 'warning', description: 'Meniscal tears require pattern and zone.' },
    { id: 'acl_pcl_status_documented', severity: 'warning', description: 'Cruciate ligament status must be stated.' },
    { id: 'fracture_acuity', severity: 'info', description: 'State acuity of any fracture/contusion.' },
  ],
  shoulder: [
    { id: 'rotator_cuff_thickness_described', severity: 'warning', description: 'Rotator-cuff tendon integrity/thickness must be described.' },
    { id: 'labrum_described', severity: 'info', description: 'Labral status must be described.' },
  ],
  bone_xray: [{ id: 'fracture_acuity', severity: 'warning', description: 'State presence and acuity of any fracture.' }],
  msk_joint: [{ id: 'fracture_acuity', severity: 'info', description: 'State acuity of any osseous injury.' }],
  cspine: [{ id: 'level_consistency', severity: 'warning', description: 'Spinal levels must be consistent.' }],
  tspine: [{ id: 'level_consistency', severity: 'warning', description: 'Spinal levels must be consistent.' }],
  lspine: [{ id: 'level_consistency', severity: 'warning', description: 'Spinal levels must be consistent.' }],
  brain: [{ id: 'midline_shift_measured', severity: 'info', description: 'Quantify midline shift when mass effect is present.' }],
  brain_stroke: [
    { id: 'midline_shift_measured', severity: 'info', description: 'Quantify midline shift when present.' },
    { id: 'critical_finding_language', severity: 'blocker', description: 'Acute infarct/haemorrhage requires documented communication.' },
  ],
  chest_ct: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }, { id: 'nodule_measurement_3d', severity: 'info', description: 'Measure dominant nodule in 3D.' }],
  hrct: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }],
  cxr: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }],
  abdomen: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }],
  abdomen_pelvis: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }],
  kidneys: [{ id: 'incidental_findings_listed', severity: 'info', description: 'List actionable incidental findings.' }],
  female_pelvis: [{ id: 'figo_staging_when_oncologic', severity: 'info', description: 'Provide FIGO staging when an oncologic indication is present.' }],
  prostate: [{ id: 'index_lesion_localized', severity: 'warning', description: 'Localise the index lesion on a sector map.' }],
  liver: [{ id: 'index_lesion_localized', severity: 'info', description: 'Localise and size the index observation.' }],
};

function rulesFor(group) {
  const map = new Map();
  const add = (r) => { if (VALID_RULES.has(r.id) && !map.has(r.id)) map.set(r.id, r); };
  CORE_RULES.forEach(add);
  add({ id: 'prior_comparison_required', severity: 'info', description: 'Compare with relevant prior imaging when available.' });
  // contrast documentation when any variant uses contrast
  if (group.v.some((x) => x.c === 'With' || x.c === 'WithAndWithout')) {
    add({ id: 'contrast_phase_documented', severity: 'warning', description: 'Document the contrast phase(s) acquired.' });
  }
  (SCAFFOLD_RULES[group.scaffold] || []).forEach(add);
  for (const fwk of group.fw || []) {
    const fw = FRAMEWORKS[fwk];
    if (fw) (fw.rules || []).forEach(add);
  }
  return [...map.values()];
}

function frameworkGuidance(group) {
  return (group.fw || [])
    .map((k) => FRAMEWORKS[k])
    .filter(Boolean)
    .map((fw) => `- ${fw.label}: ${fw.guidance}`)
    .join('\n');
}

// --- YAML emitter (scoped to our known structure) ----------------------------
const q = (s) => '"' + String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
function block(key, text, indent = '  ') {
  const body = text.split('\n').map((l) => `${indent}  ${l}`.replace(/\s+$/,'')).join('\n');
  return `${indent}${key}: |-\n${body}\n`;
}

function renderRulebook(rb) {
  let y = '';
  y += `rulebook_id: ${rb.id}\n`;
  y += `name: ${q(rb.name)}\n`;
  y += `version: 1.0.0\n`;
  y += `owner: ${q(rb.owner)}\n`;
  y += `status: approved\n`;
  y += `applies_to:\n  modalities:\n    - ${rb.modality}\n  body_parts:\n    - ${q(rb.bodyPart)}\n  report_types:\n    - diagnostic\n    - follow_up\n`;
  y += `style:\n  tone: concise_clinical\n  impression_max_bullets: 5\n`;
  y += `  avoid_terms:\n${AVOID_TERMS.map((t) => `    - ${q(t)}`).join('\n')}\n`;
  y += `  approved_followups:\n${APPROVED_FOLLOWUPS.map((t) => `    - ${q(t)}`).join('\n')}\n`;
  y += `required_sections:\n${rb.sections.map((s) => `  - ${s}`).join('\n')}\n`;
  y += `rules:\n`;
  for (const r of rb.rules) {
    y += `  - id: ${r.id}\n    severity: ${r.severity}\n    description: ${q(r.description)}\n`;
  }
  y += `prompt_blocks:\n`;
  for (const [k, v] of Object.entries(rb.prompt_blocks)) y += block(k, v);
  return y;
}

function promptBlocks(group, rb) {
  const fg = frameworkGuidance(group);
  const fgLine = fg ? `\nApply the relevant reporting framework(s):\n${fg}` : '';
  const sections = rb.sections.join(', ');
  const descriptor = `${group.mod} ${group.bp}`;
  return {
    system:
`You are assisting a board-certified consultant radiologist drafting a structured ${descriptor} report.
Write with the precision and economy of a senior subspecialty consultant. Never invent findings; report only what is supported by the images and dictation. Preserve every measurement, laterality and negation exactly. Adapt normal ranges and differential emphasis to the patient's age and sex. Output only the requested section; do not sign the report. Avoid the terms in style.avoid_terms.${fgLine}`,
    draft:
`Generate a structured draft with the sections: ${sections}. Populate Findings organ-by-organ using the scaffold; convert dictation into clean clinical prose without adding findings. Honour the stated contrast phase in Technique. Do not exceed the impression bullet limit.${fgLine}`,
    impression:
`Generate a concise impression (max ${5} bullets) faithful to the Findings. Lead with the most clinically significant finding, give actionable recommendations only from style.approved_followups, and include any required scoring category.${fgLine}`,
    cleanup:
`Rewrite the dictated text into clean grammatical clinical prose without changing meaning. Preserve all numbers, laterality, negations and units exactly. Return an empty string for any section that was not dictated.`,
    follow_up:
`Suggest up to three short, evidence-based follow-up recommendations, one per line, drawn only from approved follow-up phrasing and tied to a finding already present. No new diagnoses.`,
    dictation_cleanup:
`Rewrite this dictation into clean grammatical prose for each report section. Do not invent findings the radiologist did not dictate. Preserve every measurement, laterality and negation exactly. If a section was not dictated, return an empty string for it.`,
  };
}

// --- Template emitter --------------------------------------------------------
function renderTemplate(group, variant, examId, rulebookId) {
  const secNames = SECTIONS[group.sections];
  const sections = secNames.map((name) => {
    const meta = SECTION_META[name];
    let placeholder;
    if (name === 'Technique') placeholder = technique(group.mod, group.bp, variant.c);
    else if (name === 'Findings') placeholder = scaffold(group.scaffold, group.mod);
    else if (name === 'Indication') placeholder = 'Clinical question / referring concern.';
    else if (name === 'Comparison') placeholder = 'Date and type of relevant prior imaging, if available.';
    else if (name === 'Impression') placeholder = 'Concise, prioritised bulleted impression with any required scoring category and approved follow-up.';
    else if (name === 'Recommendations') placeholder = 'Evidence-based follow-up or correlation, drawn from approved phrasing, only if indicated.';
    else placeholder = '';
    return { id: meta.id, label: meta.label, placeholder };
  });
  return {
    id: examId,
    templateId: examId,
    name: variant.name,
    modality: group.mod,
    bodyPart: group.bp,
    contrast: variant.c,
    status: 'approved',
    subspecialty: group.sub,
    rulebookId,
    sections,
  };
}

// =============================== build =======================================
const { books: existing, ids: existingIds } = loadExistingRulebooks();
const examIds = new Set();
const manifest = [];
const rulebooksToWrite = new Map(); // id -> {group, rb}
let tplCount = 0, reuseCount = 0, newRbCount = 0;

for (const group of GROUPS) {
  // decide rulebook: reuse existing if it covers (modality, bodyPart) exactly
  const reuse = existing.find((r) => ci(r.modalities, group.mod) && ci(r.body_parts, group.bp));
  let rulebookId;
  if (reuse) { rulebookId = reuse.id; reuseCount++; }
  else {
    // Generated library rulebooks are always suffixed `_rp_v1` (RadioPad-library
    // marker): re-running overwrites them in place (stable names) and they never
    // collide with the hand-authored `{region}_{modality}_v1` rulebooks.
    // WARNING: re-running this generator OVERWRITES the generated rulebook + template
    // files and will CLOBBER any manual/agent enrichment layered on top. Treat it as
    // a one-time scaffold; do not re-run after enrichment without backing up first.
    rulebookId = `${group.mod.toLowerCase()}_${rbSlug(group.bp)}_rp_v1`;
    if (!rulebooksToWrite.has(rulebookId)) {
      const rb = {
        id: rulebookId,
        name: `${group.mod} ${group.bp} Reporting Rulebook`,
        owner: `${group.sub} Imaging Committee`,
        modality: group.mod,
        bodyPart: group.bp,
        sections: SECTIONS[group.sections],
        rules: rulesFor(group),
      };
      rb.prompt_blocks = promptBlocks(group, rb);
      rulebooksToWrite.set(rulebookId, rb);
      newRbCount++;
    }
  }

  for (const variant of group.v) {
    const ctag = CONTRAST_TAG[variant.c] || '';
    const parts = [group.mod.toLowerCase(), rbSlug(group.bp).replace(/_/g, '-')];
    if (variant.suffix) parts.push(variant.suffix);
    if (ctag) parts.push(ctag);
    let examId = parts.join('-');
    while (examIds.has(examId)) examId += '-x';
    examIds.add(examId);

    const tpl = renderTemplate(group, variant, examId, rulebookId);
    fs.writeFileSync(path.join(TPL_DIR, `${examId}.json`), JSON.stringify(tpl, null, 2) + '\n');
    tplCount++;

    const createdRb = rulebooksToWrite.get(rulebookId);
    manifest.push({
      examId, name: variant.name, modality: group.mod, bodyPart: group.bp,
      contrast: variant.c, sex: variant.sex || '', subspecialty: group.sub,
      rulebookId, rulebookSource: reuse ? 'existing' : 'generated',
      frameworks: group.fw || [], requiredSections: SECTIONS[group.sections],
      validatorRules: createdRb ? createdRb.rules.map((r) => r.id) : [],
    });
  }
}

// write new rulebooks
for (const rb of rulebooksToWrite.values()) {
  fs.writeFileSync(path.join(RB_DIR, `${rb.id}.yaml`), renderRulebook(rb));
}

// write manifest
fs.mkdirSync(path.dirname(MANIFEST), { recursive: true });
fs.writeFileSync(MANIFEST, JSON.stringify({ generatedExams: manifest.length, exams: manifest }, null, 2) + '\n');

console.log(`templates written : ${tplCount}`);
console.log(`rulebooks created : ${newRbCount}`);
console.log(`rulebooks reused  : ${reuseCount} group(s) pointed at existing rulebooks`);
console.log(`manifest exams    : ${manifest.length}`);
console.log(`distinct examIds  : ${examIds.size}`);
