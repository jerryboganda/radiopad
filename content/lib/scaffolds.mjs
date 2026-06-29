// Anatomical Findings scaffolds (the "Findings" placeholder) per region, plus
// contrast-aware Technique boilerplate per modality. Organ-by-organ, structured
// the way a consultant dictates so the report is complete and checklist-driven.
// Newlines are encoded with \n so they survive JSON serialisation verbatim.

// ---- Technique boilerplate by modality + contrast --------------------------
export function technique(modality, bodyPart, contrast) {
  const region = bodyPart.toLowerCase();
  const c = contrast || '';
  const ctPhase = {
    None: 'without intravenous contrast',
    With: 'after intravenous iodinated contrast',
    WithAndWithout: 'before and after intravenous iodinated contrast',
    '': 'per department protocol',
  }[c];
  const mrPhase = {
    None: 'without intravenous gadolinium',
    With: 'after intravenous gadolinium-based contrast',
    WithAndWithout: 'before and after intravenous gadolinium-based contrast',
    '': 'per department protocol',
  }[c];
  switch (modality) {
    case 'CT': return `Multidetector CT of the ${region} acquired ${ctPhase}. Multiplanar reformats reviewed. State kVp/contrast volume/phase and DLP as per local dose policy.`;
    case 'MR': return `Multiplanar, multisequence MRI of the ${region} ${mrPhase}. State field strength, sequences, and any diffusion/dynamic series.`;
    case 'US': return `Real-time greyscale and colour/spectral Doppler ultrasound of the ${region}. State transducer and approach (e.g. transabdominal/endocavitary).`;
    case 'XR': return `Radiograph(s) of the ${region}. State projections obtained.`;
    case 'MG': return `Digital mammography with standard projections (and tomosynthesis where performed). State views and any spot/magnification work.`;
    case 'NM': return `Scintigraphic imaging of the ${region}. State radiopharmaceutical, administered activity, and acquisition timing.`;
    case 'PET': return `Whole-body PET/CT. State radiotracer, uptake time, blood glucose, and the CT component (low-dose attenuation vs diagnostic, contrast use).`;
    case 'FL': return `Fluoroscopic examination of the ${region}. State contrast medium, volume, screening time and DAP.`;
    default: return `Imaging of the ${region} per department protocol.`;
  }
}

// ---- Organ-by-organ Findings scaffolds, keyed by a scaffold name -----------
// A group names its scaffold; falls back to a modality-generic default.
export const SCAFFOLDS = {
  brain: 'Cerebral hemispheres / grey–white differentiation: \nVentricles and CSF spaces: \nMidline / no shift: \nBasal cisterns: \nPosterior fossa and brainstem: \nExtra-axial spaces (no collection): \nVascular territories (no acute infarct): \nNo intracranial haemorrhage: \nCalvarium and skull base: \nVisualised paranasal sinuses / mastoids / orbits: ',
  brain_stroke: 'Acute infarct (DWI/ADC) — territory and volume: \nASPECTS: \nHaemorrhage (GRE/SWI): \nLarge-vessel occlusion / hyperdense vessel: \nChronic ischaemic change / prior infarct: \nMidline shift / mass effect: \nGrey–white differentiation: ',
  pituitary: 'Pituitary gland height and morphology: \nMicro-/macroadenoma (size, signal, enhancement): \nInfundibulum (midline?): \nOptic chiasm / suprasellar cistern: \nCavernous sinuses (no invasion): \nSphenoid sinus: ',
  sinuses: 'Frontal sinuses: \nEthmoid air cells: \nMaxillary sinuses: \nSphenoid sinuses: \nOstiomeatal complexes: \nNasal septum / turbinates: \nBony walls and anatomic variants (concha bullosa, Haller cells): \nOrbits / skull base: ',
  temporal_bone: 'External auditory canals: \nMiddle ear / ossicular chain: \nMastoid air cells: \nInner ear (cochlea, vestibule, semicircular canals): \nFacial nerve canal: \nInternal auditory canals: \nTegmen / scutum: ',
  neck: 'Pharynx / larynx / supraglottis: \nThyroid and parathyroid beds: \nSalivary glands (parotid, submandibular): \nCervical lymph node levels (I–VII) with size: \nGreat vessels (carotid/jugular): \nAerodigestive tract: \nVisualised lung apices and skull base: ',
  thyroid_us: 'Right lobe (size, echotexture): \nLeft lobe (size, echotexture): \nIsthmus: \nNodules — for each: location, size (3 planes), composition, echogenicity, shape, margin, echogenic foci, ACR TI-RADS points and level: \nCervical lymph nodes (levels, morphology): ',
  cspine: 'Alignment / lordosis: \nVertebral body heights and marrow: \nDiscs C2–T1 (level-by-level: disc height, herniation, canal/foraminal stenosis): \nSpinal cord signal: \nCraniocervical junction: \nPrevertebral soft tissues: \nFacet joints: ',
  tspine: 'Alignment / kyphosis: \nVertebral body heights and marrow: \nDiscs (level-by-level): \nSpinal cord / conus signal: \nCanal and foramina: \nParaspinal soft tissues: \nCostovertebral joints: ',
  lspine: 'Alignment / lordosis: \nVertebral body heights and marrow: \nDiscs L1–S1 (level-by-level: disc height/desiccation, herniation, canal and foraminal stenosis, nerve-root contact): \nConus medullaris (level and signal): \nFacet joints / ligamentum flavum: \nParaspinal soft tissues: \nSacrum / SI joints: ',
  chest_ct: 'Lungs and airways (nodules with 3D size and attenuation, consolidation, emphysema, fibrosis): \nPleura (effusion, thickening, pneumothorax): \nMediastinum and hila (nodes with short-axis size): \nHeart and pericardium: \nGreat vessels: \nThoracic skeleton and chest wall: \nUpper abdomen (visualised): ',
  hrct: 'Distribution (upper/lower, central/peripheral): \nReticulation / traction bronchiectasis: \nHoneycombing: \nGround-glass / mosaic attenuation / air-trapping: \nNodules (centrilobular, perilymphatic, random): \nCysts: \nPattern summary (e.g. UIP/probable UIP/indeterminate/alternative): ',
  ctpa: 'Pulmonary arteries — central to subsegmental (filling defects, most proximal level, laterality): \nClot burden: \nRight-heart strain (RV:LV ratio, septal bowing, contrast reflux): \nLungs (infarct, effusion): \nMediastinum / incidental: ',
  coronary: 'Coronary dominance: \nLeft main: \nLAD (proximal/mid/distal, diagonals): \nLCx (and OMs): \nRCA (and PDA): \nPer-vessel maximal stenosis and plaque type: \nCAD-RADS category and modifiers: \nCalcium score (if performed): \nCardiac chambers / valves / pericardium: ',
  cardiac_mri: 'Biventricular size and systolic function (EF, volumes indexed): \nRegional wall motion: \nMyocardial signal (oedema T2, early/late gadolinium enhancement pattern): \nValves and flow: \nPericardium: \nGreat vessels: ',
  abdomen: 'Liver (size, contour, focal lesion): \nGallbladder and biliary tree: \nPancreas: \nSpleen: \nAdrenals: \nKidneys and ureters: \nBowel: \nPeritoneum / free fluid / free air: \nVasculature (aorta, IVC): \nLymph nodes: \nVisualised lung bases and skeleton: ',
  abdomen_pelvis: 'Liver (size, contour, focal lesion): \nGallbladder and biliary tree: \nPancreas: \nSpleen: \nAdrenals: \nKidneys and ureters: \nBowel and appendix: \nBladder: \nPelvic organs (uterus/adnexa or prostate/seminal vesicles): \nPeritoneum / free fluid / free air: \nVasculature and lymph nodes: \nSkeleton and body wall: ',
  liver: 'Hepatic size, contour and parenchyma: \nFocal observations (number, segment, size, APHE, washout, capsule, growth): \nLI-RADS category per observation: \nPortal/hepatic veins and patency: \nBiliary tree: \nGallbladder, spleen, pancreas: \nAscites / varices / nodes: ',
  pancreas: 'Pancreatic parenchyma (atrophy, mass — size/location/enhancement): \nDuctal system (MPD calibre, abrupt cut-off): \nVascular involvement (SMA, SMV, coeliac, portal): \nPeripancreatic fat / collections: \nBiliary tree / CBD: \nLiver (metastases): \nNodes / ascites: \nResectability statement: ',
  mrcp: 'Intrahepatic ducts: \nCommon hepatic / common bile duct (calibre, stones, stricture): \nPancreatic duct: \nGallbladder and cystic duct: \nAmpullary region: \nLiver / pancreas parenchyma: ',
  kidneys: 'Right kidney (size, cortical thickness, lesion): \nLeft kidney: \nCystic lesions with Bosniak class: \nSolid masses (size, enhancement): \nCollecting systems / hydronephrosis: \nCalculi (size, location, HU): \nUreters and bladder: \nPerinephric fat / vasculature: ',
  adrenal: 'Right adrenal (size, nodule): \nLeft adrenal: \nNodule characterisation (size, unenhanced HU, absolute/relative washout, signal drop on out-of-phase): \nBenign vs indeterminate statement: ',
  prostate: 'Prostate volume and PSA density: \nPeripheral zone (DWI/ADC, T2): \nTransition zone (T2): \nIndex lesion (sector, size, sequence scores, PI-RADS category): \nSecond lesion: \nExtraprostatic extension / seminal vesicle invasion: \nNeurovascular bundles: \nLymph nodes and bones: ',
  female_pelvis: 'Uterus (size, position, endometrial thickness, myometrium/fibroids): \nCervix: \nRight ovary (size, follicles, lesion): \nLeft ovary: \nAdnexal lesion characterisation with O-RADS: \nFree fluid / cul-de-sac: \nBladder and bowel: \nLymph nodes: ',
  rectum: 'Primary tumour (location from anal verge, length, T-stage, EMVI): \nMesorectal fascia involvement (CRM, shortest distance): \nMesorectal and lateral pelvic nodes: \nPeritoneal reflection relationship: \nSphincter complex: \nDistant visualised structures: ',
  obstetric: 'Number of gestations / chorionicity: \nFetal cardiac activity and heart rate: \nBiometry (BPD, HC, AC, FL) and EFW with centile: \nPlacenta (position, relationship to os): \nAmniotic fluid (DVP/AFI): \nFetal anatomy survey: \nCervix and adnexa: ',
  scrotum: 'Right testis (size, echotexture, vascularity): \nLeft testis: \nEpididymes: \nFocal lesions (size, vascularity): \nHydrocele / varicocele: \nInguinal regions: ',
  shoulder: 'Rotator cuff (supraspinatus, infraspinatus, subscapularis, teres minor — tendon thickness, tear type/size): \nLong head of biceps: \nLabrum (including anterosuperior/SLAP): \nGlenohumeral cartilage and bone: \nAcromion morphology / AC joint: \nMuscle bulk and fatty atrophy: \nEffusion / bursa: ',
  knee: 'Menisci (medial, lateral — tear pattern and zone): \nACL / PCL: \nMCL / LCL and posterolateral corner: \nExtensor mechanism: \nArticular cartilage compartments: \nBone marrow (oedema, fracture): \nEffusion / Baker cyst / plicae: ',
  hip: 'Femoral head and acetabulum (cartilage, marrow): \nLabrum: \nFemoroacetabular morphology (CAM/pincer): \nMuscles and tendons (gluteal, iliopsoas, hamstrings): \nMarrow (AVN, fracture): \nJoint effusion / synovium: ',
  msk_joint: 'Osseous structures (alignment, marrow, fracture): \nArticular cartilage: \nLigaments / tendons: \nFibrocartilage / labrum (where applicable): \nJoint effusion / synovium: \nMuscles and soft tissues: ',
  bone_xray: 'Bones (alignment, cortex, fracture — site/pattern/displacement/angulation, acuity): \nJoints (alignment, effusion, arthropathy): \nSoft tissues (swelling, foreign body, gas): \nHardware (if present): ',
  cxr: 'Lungs (focal/diffuse opacity, nodule, volume): \nPleura (effusion, pneumothorax): \nHeart size and mediastinal contours: \nHila: \nBones and soft tissues: \nLines/tubes (position): \nUpper abdomen (free air): ',
  aaa: 'Aortic calibre (max diameter, level): \nMural thrombus / penetrating ulcer / dissection flap: \nBranch vessels (coeliac, SMA, renals, iliacs): \nAneurysm extent and relationship to renal arteries: \nPeri-aortic fat / rupture signs: ',
  vascular_doppler: 'Vessel patency and flow direction: \nPeak systolic / end-diastolic velocities: \nStenosis (% by velocity criteria): \nThrombus (acute vs chronic, compressibility for veins): \nPlaque morphology: \nWaveform analysis: ',
  carotid: 'Right ICA/CCA/ECA (PSV, EDV, ICA/CCA ratio, % stenosis): \nLeft ICA/CCA/ECA: \nPlaque (extent, surface, calcification): \nVertebral arteries (flow direction): \nStenosis category per NASCET/consensus criteria: ',
  breast_mg: 'Breast composition (a–d): \nRight breast (mass, calcifications, asymmetry, architectural distortion): \nLeft breast: \nComparison with prior: \nAxillae: \nBI-RADS category per breast: ',
  breast_us: 'Right breast (mass: shape, orientation, margin, echo pattern, posterior features; size; clock/distance): \nLeft breast: \nAxillary nodes: \nBI-RADS category: ',
  nm_bone: 'Tracer distribution (symmetry): \nFocal abnormal uptake (site, intensity, pattern — benign vs metastatic): \nDegenerative vs aggressive pattern: \nSuperscan / flare considerations: \nSPECT/CT correlation (if performed): ',
  nm_generic: 'Radiopharmaceutical biodistribution: \nTarget-organ uptake / function: \nFocal abnormality (site, intensity): \nQuantification (where applicable): \nSPECT/CT correlation: ',
  pet: 'Tracer-avid disease (site, SUVmax, size): \nNodal stations: \nDistant metastatic sites: \nPhysiologic vs pathologic uptake: \nComparison with prior (Deauville / PERCIST where applicable): \nCT-component findings: ',
  fluoro: 'Contrast transit / opacification: \nMucosal pattern and distensibility: \nFilling defects / strictures / leaks: \nMotility: \nAnatomy-specific findings: ',
  generic: 'Organ-by-organ structured findings: \nRelevant negatives: \nIncidental findings: ',
};

export function scaffold(name, modality) {
  if (name && SCAFFOLDS[name]) return SCAFFOLDS[name];
  const byMod = { CT: 'generic', MR: 'generic', US: 'generic', XR: 'bone_xray', MG: 'breast_mg', NM: 'nm_generic', PET: 'pet', FL: 'fluoro' };
  return SCAFFOLDS[byMod[modality]] || SCAFFOLDS.generic;
}
