// The exam taxonomy. Each GROUP = one (modality, granular body part) → one
// rulebook + one or more templates (variants by contrast / sex). Body-part
// strings MUST match the expanded catalog vocabulary (CatalogSeed.DefaultBodyParts)
// verbatim. Contrast: 'None' | 'With' | 'WithAndWithout' | '' (agnostic).
//
// v = variants: { c: contrast, name, sex?: 'M'|'F', suffix?: id suffix }
// A group with a single agnostic variant => one contrast-agnostic template.

const std = 'std', rec = 'stdRec', proc = 'procedure';

export const GROUPS = [
  // ===================== CT =====================
  { mod: 'CT', bp: 'Brain', sub: 'Neuro', scaffold: 'brain', sections: rec, fw: ['ASPECTS'], v: [
    { c: 'None', name: 'CT Brain (non-contrast)' },
    { c: 'With', name: 'CT Brain (post-contrast)' },
    { c: 'WithAndWithout', name: 'CT Brain (pre- and post-contrast)' },
  ] },
  { mod: 'CT', bp: 'Intracranial Arteries', sub: 'Neuro', scaffold: 'brain_stroke', sections: rec, fw: ['ASPECTS'], v: [
    { c: 'With', name: 'CT Angiography — Circle of Willis' },
  ] },
  { mod: 'CT', bp: 'Paranasal Sinuses', sub: 'Head & Neck', scaffold: 'sinuses', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Paranasal Sinuses' },
  ] },
  { mod: 'CT', bp: 'Facial Bones', sub: 'Head & Neck', scaffold: 'sinuses', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Facial Bones' },
  ] },
  { mod: 'CT', bp: 'Temporal Bones', sub: 'Neuro', scaffold: 'temporal_bone', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Temporal Bones' },
  ] },
  { mod: 'CT', bp: 'Orbits', sub: 'Head & Neck', scaffold: 'brain', sections: std, fw: [], v: [
    { c: 'WithAndWithout', name: 'CT Orbits' },
  ] },
  { mod: 'CT', bp: 'Neck', sub: 'Head & Neck', scaffold: 'neck', sections: rec, fw: ['NI-RADS'], v: [
    { c: 'With', name: 'CT Neck (post-contrast)' },
  ] },
  { mod: 'CT', bp: 'Carotid Arteries', sub: 'Vascular', scaffold: 'carotid', sections: std, fw: [], v: [
    { c: 'With', name: 'CT Angiography — Carotids / Neck' },
  ] },
  { mod: 'CT', bp: 'Cervical Spine', sub: 'MSK/Spine', scaffold: 'cspine', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Cervical Spine' },
  ] },
  { mod: 'CT', bp: 'Thoracic Spine', sub: 'MSK/Spine', scaffold: 'tspine', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Thoracic Spine' },
  ] },
  { mod: 'CT', bp: 'Lumbar Spine', sub: 'MSK/Spine', scaffold: 'lspine', sections: std, fw: [], v: [
    { c: 'None', name: 'CT Lumbar Spine' },
  ] },
  { mod: 'CT', bp: 'Chest', sub: 'Thoracic', scaffold: 'chest_ct', sections: rec, fw: ['Fleischner'], v: [
    { c: 'None', name: 'CT Chest (non-contrast)' },
    { c: 'With', name: 'CT Chest (post-contrast)' },
  ] },
  { mod: 'CT', bp: 'Pulmonary Arteries', sub: 'Thoracic', scaffold: 'ctpa', sections: rec, fw: ['PE-severity'], v: [
    { c: 'With', name: 'CT Pulmonary Angiography (CTPA)' },
  ] },
  { mod: 'CT', bp: 'Coronary Arteries', sub: 'Cardiac', scaffold: 'coronary', sections: rec, fw: ['CAD-RADS'], v: [
    { c: 'With', name: 'CT Coronary Angiography' },
    { c: 'None', name: 'CT Coronary Calcium Score', suffix: 'calcium' },
  ] },
  { mod: 'CT', bp: 'Thoracic Aorta', sub: 'Vascular', scaffold: 'aaa', sections: std, fw: [], v: [
    { c: 'With', name: 'CT Angiography — Thoracic Aorta' },
  ] },
  { mod: 'CT', bp: 'Abdomen', sub: 'Body', scaffold: 'abdomen', sections: rec, fw: [], v: [
    { c: 'None', name: 'CT Abdomen (non-contrast)' },
    { c: 'With', name: 'CT Abdomen (post-contrast)' },
  ] },
  { mod: 'CT', bp: 'Abdomen & Pelvis', sub: 'Body', scaffold: 'abdomen_pelvis', sections: rec, fw: ['AAST'], v: [
    { c: 'None', name: 'CT Abdomen & Pelvis (non-contrast)' },
    { c: 'With', name: 'CT Abdomen & Pelvis (portal-venous)' },
    { c: 'WithAndWithout', name: 'CT Abdomen & Pelvis (multiphase)' },
  ] },
  { mod: 'CT', bp: 'Liver', sub: 'Body', scaffold: 'liver', sections: rec, fw: ['LI-RADS'], v: [
    { c: 'WithAndWithout', name: 'CT Liver (multiphase / triphasic)' },
  ] },
  { mod: 'CT', bp: 'Pancreas', sub: 'Body', scaffold: 'pancreas', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'CT Pancreas (pancreatic protocol)' },
  ] },
  { mod: 'CT', bp: 'Adrenals', sub: 'Body', scaffold: 'adrenal', sections: std, fw: [], v: [
    { c: 'WithAndWithout', name: 'CT Adrenal (washout protocol)' },
  ] },
  { mod: 'CT', bp: 'Kidneys', sub: 'Body', scaffold: 'kidneys', sections: rec, fw: ['Bosniak'], v: [
    { c: 'WithAndWithout', name: 'CT Kidneys (renal mass protocol)' },
  ] },
  { mod: 'CT', bp: 'Urinary Tract', sub: 'Body', scaffold: 'kidneys', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'CT Urography (CTU)' },
  ] },
  { mod: 'CT', bp: 'KUB', sub: 'Body', scaffold: 'kidneys', sections: std, fw: [], v: [
    { c: 'None', name: 'CT KUB (renal colic / stone protocol)' },
  ] },
  { mod: 'CT', bp: 'Small Bowel', sub: 'Body', scaffold: 'abdomen_pelvis', sections: rec, fw: [], v: [
    { c: 'With', name: 'CT Enterography' },
  ] },
  { mod: 'CT', bp: 'Pelvis', sub: 'Body', scaffold: 'abdomen_pelvis', sections: rec, fw: [], v: [
    { c: 'None', name: 'CT Pelvis (non-contrast)' },
    { c: 'With', name: 'CT Pelvis (post-contrast)' },
  ] },
  { mod: 'CT', bp: 'Abdominal Aorta', sub: 'Vascular', scaffold: 'aaa', sections: std, fw: [], v: [
    { c: 'With', name: 'CT Angiography — Abdominal Aorta' },
  ] },
  { mod: 'CT', bp: 'Peripheral Runoff', sub: 'Vascular', scaffold: 'vascular_doppler', sections: std, fw: [], v: [
    { c: 'With', name: 'CT Angiography — Peripheral Runoff' },
  ] },
  { mod: 'CT', bp: 'Whole Body', sub: 'Trauma/Oncology', scaffold: 'abdomen_pelvis', sections: rec, fw: ['AAST'], v: [
    { c: 'With', name: 'CT Whole Body (trauma / pan-scan)' },
    { c: 'With', name: 'CT Whole Body (oncology staging)', suffix: 'onc' },
  ] },
  { mod: 'CT', bp: 'Shoulder', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'CT Shoulder' }] },
  { mod: 'CT', bp: 'Wrist', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'CT Wrist' }] },
  { mod: 'CT', bp: 'Hip', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'CT Hip' }] },
  { mod: 'CT', bp: 'Knee', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'CT Knee' }] },
  { mod: 'CT', bp: 'Foot', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'CT Foot / Ankle' }] },

  // ===================== MR =====================
  { mod: 'MR', bp: 'Brain', sub: 'Neuro', scaffold: 'brain', sections: rec, fw: [], v: [
    { c: 'None', name: 'MRI Brain (non-contrast)' },
    { c: 'With', name: 'MRI Brain (post-contrast)' },
    { c: 'WithAndWithout', name: 'MRI Brain (pre- and post-contrast)' },
  ] },
  { mod: 'MR', bp: 'Brain', sub: 'Neuro', scaffold: 'brain_stroke', sections: rec, fw: ['ASPECTS'], v: [
    { c: 'None', name: 'MRI Brain (stroke / DWI protocol)', suffix: 'stroke' },
  ] },
  { mod: 'MR', bp: 'Pituitary', sub: 'Neuro', scaffold: 'pituitary', sections: std, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Pituitary (dynamic)' },
  ] },
  { mod: 'MR', bp: 'Orbits', sub: 'Neuro', scaffold: 'brain', sections: std, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Orbits' },
  ] },
  { mod: 'MR', bp: 'Internal Auditory Canal', sub: 'Neuro', scaffold: 'temporal_bone', sections: std, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Internal Auditory Canals' },
  ] },
  { mod: 'MR', bp: 'Intracranial Arteries', sub: 'Neuro', scaffold: 'brain_stroke', sections: std, fw: [], v: [
    { c: 'None', name: 'MR Angiography — Intracranial (TOF)' },
  ] },
  { mod: 'MR', bp: 'Neck', sub: 'Head & Neck', scaffold: 'neck', sections: rec, fw: ['NI-RADS'], v: [
    { c: 'WithAndWithout', name: 'MRI Neck (soft tissue)' },
  ] },
  { mod: 'MR', bp: 'Cervical Spine', sub: 'MSK/Spine', scaffold: 'cspine', sections: rec, fw: [], v: [
    { c: 'None', name: 'MRI Cervical Spine' },
    { c: 'WithAndWithout', name: 'MRI Cervical Spine (infection/tumour)', suffix: 'gad' },
  ] },
  { mod: 'MR', bp: 'Thoracic Spine', sub: 'MSK/Spine', scaffold: 'tspine', sections: rec, fw: [], v: [
    { c: 'None', name: 'MRI Thoracic Spine' },
  ] },
  { mod: 'MR', bp: 'Lumbar Spine', sub: 'MSK/Spine', scaffold: 'lspine', sections: rec, fw: [], v: [
    { c: 'None', name: 'MRI Lumbar Spine' },
    { c: 'WithAndWithout', name: 'MRI Lumbar Spine (post-op / infection)', suffix: 'gad' },
  ] },
  { mod: 'MR', bp: 'Whole Spine', sub: 'MSK/Spine', scaffold: 'tspine', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Whole Spine (screening)' },
  ] },
  { mod: 'MR', bp: 'Sacroiliac Joints', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [
    { c: 'None', name: 'MRI Sacroiliac Joints (sacroiliitis)' },
  ] },
  { mod: 'MR', bp: 'Cardiac', sub: 'Cardiac', scaffold: 'cardiac_mri', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Cardiac (function & viability)' },
  ] },
  { mod: 'MR', bp: 'Breast', sub: 'Breast', scaffold: 'breast_us', sections: rec, fw: ['BI-RADS'], v: [
    { c: 'WithAndWithout', name: 'MRI Breast (dynamic contrast-enhanced)' },
  ] },
  { mod: 'MR', bp: 'Liver', sub: 'Body', scaffold: 'liver', sections: rec, fw: ['LI-RADS'], v: [
    { c: 'WithAndWithout', name: 'MRI Liver (hepatobiliary contrast)' },
  ] },
  { mod: 'MR', bp: 'Biliary System', sub: 'Body', scaffold: 'mrcp', sections: std, fw: [], v: [
    { c: 'None', name: 'MRCP (magnetic resonance cholangiopancreatography)' },
  ] },
  { mod: 'MR', bp: 'Pancreas', sub: 'Body', scaffold: 'pancreas', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Pancreas' },
  ] },
  { mod: 'MR', bp: 'Abdomen', sub: 'Body', scaffold: 'abdomen', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Abdomen' },
  ] },
  { mod: 'MR', bp: 'Adrenals', sub: 'Body', scaffold: 'adrenal', sections: std, fw: [], v: [
    { c: 'None', name: 'MRI Adrenal (chemical-shift)' },
  ] },
  { mod: 'MR', bp: 'Urinary Tract', sub: 'Body', scaffold: 'kidneys', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MR Urography' },
  ] },
  { mod: 'MR', bp: 'Small Bowel', sub: 'Body', scaffold: 'abdomen_pelvis', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MR Enterography' },
  ] },
  { mod: 'MR', bp: 'Female Pelvis', sub: 'Body', scaffold: 'female_pelvis', sections: rec, fw: ['O-RADS'], v: [
    { c: 'WithAndWithout', name: 'MRI Female Pelvis', sex: 'F' },
  ] },
  { mod: 'MR', bp: 'Prostate', sub: 'Body', scaffold: 'prostate', sections: rec, fw: ['PI-RADS'], v: [
    { c: 'WithAndWithout', name: 'MRI Prostate (multiparametric)', sex: 'M' },
  ] },
  { mod: 'MR', bp: 'Rectum', sub: 'Body', scaffold: 'rectum', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Rectum (cancer staging)' },
  ] },
  { mod: 'MR', bp: 'Pelvis', sub: 'Body', scaffold: 'female_pelvis', sections: rec, fw: [], v: [
    { c: 'WithAndWithout', name: 'MRI Pelvis (perianal fistula)', suffix: 'fistula' },
  ] },
  { mod: 'MR', bp: 'Shoulder', sub: 'MSK', scaffold: 'shoulder', sections: std, fw: [], v: [
    { c: 'None', name: 'MRI Shoulder' },
    { c: 'With', name: 'MR Arthrogram Shoulder', suffix: 'arthro' },
  ] },
  { mod: 'MR', bp: 'Elbow', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'MRI Elbow' }] },
  { mod: 'MR', bp: 'Wrist', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'MRI Wrist' }] },
  { mod: 'MR', bp: 'Hip', sub: 'MSK', scaffold: 'hip', sections: std, fw: [], v: [
    { c: 'None', name: 'MRI Hip' },
    { c: 'With', name: 'MR Arthrogram Hip', suffix: 'arthro' },
  ] },
  { mod: 'MR', bp: 'Knee', sub: 'MSK', scaffold: 'knee', sections: std, fw: [], v: [{ c: 'None', name: 'MRI Knee' }] },
  { mod: 'MR', bp: 'Ankle', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'MRI Ankle' }] },
  { mod: 'MR', bp: 'Foot', sub: 'MSK', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: 'None', name: 'MRI Foot' }] },
  { mod: 'MR', bp: 'Femur', sub: 'MSK', scaffold: 'msk_joint', sections: rec, fw: [], v: [{ c: 'WithAndWithout', name: 'MRI Femur (marrow / tumour)' }] },
  { mod: 'MR', bp: 'Renal Arteries', sub: 'Vascular', scaffold: 'vascular_doppler', sections: std, fw: [], v: [{ c: 'With', name: 'MR Angiography — Renal' }] },

  // ===================== US =====================
  { mod: 'US', bp: 'Abdomen', sub: 'Body', scaffold: 'abdomen', sections: std, fw: [], v: [
    { c: '', name: 'Ultrasound Abdomen (complete)' },
    { c: '', name: 'Ultrasound Right Upper Quadrant (gallbladder)', suffix: 'ruq' },
  ] },
  { mod: 'US', bp: 'Kidneys', sub: 'Body', scaffold: 'kidneys', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Renal / KUB' }] },
  { mod: 'US', bp: 'Female Pelvis', sub: 'Body', scaffold: 'female_pelvis', sections: std, fw: ['O-RADS'], v: [
    { c: '', name: 'Ultrasound Pelvis (transabdominal)', sex: 'F' },
    { c: '', name: 'Ultrasound Pelvis (transvaginal)', sex: 'F', suffix: 'tv' },
  ] },
  { mod: 'US', bp: 'Obstetric', sub: 'OB/GYN', scaffold: 'obstetric', sections: std, fw: [], v: [
    { c: '', name: 'Ultrasound Obstetric (first trimester)', sex: 'F', suffix: 't1' },
    { c: '', name: 'Ultrasound Obstetric (second/third trimester)', sex: 'F', suffix: 't2' },
  ] },
  { mod: 'US', bp: 'Scrotum', sub: 'Body', scaffold: 'scrotum', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Scrotum', sex: 'M' }] },
  { mod: 'US', bp: 'Thyroid', sub: 'Head & Neck', scaffold: 'thyroid_us', sections: std, fw: ['TI-RADS'], v: [{ c: '', name: 'Ultrasound Thyroid' }] },
  { mod: 'US', bp: 'Neck', sub: 'Head & Neck', scaffold: 'neck', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Neck / Lymph Nodes' }] },
  { mod: 'US', bp: 'Breast', sub: 'Breast', scaffold: 'breast_us', sections: std, fw: ['BI-RADS'], v: [{ c: '', name: 'Ultrasound Breast' }] },
  { mod: 'US', bp: 'Carotid Arteries', sub: 'Vascular', scaffold: 'carotid', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Carotid Doppler' }] },
  { mod: 'US', bp: 'Peripheral Runoff', sub: 'Vascular', scaffold: 'vascular_doppler', sections: std, fw: [], v: [
    { c: '', name: 'Ultrasound Venous Doppler (DVT)', suffix: 'venous' },
    { c: '', name: 'Ultrasound Arterial Doppler (lower limb)', suffix: 'arterial' },
  ] },
  { mod: 'US', bp: 'Liver', sub: 'Body', scaffold: 'liver', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Liver Doppler (portal)' }] },
  { mod: 'US', bp: 'Renal Arteries', sub: 'Vascular', scaffold: 'vascular_doppler', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Renal Doppler' }] },
  { mod: 'US', bp: 'Shoulder', sub: 'MSK', scaffold: 'shoulder', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Shoulder (rotator cuff)' }] },
  { mod: 'US', bp: 'Hip', sub: 'Paediatric', scaffold: 'msk_joint', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Infant Hip (DDH)' }] },
  { mod: 'US', bp: 'Prostate', sub: 'Body', scaffold: 'prostate', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Prostate (transrectal)', sex: 'M' }] },
  { mod: 'US', bp: 'Neonatal Head', sub: 'Paediatric', scaffold: 'brain', sections: std, fw: [], v: [{ c: '', name: 'Ultrasound Neonatal Cranial' }] },

  // ===================== XR =====================
  { mod: 'XR', bp: 'Chest', sub: 'Thoracic', scaffold: 'cxr', sections: std, fw: [], v: [{ c: '', name: 'Chest X-ray' }] },
  { mod: 'XR', bp: 'Abdomen', sub: 'Body', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Abdominal X-ray (KUB / erect)' }] },
  { mod: 'XR', bp: 'Cervical Spine', sub: 'MSK/Spine', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Cervical Spine X-ray' }] },
  { mod: 'XR', bp: 'Thoracic Spine', sub: 'MSK/Spine', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Thoracic Spine X-ray' }] },
  { mod: 'XR', bp: 'Lumbar Spine', sub: 'MSK/Spine', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Lumbar Spine X-ray' }] },
  { mod: 'XR', bp: 'Whole Spine', sub: 'MSK/Spine', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Whole Spine / Scoliosis X-ray' }] },
  { mod: 'XR', bp: 'Bony Pelvis', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Pelvis X-ray' }] },
  { mod: 'XR', bp: 'Hip', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Hip X-ray' }] },
  { mod: 'XR', bp: 'Knee', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Knee X-ray' }] },
  { mod: 'XR', bp: 'Ankle', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Ankle X-ray' }] },
  { mod: 'XR', bp: 'Foot', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Foot X-ray' }] },
  { mod: 'XR', bp: 'Shoulder', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Shoulder X-ray' }] },
  { mod: 'XR', bp: 'Elbow', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Elbow X-ray' }] },
  { mod: 'XR', bp: 'Wrist', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Wrist X-ray' }] },
  { mod: 'XR', bp: 'Hand', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Hand X-ray' }] },
  { mod: 'XR', bp: 'Humerus', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Humerus X-ray' }] },
  { mod: 'XR', bp: 'Forearm', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Forearm X-ray' }] },
  { mod: 'XR', bp: 'Femur', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Femur X-ray' }] },
  { mod: 'XR', bp: 'Tibia & Fibula', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Tibia & Fibula X-ray' }] },
  { mod: 'XR', bp: 'Facial Bones', sub: 'Head & Neck', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Facial Bones / Nasal X-ray' }] },
  { mod: 'XR', bp: 'Whole Body', sub: 'MSK', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Skeletal Survey' }] },
  { mod: 'XR', bp: 'Hand', sub: 'Paediatric', scaffold: 'bone_xray', sections: std, fw: [], v: [{ c: '', name: 'Bone Age (hand-wrist)', suffix: 'boneage' }] },

  // ===================== MG =====================
  { mod: 'MG', bp: 'Breast', sub: 'Breast', scaffold: 'breast_mg', sections: std, fw: ['BI-RADS'], v: [
    { c: '', name: 'Mammography (screening)', suffix: 'screen' },
    { c: '', name: 'Mammography (diagnostic)', suffix: 'diag' },
    { c: '', name: 'Digital Breast Tomosynthesis', suffix: 'dbt' },
  ] },

  // ===================== NM =====================
  { mod: 'NM', bp: 'Whole Body', sub: 'Nuclear', scaffold: 'nm_bone', sections: std, fw: [], v: [{ c: '', name: 'Bone Scan (whole body)', suffix: 'bone' }] },
  { mod: 'NM', bp: 'Thyroid', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [{ c: '', name: 'Thyroid Scan / Uptake' }] },
  { mod: 'NM', bp: 'Kidneys', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [
    { c: '', name: 'Renal Scan (MAG3/DTPA)', suffix: 'dynamic' },
    { c: '', name: 'Renal Cortical Scan (DMSA)', suffix: 'dmsa' },
  ] },
  { mod: 'NM', bp: 'Biliary System', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [{ c: '', name: 'Hepatobiliary Scan (HIDA)' }] },
  { mod: 'NM', bp: 'Cardiac', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [{ c: '', name: 'Myocardial Perfusion Scan' }] },
  { mod: 'NM', bp: 'Chest', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [{ c: '', name: 'V/Q Lung Scan' }] },
  { mod: 'NM', bp: 'Neck', sub: 'Nuclear', scaffold: 'nm_generic', sections: std, fw: [], v: [{ c: '', name: 'Parathyroid Scan (sestamibi)', suffix: 'pth' }] },

  // ===================== PET =====================
  { mod: 'PET', bp: 'Whole Body', sub: 'Nuclear/Onc', scaffold: 'pet', sections: rec, fw: [], v: [
    { c: 'With', name: 'FDG PET/CT (oncology, whole body)', suffix: 'fdg' },
    { c: 'With', name: 'PSMA PET/CT (prostate)', suffix: 'psma', sex: 'M' },
    { c: 'With', name: 'DOTATATE PET/CT (neuroendocrine)', suffix: 'dota' },
  ] },
  { mod: 'PET', bp: 'Brain', sub: 'Nuclear', scaffold: 'pet', sections: std, fw: [], v: [{ c: 'With', name: 'FDG PET/CT Brain' }] },

  // ===================== FL (Fluoroscopy) =====================
  { mod: 'FL', bp: 'Abdomen', sub: 'GI', scaffold: 'fluoro', sections: proc, fw: [], v: [
    { c: 'With', name: 'Barium Swallow', suffix: 'swallow' },
    { c: 'With', name: 'Barium Meal', suffix: 'meal' },
    { c: 'With', name: 'Barium Follow-through', suffix: 'followthrough' },
    { c: 'With', name: 'Barium / Contrast Enema', suffix: 'enema' },
  ] },
  { mod: 'FL', bp: 'Urinary Tract', sub: 'GU', scaffold: 'fluoro', sections: proc, fw: [], v: [
    { c: 'With', name: 'Micturating Cystourethrogram (MCUG/VCUG)', suffix: 'mcug' },
    { c: 'With', name: 'Intravenous Urogram (IVU)', suffix: 'ivu' },
  ] },
  { mod: 'FL', bp: 'Female Pelvis', sub: 'GYN', scaffold: 'fluoro', sections: proc, fw: [], v: [
    { c: 'With', name: 'Hysterosalpingogram (HSG)', sex: 'F' },
  ] },
];
