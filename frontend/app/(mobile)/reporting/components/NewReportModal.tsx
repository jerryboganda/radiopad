'use client';

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { X, Calendar, User, Hash, Clock, FilePlus } from 'lucide-react';
import { createReport, ReportDto } from '@/lib/api/reportingClient';

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
  const [timestamp, setTimestamp] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      // Auto-fill timestamp when opening modal
      const now = new Date();
      setTimestamp(now.toLocaleString('en-US', {
        dateStyle: 'medium',
        timeStyle: 'short',
      }));
      // Auto-suggest a default Radiology ID if empty
      if (!radiologyId) {
        const randId = Math.floor(1000 + Math.random() * 9000);
        setRadiologyId(`RAD-${now.getFullYear()}-${randId}`);
      }
      setError(null);
    }
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
      });

      if (onSuccess) {
        onSuccess(newReport);
      }
      onClose();
      // Navigate to audio dictate page
      const targetPath = `/reporting/dictate?id=${newReport.id}`;
      router.push(targetPath);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create report. Please try again.';
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      className="rp-modal-backdrop fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fadeIn"
      role="dialog"
      aria-modal="true"
      aria-labelledby="modal-title"
    >
      <div className="rp-modal-container bg-[var(--bg-panel,#181c24)] border border-[var(--border,#2b3245)] rounded-2xl shadow-2xl w-full max-w-md overflow-hidden flex flex-col text-[var(--text,#e2e8f0)]">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border,#2b3245)] bg-[var(--bg-subtle,#1e2330)]">
          <div className="flex items-center gap-2">
            <FilePlus className="w-5 h-5 text-[var(--accent,#3b82f6)]" />
            <h2 id="modal-title" className="text-lg font-semibold text-[var(--text,#f8fafc)]">
              Create New Report
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close modal"
            className="p-1.5 rounded-lg text-slate-400 hover:text-slate-200 hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4 flex-1 overflow-y-auto">
          {error && (
            <div className="p-3 rounded-lg bg-red-950/60 border border-red-800 text-red-300 text-sm">
              {error}
            </div>
          )}

          {/* Radiology ID */}
          <div className="space-y-1.5">
            <label htmlFor="radiologyId" className="block text-xs font-medium text-slate-300 uppercase tracking-wider">
              Radiology ID *
            </label>
            <div className="relative">
              <Hash className="w-4 h-4 absolute left-3 top-3 text-slate-400" />
              <input
                id="radiologyId"
                type="text"
                value={radiologyId}
                onChange={(e) => setRadiologyId(e.target.value)}
                placeholder="e.g. RAD-2026-1042"
                required
                className="rp-input w-full pl-9 pr-3 py-2.5 bg-[var(--bg-app,#0f172a)] border border-[var(--border,#334155)] rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 text-white placeholder-slate-500"
              />
            </div>
          </div>

          {/* Patient Name */}
          <div className="space-y-1.5">
            <label htmlFor="patientName" className="block text-xs font-medium text-slate-300 uppercase tracking-wider">
              Patient Name *
            </label>
            <div className="relative">
              <User className="w-4 h-4 absolute left-3 top-3 text-slate-400" />
              <input
                id="patientName"
                type="text"
                value={patientName}
                onChange={(e) => setPatientName(e.target.value)}
                placeholder="Full Name"
                required
                className="rp-input w-full pl-9 pr-3 py-2.5 bg-[var(--bg-app,#0f172a)] border border-[var(--border,#334155)] rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 text-white placeholder-slate-500"
              />
            </div>
          </div>

          {/* Patient Age & Gender */}
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label htmlFor="patientAge" className="block text-xs font-medium text-slate-300 uppercase tracking-wider">
                Patient Age *
              </label>
              <div className="relative">
                <Calendar className="w-4 h-4 absolute left-3 top-3 text-slate-400" />
                <input
                  id="patientAge"
                  type="number"
                  min="0"
                  max="130"
                  value={patientAge}
                  onChange={(e) => setPatientAge(e.target.value)}
                  placeholder="Age"
                  required
                  className="rp-input w-full pl-9 pr-3 py-2.5 bg-[var(--bg-app,#0f172a)] border border-[var(--border,#334155)] rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 text-white placeholder-slate-500"
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <label htmlFor="patientGender" className="block text-xs font-medium text-slate-300 uppercase tracking-wider">
                Patient Gender *
              </label>
              <select
                id="patientGender"
                value={patientGender}
                onChange={(e) => setPatientGender(e.target.value)}
                className="rp-input w-full px-3 py-2.5 bg-[var(--bg-app,#0f172a)] border border-[var(--border,#334155)] rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 text-white"
              >
                <option value="Male">Male</option>
                <option value="Female">Female</option>
                <option value="Other">Other</option>
              </select>
            </div>
          </div>

          {/* Read-Only System Timestamp */}
          <div className="space-y-1.5">
            <label htmlFor="timestamp" className="block text-xs font-medium text-slate-400 uppercase tracking-wider">
              System Timestamp (Auto-filled)
            </label>
            <div className="relative">
              <Clock className="w-4 h-4 absolute left-3 top-3 text-slate-400" />
              <input
                id="timestamp"
                type="text"
                value={timestamp}
                readOnly
                disabled
                className="rp-input w-full pl-9 pr-3 py-2.5 bg-slate-900/70 border border-slate-800 rounded-xl text-sm text-slate-400 cursor-not-allowed"
              />
            </div>
          </div>

          {/* Actions */}
          <div className="pt-3 flex items-center justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-4 py-2.5 rounded-xl border border-slate-700 text-slate-300 hover:bg-slate-800 text-sm font-medium transition-colors"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-white text-sm font-medium shadow-md shadow-blue-600/30 transition-all flex items-center gap-2"
            >
              {isSubmitting ? (
                <>Creating...</>
              ) : (
                <>Create & Start Dictation</>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
