// Deterministic lint for the generated content library. Validates schema,
// rule-ID validity, catalog-key conformance, and template→rulebook resolvability.
// Exit code 1 on any error.   node content/lint-library.mjs

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { VALID_RULES } from './lib/clinical.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const TPL_DIR = path.join(ROOT, 'templates');
const RB_DIR = path.join(ROOT, 'rulebooks');

// Canonical catalog vocabulary (mirror of CatalogSeed.cs).
const MODALITIES = new Set(['CT', 'MR', 'US', 'XR', 'NM', 'PET', 'MG', 'FL']);
const BODY_PARTS = new Set([
  'Head', 'Neck', 'Chest', 'Abdomen', 'Pelvis', 'Spine', 'Extremity', 'Whole Body',
  'Brain', 'Pituitary', 'Orbits', 'Internal Auditory Canal', 'Temporal Bones',
  'Paranasal Sinuses', 'Facial Bones', 'Nasopharynx', 'Larynx', 'Salivary Glands', 'Thyroid', 'Temporomandibular Joints',
  'Cervical Spine', 'Thoracic Spine', 'Lumbar Spine', 'Whole Spine', 'Sacrum & Coccyx', 'Sacroiliac Joints',
  'Cardiac', 'Coronary Arteries', 'Pulmonary Arteries', 'Thoracic Aorta', 'Breast',
  'Liver', 'Pancreas', 'Biliary System', 'Kidneys', 'Adrenals', 'Abdominal Aorta', 'Abdomen & Pelvis',
  'Female Pelvis', 'Prostate', 'Rectum', 'Bladder', 'Scrotum',
  'Small Bowel', 'Urinary Tract', 'KUB',
  'Shoulder', 'Humerus', 'Elbow', 'Forearm', 'Wrist', 'Hand',
  'Hip', 'Femur', 'Knee', 'Tibia & Fibula', 'Ankle', 'Foot', 'Bony Pelvis',
  'Carotid Arteries', 'Intracranial Arteries', 'Renal Arteries', 'Peripheral Runoff',
  'Obstetric', 'Neonatal Head',
  // legacy generic body parts used by some pre-existing rulebooks
  'Lung', 'Heart',
]);
const CONTRASTS = new Set(['', 'None', 'With', 'WithAndWithout']);

const errs = [];
const warns = [];
const E = (m) => errs.push(m);
const W = (m) => warns.push(m);
const ci = (arr, v) => arr.some((x) => x.toLowerCase() === String(v).toLowerCase());

// ---- parse rulebooks --------------------------------------------------------
function parseRb(txt) {
  const id = (txt.match(/rulebook_id:\s*(.+)/) || [])[1]?.trim();
  const status = (txt.match(/\nstatus:\s*(.+)/) || [])[1]?.trim();
  const ruleIds = [...txt.matchAll(/^\s+-\s+id:\s*(\S+)/gm)].map((m) => m[1]);
  const applies = { modalities: [], body_parts: [] };
  const lines = txt.split(/\r?\n/);
  let inA = false, cur = null;
  for (const ln of lines) {
    if (/^applies_to:/.test(ln)) { inA = true; continue; }
    if (inA) {
      if (/^\S/.test(ln)) { inA = false; continue; }
      const sub = ln.match(/^\s{2}(\w+):\s*$/); if (sub) { cur = sub[1]; continue; }
      const it = ln.match(/^\s{4}-\s+(.+)$/); if (it && applies[cur]) applies[cur].push(it[1].replace(/^"|"$/g, '').trim());
    }
  }
  const reqCount = [...txt.matchAll(/^required_sections:\n((?:\s+-\s+.*\n)+)/gm)].length;
  return { id, status, ruleIds, applies, hasRequired: /required_sections:\n\s+-\s+/.test(txt) };
}

const rulebooks = [];
for (const f of fs.readdirSync(RB_DIR).filter((x) => x.endsWith('.yaml'))) {
  const txt = fs.readFileSync(path.join(RB_DIR, f), 'utf8');
  const rb = parseRb(txt); rb.file = f;
  rulebooks.push(rb);
  if (!rb.id) { E(`${f}: missing rulebook_id`); continue; }
  if (!rb.hasRequired) E(`${f}: required_sections must list at least one section`);
  for (const rid of rb.ruleIds) if (!VALID_RULES.has(rid)) E(`${f}: invalid rule id '${rid}'`);
  for (const m of rb.applies.modalities) if (!MODALITIES.has(m)) W(`${f}: modality '${m}' not a catalog code`);
  for (const bp of rb.applies.body_parts) if (!BODY_PARTS.has(bp)) W(`${f}: body_part '${bp}' not in catalog`);
}
const approvedRbs = rulebooks.filter((r) => r.status === 'approved');
const rbById = new Map(rulebooks.map((r) => [r.id, r]));

// ---- parse templates --------------------------------------------------------
let tpl = 0;
for (const f of fs.readdirSync(TPL_DIR).filter((x) => x.endsWith('.json'))) {
  let t;
  try { t = JSON.parse(fs.readFileSync(path.join(TPL_DIR, f), 'utf8')); }
  catch (e) { E(`${f}: invalid JSON (${e.message})`); continue; }
  if (Array.isArray(t)) continue; // not a template object
  const id = t.templateId || t.id;
  if (!id) { E(`${f}: missing id/templateId`); continue; }
  tpl++;
  if (t.modality && !MODALITIES.has(t.modality)) W(`${f}: modality '${t.modality}' not a catalog code`);
  if (t.bodyPart && !BODY_PARTS.has(t.bodyPart)) W(`${f}: bodyPart '${t.bodyPart}' not in catalog`);
  if (t.contrast !== undefined && !CONTRASTS.has(t.contrast)) E(`${f}: invalid contrast '${t.contrast}'`);
  if (!Array.isArray(t.sections) || t.sections.length === 0) E(`${f}: sections must be a non-empty array`);
  // resolvability: an approved rulebook must apply to (modality, bodyPart)
  if (t.modality && t.bodyPart) {
    const hit = approvedRbs.find((r) => ci(r.applies.modalities, t.modality) && ci(r.applies.body_parts, t.bodyPart));
    if (!hit) E(`${f}: no approved rulebook resolves for (${t.modality}, ${t.bodyPart})`);
    if (t.rulebookId && !rbById.has(t.rulebookId)) W(`${f}: linked rulebookId '${t.rulebookId}' not found as a file`);
  }
}

console.log(`rulebooks scanned : ${rulebooks.length} (${approvedRbs.length} approved)`);
console.log(`templates scanned : ${tpl}`);
console.log(`warnings          : ${warns.length}`);
console.log(`errors            : ${errs.length}`);
if (warns.length) { console.log('\n--- warnings ---'); warns.slice(0, 40).forEach((w) => console.log('  ! ' + w)); if (warns.length > 40) console.log(`  …and ${warns.length - 40} more`); }
if (errs.length) { console.log('\n--- errors ---'); errs.slice(0, 60).forEach((e) => console.log('  ✗ ' + e)); process.exitCode = 1; }
else console.log('\nLINT PASSED ✓');
