'use client';

// Iter-36 MOB — touch-friendly draft editor. Each report section
// (Indication, Technique, Comparison, Findings, Impression,
// Recommendations) renders as a collapsible `<details>` panel with a
// 44 px-tall summary tap target. AI-generated text continues to wear
// `.ai-mark` (we read `aiHighlightsJson` to decide which sections).
// Save uses `api.reports.patch` (the existing typed-client method
// matching the spec's `api.reports.update`).
//
// Locked design system: `.rp-mobile`, `.rp-mobile-section`,
// `.rp-mobile-body`, `.ai-mark`, `.banner.*`, button variants.

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type Report } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import { mobileDictateHref, mobileReportSignHref } from '@/lib/routes';

type SectionKey = 'indication' | 'technique' | 'comparison' | 'findings' | 'impression' | 'recommendations';

const SECTIONS: Array<{ key: SectionKey; label: string }> = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'comparison', label: 'Comparison' },
  { key: 'findings', label: 'Findings' },
  { key: 'impression', label: 'Impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

export default function MobileEditPage() {
  const router = useRouter();
  const [reportId, setReportId] = useState<string | null>(null);

  const [report, setReport] = useState<Report | null>(null);
  const [draft, setDraft] = useState<Record<SectionKey, string>>({
    indication: '',
    technique: '',
    comparison: '',
    findings: '',
    impression: '',
    recommendations: '',
  });
  const [aiSections, setAiSections] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setReportId(readQueryParam('reportId'));
  }, []);

  useEffect(() => {
    if (!reportId) return;
    let cancelled = false;
    api.reports
      .get(reportId)
      .then((r) => {
        if (cancelled) return;
        setReport(r);
        setDraft({
          indication: r.indication ?? '',
          technique: r.technique ?? '',
          comparison: r.comparison ?? '',
          findings: r.findings ?? '',
          impression: r.impression ?? '',
          recommendations: r.recommendations ?? '',
        });
        try {
          setAiSections(JSON.parse(r.aiHighlightsJson || '{}'));
        } catch {
          /* noop */
        }
      })
      .catch((e: Error) => setError(e.message));
    return () => {
      cancelled = true;
    };
  }, [reportId]);

  const onChange = useCallback((key: SectionKey, value: string) => {
    setDraft((d) => ({ ...d, [key]: value }));
    setSaved(false);
  }, []);

  const onSave = useCallback(async () => {
    if (!reportId) { setError('Missing report id.'); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await api.reports.patch(reportId, draft);
      setReport(updated);
      setSaved(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save report');
    } finally {
      setSaving(false);
    }
  }, [reportId, draft]);

  return (
    <section className="rp-mobile" aria-label="Edit report">
      <h1 className="rp-page-title">Edit report</h1>
      <p className="rp-page-sub">
        Tap a section to expand and edit. AI-drafted prose stays marked until you accept it.
      </p>

      {error && (
        <div className="banner danger" role="alert">
          {error}
        </div>
      )}
      {saved && (
        <div className="banner info" role="status">
          Saved.
        </div>
      )}

      {reportId === '' && (
        <div className="banner warn" role="alert">
          Missing report id.
        </div>
      )}

      {!report && !error && <div className="rp-page-sub">Loading…</div>}

      {report &&
        SECTIONS.map(({ key, label }) => {
          const isAi = !!aiSections[key];
          return (
            <details
              key={key}
              className="rp-mobile-section"
              data-testid={`section-${key}`}
              open={key === 'findings'}
            >
              <summary>
                <span>{label}</span>
                {isAi ? <span className="badge ai">AI draft</span> : null}
              </summary>
              <div className="rp-mobile-body">
                {isAi ? (
                  <div className="ai-mark">
                    <textarea
                      aria-label={label}
                      value={draft[key]}
                      onChange={(e) => onChange(key, e.target.value)}
                      data-testid={`textarea-${key}`}
                    />
                  </div>
                ) : (
                  <textarea
                    aria-label={label}
                    value={draft[key]}
                    onChange={(e) => onChange(key, e.target.value)}
                    data-testid={`textarea-${key}`}
                  />
                )}
              </div>
            </details>
          );
        })}

      <div className="rp-row between rp-mt-sm">
        <button type="button" className="ghost" onClick={() => { if (reportId) router.push(mobileDictateHref(reportId)); }}>
          Dictate
        </button>
        <div className="rp-row rp-gap-sm">
          <button
            type="button"
            className="subtle"
            onClick={() => { if (reportId) router.push(mobileReportSignHref(reportId)); }}
            disabled={saving}
          >
            Review &amp; sign
          </button>
          <button
            type="button"
            className="primary"
            onClick={onSave}
            disabled={saving || !report}
            data-testid="save-btn"
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </section>
  );
}
