'use client';

/**
 * RPT-006 — "Rewrite in my style".
 *
 * Lets the radiologist paste 2–5 prior reports, learn a short style
 * fingerprint, and rewrite the current report in that voice. Backend
 * endpoint is `POST /api/reports/{id}/rewrite?mode=in_my_style` through the
 * typed API client so PHI policy, auth headers, and tenancy stay consistent.
 */

import { useState } from 'react';
import { api } from '@/lib/api';

type Props = {
  reportId: string;
  /** Section to apply the rewrite to ("findings" | "impression" | "recommendations"). */
  section: 'findings' | 'impression' | 'recommendations';
  currentText: string;
  providerId?: string;
  /** Called when the radiologist accepts the rewrite. */
  onAccept: (text: string) => void | Promise<void>;
};

const MIN_SAMPLES = 2;
const MAX_SAMPLES = 5;

export default function RewriteStylePanel({
  reportId,
  section,
  currentText,
  providerId,
  onAccept,
}: Props) {
  const [samples, setSamples] = useState<string[]>(['', '']);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<{ text: string; fingerprint?: string } | null>(null);
  const [showDiff, setShowDiff] = useState(false);

  function updateSample(i: number, value: string) {
    setSamples((prev) => prev.map((s, idx) => (idx === i ? value : s)));
  }

  function addSample() {
    if (samples.length >= MAX_SAMPLES) return;
    setSamples((prev) => [...prev, '']);
  }

  function removeSample(i: number) {
    setSamples((prev) => prev.filter((_, idx) => idx !== i));
  }

  const filled = samples.filter((s) => s.trim().length > 0).length;
  const canLearn = filled >= MIN_SAMPLES && !busy;

  async function learnAndRewrite() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.reports.rewriteInMyStyle(reportId, {
        samples: samples.map((s) => s.trim()).filter((s) => s.length > 0),
        sections: [section],
        providerId,
      });
      setDraft({ text: result.text, fingerprint: result.styleFingerprint });
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  async function accept() {
    if (!draft) return;
    await onAccept(draft.text);
    setDraft(null);
  }

  return (
    <div className="rp-panel">
      <div className="rp-panel-title">
        Rewrite in my style
        <span className="badge ai">AI</span>
      </div>
      <p className="rp-page-sub">
        Paste {MIN_SAMPLES}–{MAX_SAMPLES} of your prior reports below. RadioPad
        will learn a short style fingerprint (vocabulary, sentence length,
        section ordering) and rewrite the current <code>{section}</code> in
        that voice. Samples are sent through the AI gateway; PHI policy still
        applies.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <ul className="rp-list">
        {samples.map((s, i) => (
          <li key={i} className="section-block">
            <label>
              Sample {i + 1}
              {samples.length > MIN_SAMPLES && (
                <button
                  className="subtle"
                  onClick={() => removeSample(i)}
                  style={{ marginLeft: 8 }}
                  aria-label={`Remove sample ${i + 1}`}
                >
                  Remove
                </button>
              )}
            </label>
            <textarea
              className="rp-input"
              rows={4}
              placeholder="Paste a prior report you authored…"
              value={s}
              onChange={(e) => updateSample(i, e.target.value)}
            />
          </li>
        ))}
      </ul>

      <div className="rp-toolbar">
        <button
          className="ghost"
          onClick={addSample}
          disabled={samples.length >= MAX_SAMPLES}
        >
          + Add sample
        </button>
        <button className="primary" onClick={learnAndRewrite} disabled={!canLearn}>
          {busy ? 'Learning…' : 'Learn my style'}
        </button>
        <span className="rp-page-sub">
          {filled}/{MAX_SAMPLES} samples
        </span>
      </div>

      {draft && (
        <>
          <div className="rp-panel-title rp-mt-sm">
            Proposed rewrite
            <span className="badge ai">AI draft</span>
          </div>
          {draft.fingerprint && (
            <p className="rp-page-sub">
              Style fingerprint: <code>{draft.fingerprint}</code>
            </p>
          )}
          {showDiff ? (
            <div className="rp-rewrite-diff">
              <div>
                <div className="rp-stat-label">Original</div>
                <pre className="rp-rewrite-pre">{currentText || '(empty)'}</pre>
              </div>
              <div>
                <div className="rp-stat-label">Proposed</div>
                <div className="ai-mark">
                  <pre className="rp-rewrite-pre">{draft.text}</pre>
                </div>
              </div>
            </div>
          ) : (
            <div className="ai-mark">
              <pre className="rp-rewrite-pre">{draft.text}</pre>
            </div>
          )}
          <div className="rp-toolbar rp-mt-sm">
            <button className="primary" onClick={accept}>Accept</button>
            <button className="ghost" onClick={() => setDraft(null)}>Reject</button>
            <button className="subtle" onClick={() => setShowDiff((v) => !v)}>
              {showDiff ? 'Hide diff' : 'Diff'}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
