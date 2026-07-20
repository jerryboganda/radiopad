'use client';

import { useState } from 'react';
import Link from 'next/link';
import { GraduationCap, ShieldCheck } from 'lucide-react';
import {
  api,
  TEACHING_DIFFICULTY_LABELS,
  type TeachingCase,
  type TeachingDifficulty,
} from '@/lib/api';
import { teachingCaseHref } from '@/lib/routes';
import Banner from '@/components/ui/Banner';

const DIFFICULTIES: readonly TeachingDifficulty[] = [0, 1, 2];

export interface SaveAsTeachingCaseButtonProps {
  reportId: string;
  /** Optional seed for the case title (e.g. "CT Abdomen"). */
  defaultTitle?: string;
  className?: string;
}

/**
 * PRD §14.14 TF-001 — "add to teaching file" from the report editor.
 *
 * The de-identification happens SERVER-side: this component never scrubs text
 * itself and never claims to have. It states plainly what the server will strip
 * BEFORE the user commits, because a clinician needs to know what will and will
 * not survive into a shared library — and the notice must not be dismissible or
 * buried behind a tooltip.
 *
 * The new case is always created Private (TF-007). Publishing to the workspace
 * is a separate, deliberate action taken on the case itself.
 */
export default function SaveAsTeachingCaseButton({
  reportId,
  defaultTitle = '',
  className,
}: SaveAsTeachingCaseButtonProps) {
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState(defaultTitle);
  const [diagnosis, setDiagnosis] = useState('');
  const [teachingPoints, setTeachingPoints] = useState('');
  const [tags, setTags] = useState('');
  const [difficulty, setDifficulty] = useState<TeachingDifficulty>(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<TeachingCase | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      const created = await api.teachingCases.createFromReport(reportId, {
        title: title.trim() || undefined,
        diagnosis: diagnosis.trim() || undefined,
        teachingPoints: teachingPoints.trim() || undefined,
        tags: tags.trim() || undefined,
        difficulty,
      });
      setSaved(created);
      setOpen(false);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (saved) {
    return (
      <Banner tone="success" title="Saved to your teaching file">
        The case was de-identified and saved privately.{' '}
        <Link href={teachingCaseHref(saved.id)}>Open the case</Link> to review it or
        publish it to your workspace.
      </Banner>
    );
  }

  if (!open) {
    return (
      <button
        type="button"
        className={className ?? 'primary-ghost'}
        onClick={() => setOpen(true)}
      >
        <GraduationCap size={14} strokeWidth={1.8} aria-hidden /> Save as teaching case
      </button>
    );
  }

  return (
    <div className="rp-panel" role="group" aria-label="Save as teaching case">
      <Banner tone="info" title="This case will be de-identified">
        Patient names, MRNs, the accession number, dates of birth, and explicit
        dates are removed from the history, findings, and impression before the
        case is stored. The accession number and patient reference are never
        saved on a teaching case at all. Review the saved case before you publish
        it to your workspace.
      </Banner>

      {error && <Banner tone="danger" title="Couldn't save the teaching case">{error}</Banner>}

      <label className="rp-field">
        <span>Title</span>
        <input
          className="rp-input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="e.g. Acute appendicitis on CT"
        />
      </label>

      <label className="rp-field">
        <span>Diagnosis</span>
        <input
          className="rp-input"
          value={diagnosis}
          onChange={(e) => setDiagnosis(e.target.value)}
          placeholder="e.g. Acute appendicitis"
        />
      </label>

      <label className="rp-field">
        <span>Teaching points</span>
        <textarea
          className="rp-input"
          rows={3}
          value={teachingPoints}
          onChange={(e) => setTeachingPoints(e.target.value)}
          placeholder="Why is this case worth studying?"
        />
      </label>

      <label className="rp-field">
        <span>Tags</span>
        <input
          className="rp-input"
          value={tags}
          onChange={(e) => setTags(e.target.value)}
          placeholder="Comma separated, e.g. GI, emergency, LI-RADS"
        />
      </label>

      <label className="rp-field">
        <span>Level</span>
        <select
          className="rp-input"
          value={String(difficulty)}
          onChange={(e) => setDifficulty(Number(e.target.value) as TeachingDifficulty)}
          style={{ maxWidth: 220 }}
        >
          {DIFFICULTIES.map((d) => (
            <option key={d} value={d}>{TEACHING_DIFFICULTY_LABELS[d]}</option>
          ))}
        </select>
      </label>

      <div className="rp-card-actions">
        <button type="button" className="primary" disabled={busy} onClick={save}>
          <ShieldCheck size={14} strokeWidth={1.8} aria-hidden />
          {busy ? 'De-identifying…' : 'De-identify and save'}
        </button>
        <button type="button" className="ghost" disabled={busy} onClick={() => setOpen(false)}>
          Cancel
        </button>
      </div>
    </div>
  );
}
