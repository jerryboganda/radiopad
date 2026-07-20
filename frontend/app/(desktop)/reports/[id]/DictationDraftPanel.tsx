'use client';

/**
 * Dictation-engine brief §4.2 — the SAFETY-CHECKED dictation→report draft panel.
 *
 * Sends raw dictation to `POST /api/reports/{id}/dictation/draft`, which runs the
 * deterministic pass-through (§5.2) → formatter → validation-diff (§5.3) → sentinel
 * (§5.6) pipeline and writes the local audit (§5.7). Unlike plain cleanup, this panel
 * surfaces the SAFETY outcome: whether the AI output was accepted or rejected (fail-safe
 * fallback), the validation violations (blocker/red), and the laterality/negation/gender
 * warnings that need eye-confirmation (warning/amber). The draft is editable and still
 * gated by the §5.5 sign-off flow — RadioPad never auto-signs.
 */

import { useEffect, useState } from 'react';
import { api, type DictationDraftResult, type DictationSectionKey } from '@/lib/api';
import { resolveCorrections } from '@/lib/dictation/resolveCorrections';
import Banner from '@/components/ui/Banner';
import StatusBadge from '@/components/ui/StatusBadge';

type Props = {
  reportId: string;
  /** Optional seed for the dictation textarea (e.g. the current Findings text). */
  initialText?: string;
  /** Applies the drafted sections to the report. */
  onApply: (sections: Partial<Record<DictationSectionKey, string>>) => void | Promise<void>;
};

const SECTION_ORDER: DictationSectionKey[] = [
  'indication', 'technique', 'findings', 'impression', 'recommendations',
];

const SECTION_LABELS: Record<DictationSectionKey, string> = {
  indication: 'Indication',
  technique: 'Technique',
  findings: 'Findings',
  impression: 'Impression',
  recommendations: 'Recommendations',
};

/**
 * Title the warning banner after what actually fired.
 *
 * DictationEngineService merges the F4 deterministic checks (measurement sanity, findings-vs-
 * impression consistency) into the same `sentinelWarnings` channel as the §5.6 laterality /
 * negation / sex sentinel. The banner hardcoded the §5.6 wording, so a report flagged only for an
 * implausible measurement told the radiologist to check for a "possible laterality / negation / sex
 * mismatch" — sending them looking for a side error that had not occurred, and teaching them that
 * the most safety-critical banner in the product is often about something else.
 */
function sentinelBannerTitle(warnings: { kind: string }[]): string {
  const kinds = new Set(warnings.map((w) => w.kind));
  const safety = ['Laterality', 'Negation', 'Gender'].some((k) => kinds.has(k));
  const consistency = ['Consistency', 'MeasurementSanity'].some((k) => kinds.has(k));

  if (safety && consistency) return 'Requires review — safety and consistency checks flagged this draft';
  if (safety) return 'Requires review — possible laterality / negation / sex mismatch';
  if (consistency) return 'Requires review — measurement or findings/impression inconsistency';
  return 'Requires review — a deterministic safety check flagged this draft';
}

export default function DictationDraftPanel({ reportId, initialText, onApply }: Props) {
  const [raw, setRaw] = useState(initialText ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<DictationDraftResult | null>(null);
  const [applied, setApplied] = useState(false);
  // Offline formatting is DESKTOP-only and OPT-IN: decision D1 keeps the cloud formatter the
  // default. Until this existed, `api.reports.dictationDraftLocal` had no caller at all — the whole
  // on-device MedGemma path was unreachable from the UI no matter how it was provisioned.
  const [onDevice, setOnDevice] = useState(false);
  const [canRunOnDevice, setCanRunOnDevice] = useState(false);
  const [fellBack, setFellBack] = useState(false);

  useEffect(() => {
    setCanRunOnDevice(typeof window !== 'undefined' && '__TAURI__' in window);
  }, []);

  async function run() {
    const text = raw.trim();
    if (!text) {
      setError('Enter or paste some dictation to format.');
      return;
    }
    setBusy(true);
    setError(null);
    setApplied(false);
    setFellBack(false);
    try {
      if (onDevice && canRunOnDevice) {
        try {
          // The on-device endpoint is stateless (PHI never reaches the server), so it cannot look
          // the correction dictionary up the way the report-scoped cloud path does. Resolve it here
          // and pass it in — otherwise switching to on-device would silently drop every correction
          // the radiologist has configured, and the same dictation would produce different text
          // depending only on WHERE it was formatted.
          const [lexicon, userCorrections] = await Promise.all([
            api.lexicon.list().catch(() => []),
            api.userCorrections.list().catch(() => []),
          ]);
          setResult(
            await api.reports.dictationDraftLocal(text, {
              corrections: resolveCorrections(lexicon, userCorrections),
            }),
          );
          return;
        } catch {
          // The on-device path 503s until the model + llama-server are provisioned. Falling back to
          // the cloud formatter keeps the radiologist working, but it must be VISIBLE — silently
          // sending PHI to the cloud after the user asked for on-device would be a privacy
          // surprise, not a convenience.
          setFellBack(true);
        }
      }
      setResult(await api.reports.dictationDraft(reportId, text));
    } catch (e) {
      const err = e as { body?: { error?: string; detail?: string }; message: string };
      setError(err.body?.error || err.body?.detail || err.message || 'Formatting failed.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    if (!result) return;
    await onApply(result.sections);
    setApplied(true);
  }

  const populatedSections = result
    ? SECTION_ORDER.filter((k) => (result.sections[k] ?? '').trim().length > 0)
    : [];

  return (
    <div className="rp-panel rp-anim-scale-in">
      <div className="rp-panel-title">
        Format dictation
        <StatusBadge tone="ai">AI · safety-checked</StatusBadge>
      </div>
      <p className="rp-page-sub">
        Runs your dictation through the on-device safety pipeline: numbers, measurements,
        laterality and dates are locked deterministically before the model (§5.2); the output is
        rejected and your dictation shown instead if the model fabricates a value or drops a
        section (§5.3); left/right, negation and sex mismatches are flagged for review (§5.6).
        The draft is editable and still requires sign-off.
      </p>

      {error && <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>}

      {fellBack && (
        <Banner tone="warn" onDismiss={() => setFellBack(false)}>
          On-device formatting was unavailable, so this draft was formatted in the cloud. Download
          the MedGemma model in Settings → On-device models to keep dictation on this machine.
        </Banner>
      )}

      {canRunOnDevice && (
        <div className="section-block">
          <label
            htmlFor="rp-dictation-on-device"
            className="rp-row"
            style={{ gap: 8, alignItems: 'flex-start', cursor: 'pointer' }}
          >
            <input
              id="rp-dictation-on-device"
              type="checkbox"
              checked={onDevice}
              onChange={(e) => setOnDevice(e.target.checked)}
              data-testid="dictation-on-device-toggle"
            />
            <span>
              Format on this device (offline)
              <span className="rp-page-sub" style={{ display: 'block' }}>
                Keeps the dictation on this machine instead of sending it to the cloud formatter.
                Requires the MedGemma model; falls back to the cloud if it is not installed.
              </span>
            </span>
          </label>
        </div>
      )}

      <div className="section-block">
        <label htmlFor="rp-dictation-raw">Dictation</label>
        <textarea
          id="rp-dictation-raw"
          className="rp-input"
          rows={5}
          placeholder="Dictate or paste the raw report narrative…"
          value={raw}
          onChange={(e) => setRaw(e.target.value)}
        />
      </div>

      <div className="rp-toolbar">
        <button className="primary-ghost" onClick={run} disabled={busy || !raw.trim()} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />}
          {busy ? 'Formatting…' : 'Format (safety-checked)'}
        </button>
        <span className="rp-page-sub">{raw.trim().length} chars</span>
      </div>

      {result && (
        <div className="rp-anim-fade-in-up">
          {result.usedFallback ? (
            <Banner tone="danger" title="AI output rejected — showing your dictation (fail-safe)">
              The safety validator (§5.3) rejected the formatted output, so your
              dictionary-corrected dictation is shown instead. Nothing was fabricated.
              {result.violations.length > 0 && (
                <ul className="rp-list rp-mt-sm">
                  {result.violations.map((v, i) => (
                    <li key={i}>{v.detail}</li>
                  ))}
                </ul>
              )}
            </Banner>
          ) : (
            <Banner tone="success" title="Passed the safety validator">
              No fabricated numbers, measurements or dates; required sections present (§5.3).
            </Banner>
          )}

          {/* F6 — rulebook findings over the drafted text. Severity mapping is the documented one:
              Blocker→red, Warning→amber, Info/Style→blue. Shown here so the radiologist sees a
              missing required section or a forbidden term while deciding whether to accept the
              draft, instead of after applying it and running Validate. */}
          {result.validation && result.validation.findings.length > 0 && (
            <Banner
              tone={result.validation.blockerPresent ? 'danger' : 'warn'}
              title={
                result.validation.blockerPresent
                  ? 'Rulebook blockers in this draft'
                  : 'Rulebook notes on this draft'
              }
            >
              <ul className="rp-list">
                {result.validation.findings.map((f, i) => (
                  <li key={i}>
                    <strong>{f.severity}</strong>
                    {f.section ? ` · ${f.section}` : ''}: {f.message}{' '}
                    <span className="rule"><code>{f.ruleId}</code></span>
                  </li>
                ))}
              </ul>
            </Banner>
          )}

          {result.sentinelWarnings.length > 0 && (
            <Banner tone="warn" title={sentinelBannerTitle(result.sentinelWarnings)}>
              <ul className="rp-list">
                {result.sentinelWarnings.map((w, i) => (
                  <li key={i}>
                    <strong>{w.kind}:</strong> {w.detail}
                  </li>
                ))}
              </ul>
            </Banner>
          )}

          <div className="rp-panel-title rp-mt-sm">
            Draft
            <StatusBadge tone="warning">Requires review</StatusBadge>
          </div>

          {populatedSections.length === 0 ? (
            <p className="rp-page-sub">The formatter returned no populated sections.</p>
          ) : (
            populatedSections.map((k) => (
              <div key={k} className="section-block">
                <div className="rp-stat-label">{SECTION_LABELS[k]}</div>
                <div className="ai-mark">
                  <pre className="rp-rewrite-pre">{result.sections[k]}</pre>
                </div>
              </div>
            ))
          )}

          <div className="rp-toolbar rp-mt-sm">
            <button className="primary" onClick={apply} disabled={populatedSections.length === 0 || applied}>
              {applied ? 'Applied' : 'Apply to report'}
            </button>
            <button className="ghost" onClick={() => { setResult(null); setApplied(false); }}>
              Discard
            </button>
          </div>

          <p className="rp-page-sub rp-mt-sm">
            {result.provider}
            {result.model ? ` · ${result.model}` : ''}
            {result.latencyMs ? ` · ${result.latencyMs} ms` : ''}
          </p>
        </div>
      )}
    </div>
  );
}
