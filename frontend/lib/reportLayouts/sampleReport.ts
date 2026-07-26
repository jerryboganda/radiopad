/**
 * Report Templates (RPT-030) — a fictional CT-chest report used to render the
 * designer canvas and gallery previews. Mirrors
 * `backend/RadioPad.Api/src/RadioPad.Api/Services/ReportLayouts/ReportLayoutSampleData.cs`
 * (duplicated, not shared — the backend copy renders server-side PDF/DOCX previews;
 * this one renders the pure-React canvas). No real patient data — the accession
 * "SAMPLE-2041" is clearly fictional.
 */
import type { SectionKey, StudyFieldKey } from './schema';

export interface SampleSignature {
  role: 'Primary radiologist' | 'Co-signer' | 'Addendum';
  signedAt: string;
  note: string;
  hash: string;
}

export interface SampleReport {
  status: string;
  updatedAt: string;
  study: Record<Exclude<StudyFieldKey, 'reportDate' | 'status'>, string>;
  sections: Record<SectionKey, string>;
  signatures: SampleSignature[];
}

export const SAMPLE_REPORT: SampleReport = {
  status: 'Acknowledged',
  updatedAt: new Date().toISOString(),
  study: {
    patientReference: 'SAMPLE-PATIENT',
    accessionNumber: 'SAMPLE-2041',
    modality: 'CT',
    bodyPart: 'Chest',
    contrast: 'With',
    age: '58',
    gender: 'Female',
    comparison: 'CT chest, 14 months prior',
    priorReportSummary: 'Stable 4 mm right upper lobe nodule.',
    departmentTag: 'Thoracic',
  },
  sections: {
    indication: 'Persistent cough and 6 kg unintentional weight loss over three months; further evaluation.',
    technique: 'Contiguous axial CT images of the chest were obtained following intravenous administration '
      + 'of iodinated contrast, with coronal and sagittal reformats. Radiation dose-reduction techniques were applied.',
    comparison: 'CT chest dated 14 months prior.',
    findings: 'Lungs: A stable 4 mm noncalcified nodule is again seen in the right upper lobe, unchanged from '
      + 'prior. No new nodule, consolidation, or ground-glass opacity.\n'
      + 'Airways: Trachea and central airways are patent.\n'
      + 'Pleura: No pleural effusion or pneumothorax.\n'
      + 'Mediastinum/Hila: No mediastinal or hilar lymphadenopathy. Heart size is normal; no pericardial effusion.\n'
      + 'Chest wall/Bones: No aggressive osseous lesion. Mild multilevel degenerative changes of the thoracic spine.\n'
      + 'Upper abdomen (limited): Visualized upper abdominal organs are unremarkable.',
    impression: '1. Stable 4 mm right upper lobe pulmonary nodule, unchanged over 14 months — consistent with a '
      + 'benign etiology; no further dedicated follow-up required.\n'
      + '2. No CT evidence of a mass or consolidation to account for the reported weight loss; clinical correlation recommended.',
    recommendations: 'Correlate clinically; consider outpatient work-up for the reported weight loss if symptoms '
      + 'persist. Routine surveillance only for the pulmonary nodule per Fleischner Society guidance.',
  },
  signatures: [
    {
      role: 'Primary radiologist',
      signedAt: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
      note: 'No discrepancy from preliminary read.',
      hash: 'sample-hash-not-cryptographic',
    },
  ],
};

export function resolveSampleStudyField(field: StudyFieldKey): string {
  if (field === 'reportDate') return new Date(SAMPLE_REPORT.updatedAt).toLocaleDateString();
  if (field === 'status') return SAMPLE_REPORT.status;
  return SAMPLE_REPORT.study[field] || '—';
}
