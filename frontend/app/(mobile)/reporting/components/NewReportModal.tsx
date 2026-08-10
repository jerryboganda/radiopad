'use client';

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { X, FilePlus } from 'lucide-react';
import { createReport } from '@/lib/api/reportingClient';
import type { ReportDto } from '@/lib/api/reportingClient';
import { api, type CatalogItem } from '@/lib/api';

export interface NewReportModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: (report: ReportDto) => void;
}

export default function NewReportModal({ isOpen, onClose, onSuccess }: NewReportModalProps) {
  const router = useRouter();
  const [radiologyId, setRadiologyId] = useState('');
  const [patientName, setPatientName] = useState('');
  const [patientAge, setPatientAge] = useState<number | string>('');
  const [patientGender, setPatientGender] = useState('Male');
  const [modality, setModality] = useState('');
  const [bodyPart, setBodyPart] = useState('');
  const [modalityOptions, setModalityOptions] = useState<CatalogItem[]>([]);
  const [bodyPartOptions, setBodyPartOptions] = useState<CatalogItem[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;
    // Auto-suggest a default Radiology ID if empty.
    if (!radiologyId) {
      const randId = Math.floor(1000 + Math.random() * 9000);
      setRadiologyId(`RAD-${new Date().getFullYear()}-${randId}`);
    }
    setError(null);
    // Same tenant-managed catalog the desktop app uses for template/rulebook
    // auto-resolution — keeps mobile-created reports consistent with desktop.
    api.modalities
      .list()
      .then((items) => setModalityOptions(items.filter((m) => m.active)))
      .catch(() => setModalityOptions([]));
    api.bodyParts
      .list()
      .then((items) => setBodyPartOptions(items.filter((b) => b.active)))
      .catch(() => setBodyPartOptions([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!radiologyId.trim() || !patientName.trim() || !patientAge) {
      setError('Please fill in all required fields.');
      return;
    }

    const ageNum = Number(patientAge);
    if (isNaN(ageNum) || ageNum <= 0 || ageNum > 130) {
      setError('Please enter a valid patient age.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const newReport = await createReport({
        radiologyId: radiologyId.trim(),
        patientName: patientName.trim(),
        patientAge: ageNum,
        patientGender,
        modality: modality || undefined,
        bodyPart: bodyPart || undefined,
      });

      if (onSuccess) {
        onSuccess(newReport);
      }
      onClose();
      router.push(`/reporting/dictate?id=${newReport.id}`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create report. Please try again.';
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="rp-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="new-report-modal-title">
      <div className="rp-modal">
        <div className="rp-dictation-card-header">
          <div className="rp-dictation-title">
            <FilePlus size={18} aria-hidden style={{ color: 'var(--accent)' }} />
            <h2 id="new-report-modal-title" style={{ margin: 0, fontSize: 16 }}>
              Create New Report
            </h2>
          </div>
          <button type="button" className="ghost icon-btn" onClick={onClose} aria-label="Close modal">
            <X size={16} />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          {error && <div className="banner danger" role="alert">{error}</div>}

          <div className="rp-field">
            <span>Radiology ID *</span>
            <input
              type="text"
              value={radiologyId}
              onChange={(e) => setRadiologyId(e.target.value)}
              placeholder="e.g. RAD-2026-1042"
              required
              data-testid="new-report-radiology-id"
            />
          </div>

          <div className="rp-field">
            <span>Patient Name *</span>
            <input
              type="text"
              value={patientName}
              onChange={(e) => setPatientName(e.target.value)}
              placeholder="Full name"
              required
              data-testid="new-report-patient-name"
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div className="rp-field">
              <span>Age *</span>
              <input
                type="number"
                min={0}
                max={130}
                value={patientAge}
                onChange={(e) => setPatientAge(e.target.value)}
                placeholder="Age"
                required
                data-testid="new-report-patient-age"
              />
            </div>
            <div className="rp-field">
              <span>Gender *</span>
              <select
                value={patientGender}
                onChange={(e) => setPatientGender(e.target.value)}
                data-testid="new-report-patient-gender"
              >
                <option value="Male">Male</option>
                <option value="Female">Female</option>
                <option value="Other">Other</option>
              </select>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div className="rp-field">
              <span>Modality</span>
              {modalityOptions.length > 0 ? (
                <select
                  value={modality}
                  onChange={(e) => setModality(e.target.value)}
                  data-testid="new-report-modality"
                >
                  <option value="">Select…</option>
                  {modalityOptions.map((m) => (
                    <option key={m.id} value={m.code}>{m.name || m.code}</option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  value={modality}
                  onChange={(e) => setModality(e.target.value)}
                  placeholder="e.g. CT, MRI, X-Ray"
                  data-testid="new-report-modality"
                />
              )}
            </div>
            <div className="rp-field">
              <span>Region of Scan</span>
              {bodyPartOptions.length > 0 ? (
                <select
                  value={bodyPart}
                  onChange={(e) => setBodyPart(e.target.value)}
                  data-testid="new-report-body-part"
                >
                  <option value="">Select…</option>
                  {bodyPartOptions.map((b) => (
                    <option key={b.id} value={b.code}>{b.name || b.code}</option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  value={bodyPart}
                  onChange={(e) => setBodyPart(e.target.value)}
                  placeholder="e.g. Chest, Abdomen"
                  data-testid="new-report-body-part"
                />
              )}
            </div>
          </div>

          <div className="row" style={{ marginTop: 16 }}>
            <button type="button" className="ghost" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </button>
            <button type="submit" className="primary" disabled={isSubmitting} data-testid="new-report-submit">
              {isSubmitting ? 'Creating…' : 'Create & Start Dictation'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
