'use client';

import { useEffect, useState, useCallback, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import {
  api,
  type Report,
  type ValidationFinding,
  type Provider,
  type Rulebook,
  type ReportTemplate,
  type RewriteMode,
  type ReportSignature,
} from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import { mobileDictateHref } from '@/lib/routes';
import { detectCommand, stripCommand, type VoiceCommand, type CommandMatch } from '@/lib/voiceCommands';
import RewriteStylePanel from './RewriteStylePanel';
import PriorComparePanel from './PriorComparePanel';
import CopyToRisButton from './CopyToRisButton';

const SECTIONS: Array<{ key: keyof Report; label: string; cls?: string }> = [
  { key: 'indication', label: 'Indication' },
  { key: 'technique', label: 'Technique' },
  { key: 'comparison', label: 'Comparison' },
  { key: 'findings', label: 'Findings', cls: 'findings' },
  { key: 'impression', label: 'Impression', cls: 'impression' },
  { key: 'recommendations', label: 'Recommendations' },
];

const REWRITE_MODES: Array<{ mode: RewriteMode; label: string; hint: string }> = [
  { mode: 'concise', label: 'Concise', hint: 'Shorter, denser prose' },
  { mode: 'formal', label: 'Formal', hint: 'Strict radiology register' },
  { mode: 'patient_friendly', label: 'Patient-friendly', hint: 'Plain-language summary for the patient' },
  { mode: 'referring_summary', label: 'Referring summary', hint: 'Brief note for the referring clinician' },
];

const REWRITABLE_KEYS: Array<keyof Report> = ['findings', 'impression', 'recommendations'];

type RewriteState = {
  mode: RewriteMode;
  section: keyof Report;
  original: string;
  proposed: string;
  diff: boolean;
};

export default function ReportPage() {
  const router = useRouter();
  const [id, setId] = useState<string | null>(null);
  const [report, setReport] = useState<Report | null>(null);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [templates, setTemplates] = useState<ReportTemplate[]>([]);
  const [findings, setFindings] = useState<ValidationFinding[]>([]);
  const [qualityScore, setQualityScore] = useState<number | null>(null);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiHighlights, setAiHighlights] = useState<Record<string, boolean>>({});
  const [providerId, setProviderId] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [rewriteOpen, setRewriteOpen] = useState(false);
  const [rewriteSection, setRewriteSection] = useState<keyof Report>('impression');
  const [rewriteBusy, setRewriteBusy] = useState(false);
  const [rewriteDraft, setRewriteDraft] = useState<RewriteState | null>(null);
  const [signatures, setSignatures] = useState<ReportSignature[]>([]);
  const [signBusy, setSignBusy] = useState(false);
  const [signNote, setSignNote] = useState('');
  const [addendumBody, setAddendumBody] = useState('');
  const [addendumOpen, setAddendumOpen] = useState(false);
  const [stylePanelOpen, setStylePanelOpen] = useState(false);
  const [styleSection, setStyleSection] =
    useState<'findings' | 'impression' | 'recommendations'>('impression');
  const [showPrior, setShowPrior] = useState(false);
  const [voiceCommandMode, setVoiceCommandMode] = useState(false);
  const [voiceCommandPills, setVoiceCommandPills] = useState<Array<{ id: number; command: VoiceCommand }>>([]);
  const voicePillIdRef = { current: 0 };

  useEffect(() => {
    setId(readQueryParam('id'));
  }, []);

  useEffect(() => {
    if (!id) return;
    api.reports.get(id).then((r) => {
      setReport(r);
      try { setAiHighlights(JSON.parse(r.aiHighlightsJson || '{}')); } catch { /* noop */ }
    }).catch((e: Error) => setError(e.message));
    api.providers.list().then((p) => {
      setProviders(p);
      const def = p.find((x) => x.enabled) || p[0];
      if (def) setProviderId(def.id);
    });
    api.rulebooks.list().then(setRulebooks);
    api.templates.list().then(setTemplates).catch(() => {});
    api.reports.signatures(id).then(setSignatures).catch(() => setSignatures([]));
  }, [id]);

  const update = useCallback(async (patch: Partial<Report>) => {
    if (!report) return;
    setSaving(true);
    try {
      const next = await api.reports.patch(report.id, patch);
      setReport(next);
    } finally {
      setSaving(false);
    }
  }, [report]);

  async function refreshSignatures() {
    if (!report) return;
    try { setSignatures(await api.reports.signatures(report.id)); } catch { /* noop */ }
  }

  async function runAi(mode: string) {
    if (!report || !providerId) return;
    setAiBusy(true);
    setError(null);
    try {
      const out = await api.reports.runAi(report.id, { mode: mode as 'impression', providerId });
      const nextHighlights = { ...aiHighlights, impression: true };
      setAiHighlights(nextHighlights);
      await update({ impression: out.text, aiHighlightsJson: JSON.stringify(nextHighlights) });
    } catch (e) {
      const err = e as { body?: { error?: string; kind?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setAiBusy(false);
    }
  }

  async function runRewrite(mode: RewriteMode) {
    if (!report) return;
    setRewriteOpen(false);
    setRewriteBusy(true);
    setError(null);
    try {
      const original = String((report as Record<string, unknown>)[rewriteSection as string] ?? '');
      const result = await api.reports.rewrite(report.id, {
        mode,
        sections: [rewriteSection as string],
        providerId: providerId || undefined,
      });
      setRewriteDraft({ mode, section: rewriteSection, original, proposed: result.text, diff: false });
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setRewriteBusy(false);
    }
  }

  async function acceptRewrite() {
    if (!report || !rewriteDraft) return;
    const key = rewriteDraft.section;
    const next = { ...aiHighlights, [key as string]: true };
    setAiHighlights(next);
    await update({ [key]: rewriteDraft.proposed, aiHighlightsJson: JSON.stringify(next) } as Partial<Report>);
    setRewriteDraft(null);
  }

  async function validate() {
    if (!report) return;
    const v = await api.reports.validate(report.id);
    setFindings(v.findings);
    setQualityScore(v.qualityScore);
  }

  async function applyTemplate(templatePk: string) {
    if (!report || !templatePk) return;
    const t = templates.find((x) => x.id === templatePk);
    if (!t) return;
    let sections: Array<{ id: string; placeholder?: string }> = [];
    try {
      const parsed = JSON.parse(t.sectionsJson) as { sections?: typeof sections } | typeof sections;
      sections = Array.isArray(parsed) ? parsed : parsed.sections ?? [];
    } catch { return; }
    const patch: Partial<Report> = { templateId: t.id };
    const map: Record<string, keyof Report> = {
      indication: 'indication', technique: 'technique', comparison: 'comparison',
      findings: 'findings', impression: 'impression', recommendations: 'recommendations',
    };
    for (const s of sections) {
      const key = map[s.id];
      if (!key) continue;
      const current = (report as Record<string, unknown>)[key as string];
      if (!current || (typeof current === 'string' && current.trim().length === 0)) {
        (patch as Record<string, unknown>)[key as string] = s.placeholder ?? '';
      }
    }
    await update(patch);
  }

  async function acknowledge() {
    if (!report) return;
    if (!confirm('Acknowledge this AI-assisted draft? AI text will be marked as reviewed.')) return;
    const next = await api.reports.acknowledge(report.id);
    setAiHighlights({});
    await update({ aiHighlightsJson: '{}' });
    setReport(next);
  }

  /**
   * PRD Beta #5 — Voice Command Mode. When enabled, incoming dictation
   * transcript is checked for command phrases before appending.
   */
  function handleVoiceCommandTranscript(transcript: string): string {
    if (!voiceCommandMode || !report) return transcript;
    const match = detectCommand(transcript);
    if (!match) return transcript;

    // Show auto-dismiss badge pill
    const pillId = ++voicePillIdRef.current;
    setVoiceCommandPills((prev) => [...prev, { id: pillId, command: match.command }]);
    setTimeout(() => {
      setVoiceCommandPills((prev) => prev.filter((p) => p.id !== pillId));
    }, 3000);

    // Execute command
    void executeDesktopCommand(match.command);
    return stripCommand(transcript, match);
  }

  async function executeDesktopCommand(command: VoiceCommand) {
    if (!report) return;
    setAiBusy(true);
    setError(null);
    try {
      switch (command) {
        case 'generate_impression':
          await runAi('impression');
          return; // runAi handles setAiBusy
        case 'make_concise':
          await runRewrite('concise');
          break;
        case 'make_formal':
          await runRewrite('formal');
          break;
        case 'patient_friendly':
          await runRewrite('patient_friendly');
          break;
        case 'validate_report':
          await validate();
          break;
        case 'cleanup_dictation': {
          const current = await api.reports.get(report.id);
          await api.reports.cleanupDictation(report.id, current.findings || '');
          break;
        }
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setAiBusy(false);
    }
  }

  async function signAsPrimary() {
    if (!report) return;
    setSignBusy(true);
    setError(null);
    try {
      await api.reports.sign(report.id, { role: 'Primary', note: signNote || undefined });
      setSignNote('');
      await refreshSignatures();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSignBusy(false);
    }
  }

  async function addCoSigner() {
    if (!report) return;
    setSignBusy(true);
    setError(null);
    try {
      await api.reports.sign(report.id, { role: 'CoSigner', note: signNote || undefined });
      setSignNote('');
      await refreshSignatures();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSignBusy(false);
    }
  }

  async function submitAddendum() {
    if (!report || !addendumBody.trim()) return;
    setSignBusy(true);
    setError(null);
    try {
      await api.reports.addAddendum(report.id, addendumBody.trim(), signNote || undefined);
      setAddendumBody('');
      setSignNote('');
      setAddendumOpen(false);
      await refreshSignatures();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSignBusy(false);
    }
  }

  async function exportFhir() {
    if (!report) return;
    const fhir = await api.reports.exportFhir(report.id);
    const blob = new Blob([JSON.stringify(fhir, null, 2)], { type: 'application/fhir+json' });
    downloadBlob(blob, `${report.study.accessionNumber || report.id}.fhir.json`);
  }

  async function exportJson() {
    if (!report) return;
    const json = await api.reports.exportJson(report.id);
    const blob = new Blob([JSON.stringify(json, null, 2)], { type: 'application/json' });
    downloadBlob(blob, `${report.study.accessionNumber || report.id}.json`);
  }

  async function exportText() {
    if (!report) return;
    const text = await api.reports.exportText(report.id);
    downloadBlob(new Blob([text], { type: 'text/plain' }), `${report.study.accessionNumber || report.id}.txt`);
  }

  async function exportPdf() {
    if (!report) return;
    const blob = await api.reports.exportPdf(report.id);
    downloadBlob(blob, `${report.study.accessionNumber || report.id}.pdf`);
  }

  async function exportDocx() {
    if (!report) return;
    const blob = await api.reports.exportDocx(report.id);
    downloadBlob(blob, `${report.study.accessionNumber || report.id}.docx`);
  }

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const generate = () => { void runAi('impression'); };
    const rewrite = () => setRewriteOpen(true);
    const dictate = () => {
      const reportId = report?.id ?? id;
      if (reportId) router.push(mobileDictateHref(reportId));
    };

    window.addEventListener('radiopad:generate-impression', generate);
    window.addEventListener('radiopad:rewrite', rewrite);
    window.addEventListener('radiopad:dictate', dictate);
    return () => {
      window.removeEventListener('radiopad:generate-impression', generate);
      window.removeEventListener('radiopad:rewrite', rewrite);
      window.removeEventListener('radiopad:dictate', dictate);
    };
  }, [id, report?.id, providerId, router]);

  const primarySigned = useMemo(
    () => signatures.some((s) => normalizeRole(s.role) === 'Primary'),
    [signatures],
  );

  if (error && !report) return <div className="rp-container"><div className="banner warn">{error}</div></div>;
  if (id === null) return <div className="rp-container"><p style={{ color: 'var(--text-muted)' }}>Loading report…</p></div>;
  if (!id) return <div className="rp-container"><div className="banner warn">Missing report id.</div></div>;
  if (!report) return <div className="rp-container"><p style={{ color: 'var(--text-muted)' }}>Loading report…</p></div>;

  const blockers = findings.filter((f) => f.severity === 'Blocker' || (f.severity as unknown as number) === 2).length;
  const exportAllowed = statusLabel(report.status) === 'Acknowledged' || statusLabel(report.status) === 'Exported';
  const exportTitle = exportAllowed ? undefined : 'Acknowledge report before exporting';

  return (
    <div className="rp-container">
      <button className="ghost" onClick={() => router.push('/')}>← Back</button>
      <h1 className="rp-page-title" style={{ marginTop: 8 }}>
        {report.study.modality} {report.study.bodyPart} — <code>{report.study.accessionNumber}</code>
      </h1>
      <p className="rp-page-sub">
        {saving ? 'Saving…' : 'Auto-saved'} · Status:{' '}
        <span className="badge">{statusLabel(report.status)}</span>
        {primarySigned && <> · <span className="badge ok">Signed</span></>}
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-workspace">
        {/* Study context */}
        <div className="rp-panel">
          <div className="rp-panel-title">Study context</div>
          <div className="section-block">
            <label>Modality</label>
            <input value={report.study.modality} readOnly />
          </div>
          <div className="section-block">
            <label>Body part</label>
            <input value={report.study.bodyPart} readOnly />
          </div>
          <div className="section-block">
            <label>Indication</label>
            <input value={report.study.indication} readOnly />
          </div>
          <div className="section-block">
            <label>Rulebook</label>
            <select
              value={report.rulebookId || ''}
              onChange={(e) => update({ rulebookId: e.target.value || null })}
            >
              <option value="">— none —</option>
              {rulebooks.map((rb) => (
                <option key={rb.id} value={rb.id}>{rb.name} ({rb.version})</option>
              ))}
            </select>
          </div>
          <div className="section-block">
            <label>Template (apply scaffolding)</label>
            <select
              defaultValue=""
              onChange={(e) => applyTemplate(e.target.value)}
            >
              <option value="">— none —</option>
              {templates.map((t) => (
                <option key={t.id} value={t.id}>{t.name}</option>
              ))}
            </select>
          </div>
          <div className="section-block">
            <label>AI provider</label>
            <select value={providerId} onChange={(e) => setProviderId(e.target.value)}>
              {providers.map((p) => (
                <option key={p.id} value={p.id} disabled={!p.enabled}>{p.name}</option>
              ))}
            </select>
          </div>
        </div>

        {/* Editor */}
        <div>
          <div className="rp-toolbar">
            <button className="primary" disabled={aiBusy || !providerId} onClick={() => runAi('impression')}>
              {aiBusy ? '…' : 'Generate impression'}
            </button>

            <div className="rp-rewrite-menu">
              <button
                className="primary-ghost"
                disabled={rewriteBusy}
                aria-haspopup="menu"
                aria-expanded={rewriteOpen}
                onClick={() => setRewriteOpen((v) => !v)}
              >
                {rewriteBusy ? 'Rewriting…' : 'Rewrite ▾'}
              </button>
              {rewriteOpen && (
                <div className="rp-rewrite-popover" role="menu">
                  <div className="section-block">
                    <label>Section</label>
                    <select
                      className="rp-input"
                      value={rewriteSection as string}
                      onChange={(e) => setRewriteSection(e.target.value as keyof Report)}
                    >
                      {REWRITABLE_KEYS.map((k) => (
                        <option key={k as string} value={k as string}>
                          {SECTIONS.find((s) => s.key === k)?.label ?? (k as string)}
                        </option>
                      ))}
                    </select>
                  </div>
                  <ul className="rp-list">
                    {REWRITE_MODES.map((m) => (
                      <li key={m.mode} className="rp-rewrite-option">
                        <button className="subtle" role="menuitem" onClick={() => runRewrite(m.mode)}>
                          <span className="rp-rewrite-option-label">{m.label}</span>
                          <span className="rp-rewrite-option-hint">{m.hint}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>

            <button
              className="primary-ghost"
              onClick={() => setStylePanelOpen((v) => !v)}
              aria-expanded={stylePanelOpen}
            >
              {stylePanelOpen ? 'Close style rewrite' : 'Rewrite in my style'}
            </button>
            <button className="ghost" onClick={() => setShowPrior((v) => !v)} aria-expanded={showPrior}>
              {showPrior ? 'Hide prior' : 'Compare prior'}
            </button>
            <button className="ghost" onClick={validate}>Validate</button>
            <button
              className="ghost"
              onClick={() => setVoiceCommandMode((v) => !v)}
              aria-pressed={voiceCommandMode}
              data-testid="voice-command-toggle"
            >
              {voiceCommandMode ? '🎙 Voice Cmds On' : '🎙 Voice Cmds'}
            </button>
            {voiceCommandPills.map((pill) => (
              <span key={pill.id} className="badge" data-testid="voice-command-pill">
                {pill.command}
              </span>
            ))}
            <CopyToRisButton reportId={report.id} />
            <button className="ghost" disabled={!exportAllowed} title={exportTitle} onClick={exportText}>Export text</button>
            <button className="ghost" disabled={!exportAllowed} title={exportTitle} onClick={exportJson}>Export JSON</button>
            <button className="ghost" disabled={!exportAllowed} title={exportTitle} onClick={exportFhir}>Export FHIR</button>
            <button className="ghost" disabled={!exportAllowed} title={exportTitle} onClick={exportPdf}>Export PDF</button>
            <button className="ghost" disabled={!exportAllowed} title={exportTitle} onClick={exportDocx}>Export DOCX</button>
            <button className="primary-ghost" disabled={blockers > 0} onClick={acknowledge}>
              Acknowledge & lock
            </button>
          </div>

          {Object.values(aiHighlights).some(Boolean) && (
            <div className="banner ai">
              AI-generated text is highlighted below — review every section before acknowledging.
            </div>
          )}

          {SECTIONS.map(({ key, label, cls }) => {
            const isNarrative = key === 'findings' || key === 'impression' || key === 'recommendations';
            return (
              <div
                className={`section-block ${isNarrative ? 'rp-narrative' : ''}`}
                key={key as string}
                id={key === 'findings' ? 'rp-findings-section' : undefined}
                data-section={key as string}
              >
                <label>{label}</label>
                <div className={aiHighlights[key as string] ? 'ai-mark' : ''}>
                  <textarea
                    className={cls}
                    value={(report as Record<string, unknown>)[key as string] as string}
                    onChange={(e) => {
                      const next = { ...aiHighlights };
                      if (next[key as string]) delete next[key as string];
                      setAiHighlights(next);
                      setReport({ ...report, [key]: e.target.value });
                    }}
                    onBlur={(e) =>
                      update({
                        [key]: e.target.value,
                        aiHighlightsJson: JSON.stringify(aiHighlights),
                      } as Partial<Report>)
                    }
                  />
                </div>
              </div>
            );
          })}
        </div>

        {/* Right pane: validation + rewrite preview + signatures */}
        <div>
          {stylePanelOpen && (
            <>
              <div className="rp-panel">
                <div className="rp-panel-title">Style rewrite — section</div>
                <div className="section-block">
                  <label htmlFor="rp-style-section">Apply to</label>
                  <select
                    id="rp-style-section"
                    className="rp-input"
                    value={styleSection}
                    onChange={(e) =>
                      setStyleSection(e.target.value as 'findings' | 'impression' | 'recommendations')
                    }
                  >
                    <option value="findings">Findings</option>
                    <option value="impression">Impression</option>
                    <option value="recommendations">Recommendations</option>
                  </select>
                </div>
              </div>
              <RewriteStylePanel
                reportId={report.id}
                section={styleSection}
                currentText={String(
                  (report as Record<string, unknown>)[styleSection] ?? '',
                )}
                providerId={providerId || undefined}
                onAccept={async (text) => {
                  const next = { ...aiHighlights, [styleSection]: true };
                  setAiHighlights(next);
                  await update({
                    [styleSection]: text,
                    aiHighlightsJson: JSON.stringify(next),
                  } as Partial<Report>);
                  setStylePanelOpen(false);
                }}
              />
            </>
          )}

          {showPrior && <PriorComparePanel reportId={report.id} />}

          {rewriteDraft && (
            <div className="rp-panel">
              <div className="rp-panel-title">
                Rewrite preview · <code>{rewriteDraft.mode}</code>
                <span className="badge ai">AI draft</span>
              </div>
              <p className="rp-page-sub">
                Section: <code>{String(rewriteDraft.section)}</code>
              </p>
              {rewriteDraft.diff ? (
                <div className="rp-rewrite-diff">
                  <div>
                    <div className="rp-stat-label">Original</div>
                    <pre className="rp-rewrite-pre">{rewriteDraft.original || '(empty)'}</pre>
                  </div>
                  <div>
                    <div className="rp-stat-label">Proposed</div>
                    <div className="ai-mark">
                      <pre className="rp-rewrite-pre">{rewriteDraft.proposed}</pre>
                    </div>
                  </div>
                </div>
              ) : (
                <div className="ai-mark">
                  <pre className="rp-rewrite-pre">{rewriteDraft.proposed}</pre>
                </div>
              )}
              <div className="rp-toolbar rp-mt-sm">
                <button className="primary" onClick={acceptRewrite}>Accept</button>
                <button className="ghost" onClick={() => setRewriteDraft(null)}>Reject</button>
                <button
                  className="subtle"
                  onClick={() => setRewriteDraft({ ...rewriteDraft, diff: !rewriteDraft.diff })}
                >
                  {rewriteDraft.diff ? 'Hide diff' : 'Diff'}
                </button>
              </div>
            </div>
          )}

          <div className="rp-panel">
            <div className="rp-panel-title">
              Validation
              {qualityScore !== null && (
                <span className={`badge ${qualityScore >= 80 ? 'ok' : qualityScore >= 50 ? 'warn' : 'danger'}`}>
                  Quality: {qualityScore}/100
                </span>
              )}
            </div>
            {findings.length === 0 && <p style={{ color: 'var(--text-muted)' }}>Click <em>Validate</em> to run rulebook checks.</p>}
            {findings.length > 0 && (() => {
              const groups = groupBySeverity(findings);
              return (
                <>
                  <div className="rp-row" style={{ gap: 6, marginBottom: 8, flexWrap: 'wrap' }}>
                    {groups.blocker.length > 0 && <span className="badge danger">{groups.blocker.length} blocker{groups.blocker.length === 1 ? '' : 's'}</span>}
                    {groups.warning.length > 0 && <span className="badge warn">{groups.warning.length} warning{groups.warning.length === 1 ? '' : 's'}</span>}
                    {groups.info.length > 0 && <span className="badge info">{groups.info.length} info</span>}
                    {blockers === 0 && <span className="badge ok">No blockers</span>}
                  </div>
                  {(['blocker', 'warning', 'info'] as const).map((sev) => groups[sev].length > 0 && (
                    <div key={sev} style={{ marginTop: 8 }}>
                      <div style={{ font: '500 11px var(--sans)', textTransform: 'uppercase', color: 'var(--text-muted)', marginBottom: 4 }}>
                        {sev}
                      </div>
                      {groups[sev].map((f, i) =>
                        f.ruleId === 'ai:unsupported_claim' ? (
                          <UnsupportedClaimFinding key={`${sev}-${i}`} finding={f} />
                        ) : (
                          <div key={`${sev}-${i}`} className={`finding ${sev}`}>
                            <div>{f.message}</div>
                            <div className="rule"><code>{f.ruleId}</code>{f.section ? ` · ${f.section}` : ''}</div>
                          </div>
                        ),
                      )}
                    </div>
                  ))}
                </>
              );
            })()}
          </div>

          <div className="rp-panel">
            <div className="rp-panel-title">
              Sign &amp; addendum
              {primarySigned ? (
                <span className="badge ok">Signed</span>
              ) : (
                <span className="badge warn">Unsigned</span>
              )}
            </div>

            {!primarySigned ? (
              <>
                <div className="section-block">
                  <label>Note (optional)</label>
                  <input
                    className="rp-input"
                    value={signNote}
                    onChange={(e) => setSignNote(e.target.value)}
                    placeholder="e.g. preliminary read"
                  />
                </div>
                <div className="rp-toolbar">
                  <button className="primary" disabled={signBusy} onClick={signAsPrimary}>
                    {signBusy ? '…' : 'Sign as Primary'}
                  </button>
                </div>
              </>
            ) : (
              <>
                <div className="rp-toolbar">
                  <button className="primary-ghost" disabled={signBusy} onClick={addCoSigner}>
                    Add Co-Signer
                  </button>
                  <button
                    className="primary-ghost"
                    disabled={signBusy}
                    onClick={() => setAddendumOpen((v) => !v)}
                  >
                    {addendumOpen ? 'Cancel addendum' : 'Add Addendum'}
                  </button>
                </div>
                {addendumOpen && (
                  <div className="composer-shell">
                    <textarea
                      className="rp-input"
                      placeholder="Addendum text…"
                      value={addendumBody}
                      onChange={(e) => setAddendumBody(e.target.value)}
                      rows={4}
                    />
                    <div className="section-block">
                      <label>Note (optional)</label>
                      <input
                        className="rp-input"
                        value={signNote}
                        onChange={(e) => setSignNote(e.target.value)}
                      />
                    </div>
                    <div className="rp-toolbar">
                      <button
                        className="primary"
                        disabled={signBusy || !addendumBody.trim()}
                        onClick={submitAddendum}
                      >
                        {signBusy ? '…' : 'Submit addendum'}
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}

            <ul className="rp-list rp-mt-sm">
              <li className="rp-row between rp-divider-row">
                <span className="rp-stat-label rp-cell f2">Radiologist</span>
                <span className="rp-stat-label rp-cell f1">Role</span>
                <span className="rp-stat-label rp-cell f1 r">Signed</span>
              </li>
              {signatures.length === 0 && (
                <li className="rp-page-sub rp-divider-row">No signatures yet.</li>
              )}
              {signatures.map((s) => (
                <li key={s.id} className="rp-row between rp-divider-row">
                  <span className="rp-cell f2"><code>{s.radiologistEmail}</code></span>
                  <span className="rp-cell f1">
                    <span className={`badge ${roleBadge(s.role)}`}>{normalizeRole(s.role)}</span>
                  </span>
                  <span className="rp-cell f1 r">{fmtDateTime(s.signedAt)}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

function severityClass(s: ValidationFinding['severity']): string {
  const v = typeof s === 'number' ? ['info', 'warning', 'blocker'][s] : String(s).toLowerCase();
  return v;
}

function groupBySeverity(findings: ValidationFinding[]) {
  const groups = { blocker: [] as ValidationFinding[], warning: [] as ValidationFinding[], info: [] as ValidationFinding[] };
  for (const f of findings) {
    const k = severityClass(f.severity) as 'blocker' | 'warning' | 'info';
    if (groups[k]) groups[k].push(f);
  }
  return groups;
}

function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

function normalizeRole(role: string | number): string {
  if (typeof role === 'number') return ['Primary', 'CoSigner', 'Addendum'][role] ?? String(role);
  return role;
}

function roleBadge(role: string | number): string {
  const r = normalizeRole(role);
  if (r === 'Primary') return 'ok';
  if (r === 'CoSigner') return 'info';
  if (r === 'Addendum') return 'ai';
  return '';
}

function fmtDateTime(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

/**
 * AI-007 — render an `ai:unsupported_claim` finding with the offending
 * sentence quoted in `.ai-mark`, an "Unsupported claim" warning badge, and
 * an "Edit Findings" button that scrolls to the Findings section.
 */
function UnsupportedClaimFinding({ finding }: { finding: ValidationFinding }) {
  const sentence = finding.snippet?.trim() || finding.message;
  function scrollToFindings() {
    const el = document.getElementById('rp-findings-section');
    if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    const ta = el.querySelector('textarea');
    if (ta && ta instanceof HTMLTextAreaElement) ta.focus();
  }
  return (
    <div className="finding warning">
      <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
        <span className="badge warn">Unsupported claim</span>
        {finding.section && <span className="badge">{finding.section}</span>}
      </div>
      <blockquote className="ai-mark" style={{ margin: '4px 0', padding: '6px 10px' }}>
        “{sentence}”
      </blockquote>
      <div className="rp-row" style={{ gap: 8, marginTop: 6 }}>
        <button className="subtle" onClick={scrollToFindings}>Edit Findings</button>
        <span className="rule"><code>{finding.ruleId}</code></span>
      </div>
    </div>
  );
}
