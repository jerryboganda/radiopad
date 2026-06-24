'use client';

import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
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
import ReportRibbon, { type RibbonTab, type ExportFormat } from './ReportRibbon';
import ReportInspector, { type InspectorTab } from './ReportInspector';
import { SECTIONS, statusLabel, statusTone, normalizeRole } from './reportShared';
import { usePermissions } from '@/lib/permissions';

type RewriteState = {
  mode: RewriteMode;
  section: keyof Report;
  original: string;
  proposed: string;
  diff: boolean;
};

export default function ReportPage() {
  const router = useRouter();
  // RBAC mirror — hide editing/signing/exporting affordances a read-only viewer
  // (e.g. Researcher, Auditor) or a trainee who cannot sign should never see.
  // The backend still enforces each action.
  const { can: canDo } = usePermissions();
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
  const [ribbonTab, setRibbonTab] = useState<RibbonTab>('home');
  const [inspectorTab, setInspectorTab] = useState<InspectorTab>('context');
  const [exportMenuOpen, setExportMenuOpen] = useState(false);
  const voicePillIdRef = { current: 0 };

  // Latest-value refs. AI actions can fire from the toolbar button (fresh
  // closure), but also from a desktop menu/keyboard event or a voice command
  // whose handler captured an older render. Reading the live editor state from
  // refs keeps the pre-AI flush (see `flushEdits`) correct on every path and
  // avoids ever PATCHing stale text back over newer content.
  const reportRef = useRef<Report | null>(report);
  reportRef.current = report;
  const aiHighlightsRef = useRef(aiHighlights);
  aiHighlightsRef.current = aiHighlights;

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

  /**
   * HANDOFF gotcha #3 — section textareas only persist on blur, but the AI
   * endpoints (`runAi`) read the report from the DB. Clicking "Generate
   * impression" straight from typing the Findings would otherwise race the
   * blur save and the model would draft from stale/empty server state ("No
   * findings were provided…"). Flush the on-screen editor state with one
   * PATCH before any AI call. Reads come from refs (not closures) so this is
   * correct even when triggered from the window-event / voice-command paths,
   * which would otherwise overwrite the DB with outdated text.
   */
  const flushEdits = useCallback(async () => {
    const current = reportRef.current;
    if (!current) return;
    const next = await api.reports.patch(current.id, {
      indication: current.indication,
      technique: current.technique,
      comparison: current.comparison,
      findings: current.findings,
      impression: current.impression,
      recommendations: current.recommendations,
      aiHighlightsJson: JSON.stringify(aiHighlightsRef.current),
    });
    setReport(next);
  }, []);

  async function refreshSignatures() {
    if (!report) return;
    try { setSignatures(await api.reports.signatures(report.id)); } catch { /* noop */ }
  }

  async function runAi(mode: string) {
    if (!report || !providerId) return;
    setAiBusy(true);
    setError(null);
    try {
      // Persist unsaved edits first so the AI drafts from what's on screen.
      await flushEdits();
      const out = await api.reports.runAi(report.id, { mode: mode as 'impression', providerId });
      const nextHighlights = { ...aiHighlightsRef.current, impression: true };
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
    setInspectorTab('checks');
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
  // Permission mirror (backend enforces; this only governs what we render).
  const canEdit = canDo('reports.edit');
  const canValidate = canDo('reports.validate');
  const canSign = canDo('reports.sign');
  const canExport = canDo('reports.export');

  return (
    <div className="rp-container">
      <div className="rp-doc-header">
        <button className="ghost" onClick={() => router.push('/')}>← Back</button>
        <h1 className="rp-doc-title">{report.study.modality} {report.study.bodyPart}</h1>
        <code className="rp-doc-accession">{report.study.accessionNumber}</code>
        <span className={`rp-status ${statusTone(report.status)}`}>{statusLabel(report.status)}</span>
        {primarySigned && <span className="rp-status success">Signed</span>}
        <span className="rp-doc-saved">{saving ? 'Saving…' : 'Auto-saved'}</span>
      </div>

      <ReportRibbon
        tab={ribbonTab}
        onTabChange={setRibbonTab}
        canEdit={canEdit}
        canValidate={canValidate}
        canExport={canExport}
        canSign={canSign}
        providers={providers}
        providerId={providerId}
        onProviderChange={setProviderId}
        rulebooks={rulebooks}
        rulebookId={report.rulebookId ?? null}
        onRulebookChange={(v) => update({ rulebookId: v })}
        aiBusy={aiBusy}
        onGenerate={() => runAi('impression')}
        rewriteOpen={rewriteOpen}
        onToggleRewrite={() => setRewriteOpen((v) => !v)}
        rewriteBusy={rewriteBusy}
        rewriteSection={rewriteSection}
        onRewriteSectionChange={setRewriteSection}
        onRewrite={runRewrite}
        stylePanelOpen={stylePanelOpen}
        onToggleStylePanel={() => setStylePanelOpen((v) => !v)}
        onDictate={() => router.push(mobileDictateHref(report.id))}
        voiceCommandMode={voiceCommandMode}
        onToggleVoiceCommand={() => setVoiceCommandMode((v) => !v)}
        voiceCommandPills={voiceCommandPills}
        onValidate={validate}
        showPrior={showPrior}
        onTogglePrior={() => setShowPrior((v) => !v)}
        reportId={report.id}
        exportAllowed={exportAllowed}
        exportTitle={exportTitle}
        exportMenuOpen={exportMenuOpen}
        onToggleExportMenu={() => setExportMenuOpen((v) => !v)}
        onCloseExportMenu={() => setExportMenuOpen(false)}
        onExport={(fmt: ExportFormat) => {
          if (fmt === 'text') void exportText();
          else if (fmt === 'json') void exportJson();
          else if (fmt === 'fhir') void exportFhir();
          else if (fmt === 'pdf') void exportPdf();
          else if (fmt === 'docx') void exportDocx();
        }}
        blockers={blockers}
        onAcknowledge={acknowledge}
        primarySigned={primarySigned}
        onGoToSignoff={() => setInspectorTab('signoff')}
      />

      {error && <div className="banner warn">{error}</div>}

      {/* Transient AI working surfaces — appear between the ribbon and the body. */}
      {stylePanelOpen && (
        <div className="rp-panel">
          <div className="rp-panel-title">Style rewrite</div>
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
          <RewriteStylePanel
            reportId={report.id}
            section={styleSection}
            currentText={String((report as Record<string, unknown>)[styleSection] ?? '')}
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
        </div>
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

      <div className="rp-report-body">
        <div className="rp-doc">
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

        <ReportInspector
          tab={inspectorTab}
          onTabChange={setInspectorTab}
          report={report}
          templates={templates}
          onApplyTemplate={applyTemplate}
          findings={findings}
          qualityScore={qualityScore}
          blockers={blockers}
          canValidate={canValidate}
          onValidate={validate}
          canSign={canSign}
          primarySigned={primarySigned}
          signatures={signatures}
          signBusy={signBusy}
          signNote={signNote}
          onSignNoteChange={setSignNote}
          addendumBody={addendumBody}
          onAddendumBodyChange={setAddendumBody}
          addendumOpen={addendumOpen}
          onToggleAddendum={() => setAddendumOpen((v) => !v)}
          onSignPrimary={signAsPrimary}
          onAddCoSigner={addCoSigner}
          onSubmitAddendum={submitAddendum}
        />
      </div>
    </div>
  );
}

function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}
