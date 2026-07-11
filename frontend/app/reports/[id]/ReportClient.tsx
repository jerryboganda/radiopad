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
  type CrossCheckCorrection,
  type CatalogItem,
} from '@/lib/api';
import { getSessionAudio } from '@/lib/dictation/audioBuffer';
import { isUseUbagEnabled } from '@/lib/dictation/crossCheckPrefs';
import { anchorCorrections, applyCorrection } from '@/lib/dictation/anchorCorrections';
import CrossCheckBadge from '@/components/dictation/CrossCheckBadge';
import { readQueryParam } from '@/lib/browserParams';
import { detectCommand, stripCommand, type VoiceCommand, type CommandMatch } from '@/lib/voiceCommands';
import RewriteStylePanel from './RewriteStylePanel';
import PriorComparePanel from './PriorComparePanel';
import ReportRibbon, { type RibbonTab, type ExportFormat } from './ReportRibbon';
import ReportInspector, { type InspectorTab } from './ReportInspector';
import { SECTIONS, SECTION_FIELD_MAP, normalizeScaffold, statusLabel, statusTone, normalizeRole } from './reportShared';
import { usePermissions } from '@/lib/permissions';
import SectionEditor from '@/components/editor/SectionEditor';
import { useRichEditorEnabled } from '@/lib/editor/richEditorFlag';

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
  const richEditor = useRichEditorEnabled();
  const [id, setId] = useState<string | null>(null);
  const [report, setReport] = useState<Report | null>(null);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [templates, setTemplates] = useState<ReportTemplate[]>([]);
  // Iter-36 — admin-managed catalogs for the study-context dropdowns.
  const [modalities, setModalities] = useState<CatalogItem[]>([]);
  const [bodyParts, setBodyParts] = useState<CatalogItem[]>([]);
  const [findings, setFindings] = useState<ValidationFinding[]>([]);
  const [qualityScore, setQualityScore] = useState<number | null>(null);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiHighlights, setAiHighlights] = useState<Record<string, boolean>>({});
  // Cross-check suggestions keyed by section, plus the processing-badge state.
  const [corrections, setCorrections] = useState<Record<string, CrossCheckCorrection[]>>({});
  const [xc, setXc] = useState<{ status: 'running' | 'completed' | 'failed'; stage: string } | null>(null);
  // Guards runCrossCheck against concurrent invocation (button + voice command).
  const xcBusyRef = useRef(false);
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
  const [dictating, setDictating] = useState(false);
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
    api.modalities.list().then(setModalities).catch(() => {});
    api.bodyParts.list().then(setBodyParts).catch(() => {});
    api.reports.signatures(id).then(setSignatures).catch(() => setSignatures([]));
  }, [id]);

  const update = useCallback(async (
    patch: Partial<Report> & { modality?: string; bodyPart?: string; contrast?: string; age?: number | null; gender?: string },
  ) => {
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

  // Scaffold-swap — keep the report body in sync with the bound template. The
  // binding itself is resolved server-side from the (modality, body part,
  // contrast) selection key (or pinned manually); whenever it changes, sections
  // that are still untouched scaffold (empty, or verbatim equal to a known
  // template placeholder) are replaced with the NEW template's scaffolding.
  // Sections the radiologist edited are never touched — they're listed in a
  // dismissible notice instead, so stale "chest technique under a brain study"
  // text can no longer survive a context change while real prose always does.
  const parseTemplateSections = useCallback((t: ReportTemplate) => {
    let sections: Array<{ id: string; placeholder?: string }> = [];
    try {
      const parsed = JSON.parse(t.sectionsJson) as { sections?: typeof sections } | typeof sections;
      sections = Array.isArray(parsed) ? parsed : parsed.sections ?? [];
    } catch { return null; }
    return sections;
  }, []);

  // Whitespace-normalized placeholder index across ALL loaded templates. A
  // section matching ANY known placeholder is treated as untouched scaffold —
  // robust to reports whose source template is no longer knowable (created
  // before this fix, rapid context flips, reload mid-swap).
  const placeholderIndex = useMemo(() => {
    const idx = new Map<string, Set<string>>();
    for (const t of templates) {
      const sections = parseTemplateSections(t);
      if (!sections) continue;
      for (const s of sections) {
        const key = SECTION_FIELD_MAP[s.id];
        if (!key) continue;
        const ph = normalizeScaffold(s.placeholder ?? '');
        if (!ph) continue;
        let set = idx.get(key as string);
        if (!set) { set = new Set(); idx.set(key as string, set); }
        set.add(ph);
      }
    }
    return idx;
  }, [templates, parseTemplateSections]);

  const [scaffoldNotice, setScaffoldNotice] = useState<{ templateName: string; kept: string[] } | null>(null);

  const swapScaffold = useCallback(async (templatePk: string, showNotice: boolean) => {
    const current = reportRef.current;
    if (!current || !templatePk) return;
    // Bindings are locked after primary sign-off; never rewrite a signed body.
    if (signatures.some((s) => normalizeRole(s.role) === 'Primary')) return;
    const t = templates.find((x) => x.id === templatePk);
    if (!t) return;
    const sections = parseTemplateSections(t);
    if (!sections) return;
    const nextPlaceholders = new Map<string, string>();
    for (const s of sections) {
      const key = SECTION_FIELD_MAP[s.id];
      if (key) nextPlaceholders.set(key as string, s.placeholder ?? '');
    }

    const patch: Partial<Report> = {};
    const kept: string[] = [];
    for (const { key, label } of SECTIONS) {
      const cur = String((current as Record<string, unknown>)[key as string] ?? '');
      const curNorm = normalizeScaffold(cur);
      const isScaffold = curNorm.length === 0 || (placeholderIndex.get(key as string)?.has(curNorm) ?? false);
      // New template may not carry this section — clear stale scaffold to "".
      const next = nextPlaceholders.get(key as string) ?? '';
      if (isScaffold) {
        if (normalizeScaffold(next) !== curNorm) {
          (patch as Record<string, unknown>)[key as string] = next;
        }
      } else if (normalizeScaffold(next) !== curNorm) {
        kept.push(label);
      }
    }

    if (showNotice) setScaffoldNotice(kept.length > 0 ? { templateName: t.name, kept } : null);
    if (Object.keys(patch).length === 0) return;
    setSaving(true);
    try {
      const next = await api.reports.patch(current.id, patch);
      // Response-merge: for sections NOT in the swap patch, keep the live
      // on-screen value (sections persist on blur, so the server copy may be
      // older than what the radiologist is typing/dictating right now).
      const live = reportRef.current;
      const merged: Report = { ...next };
      if (live) {
        for (const { key } of SECTIONS) {
          if (!(key in patch)) {
            (merged as Record<string, unknown>)[key as string] =
              (live as Record<string, unknown>)[key as string];
          }
        }
      }
      setReport(merged);
    } finally {
      setSaving(false);
    }
  }, [templates, placeholderIndex, parseTemplateSections, signatures]);

  // Serialize swaps: if the binding changes again while a swap PATCH is in
  // flight (rapid context flips), queue the latest id and run it once after.
  const swapBusyRef = useRef(false);
  const pendingSwapRef = useRef<{ tid: string; showNotice: boolean } | null>(null);
  const runSwap = useCallback(async (tid: string, showNotice: boolean) => {
    if (swapBusyRef.current) {
      pendingSwapRef.current = { tid, showNotice };
      return;
    }
    swapBusyRef.current = true;
    try {
      await swapScaffold(tid, showNotice);
    } finally {
      swapBusyRef.current = false;
      const pending = pendingSwapRef.current;
      pendingSwapRef.current = null;
      if (pending && pending.tid !== tid) void runSwap(pending.tid, pending.showNotice);
    }
  }, [swapScaffold]);

  // Fire the swap whenever the bound template id changes (auto re-bind from a
  // study-context change, manual override, or reset-to-auto) — including when
  // an id recurs after flipping away and back. Waits until the bound template
  // is present in the loaded template list; the effect re-fires on load.
  const prevTemplateIdRef = useRef<string | null>(null);
  useEffect(() => {
    const tid = report?.templateId ?? null;
    if (!tid) return;
    if (prevTemplateIdRef.current === tid) return;
    if (!templates.some((t) => t.id === tid)) return;
    const hadPrevious = prevTemplateIdRef.current !== null;
    prevTemplateIdRef.current = tid;
    // On initial load (no previous id this session) the swap silently fills
    // empty/stale scaffold; the "kept your text" notice only makes sense when
    // the binding visibly changed under the user.
    void runSwap(tid, hadPrevious);
  }, [report?.templateId, templates, runSwap]);

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

  /**
   * Broadcasts the dictation-cleanup lifecycle to the floating overlay so the
   * "Fix" button can render a live spinner and a result line. The work runs
   * here (in the editor) but the button lives in the global overlay, so the two
   * are bridged by a window event. `busy` shows the spinner; the terminal
   * statuses replace it with a message instead of failing silently.
   */
  function emitCleanupResult(
    status: 'busy' | 'success' | 'no-changes' | 'empty' | 'error',
    message?: string,
  ) {
    if (typeof window === 'undefined') return;
    window.dispatchEvent(
      new CustomEvent('radiopad:dictation-cleanup-result', { detail: { status, message } }),
    );
  }

  /**
   * "Fix everything" from the dictation overlay — runs the report's dictated
   * prose through the UBAG-routed cleanup pipeline and applies the structured
   * medical sections it returns. AI-touched sections wear `.ai-mark` until the
   * radiologist acknowledges them (CLAUDE.md rule 3).
   */
  async function runDictationCleanup() {
    if (!report) {
      emitCleanupResult('error', 'Open a report before running Fix.');
      return;
    }
    setAiBusy(true);
    setError(null);
    // Tell the floating overlay to show its "Fixing…" spinner immediately — the
    // UBAG cleanup is a web-automation round trip that can take up to a minute,
    // so the radiologist needs a live indication the action is in progress.
    emitCleanupResult('busy');
    try {
      await flushEdits();
      const current = reportRef.current;
      if (!current) {
        emitCleanupResult('error', 'Report is still loading — try again in a moment.');
        return;
      }
      const raw = [
        current.indication, current.technique, current.comparison,
        current.findings, current.impression, current.recommendations,
      ]
        .map((s) => (s ?? '').trim())
        .filter(Boolean)
        .join('\n');
      if (!raw) {
        emitCleanupResult('empty', 'Nothing to clean up yet — dictate or type into a section first.');
        return;
      }
      const res = await api.reports.cleanupDictation(current.id, raw);
      const cs = res.cleanedSections;
      const patch: Partial<Report> = {};
      const nextHighlights = { ...aiHighlightsRef.current };
      (['indication', 'technique', 'findings', 'impression', 'recommendations'] as const).forEach((k) => {
        const v = cs[k];
        if (v && v.trim()) {
          (patch as Record<string, unknown>)[k] = v;
          nextHighlights[k] = true;
        }
      });
      const changed = Object.keys(patch).length;
      if (changed === 0) {
        emitCleanupResult('no-changes', 'UBAG returned no changes for this dictation.');
        return;
      }
      setAiHighlights(nextHighlights);
      await update({ ...patch, aiHighlightsJson: JSON.stringify(nextHighlights) } as Partial<Report>);
      emitCleanupResult('success', `Cleaned ${changed} section${changed === 1 ? '' : 's'} via ${res.provider || 'UBAG'}.`);
    } catch (e) {
      const err = e as { body?: { error?: string; detail?: string; title?: string }; message: string };
      const msg = err.body?.error || err.body?.detail || err.body?.title || err.message || 'Fix failed.';
      setError(msg);
      emitCleanupResult('error', msg);
    } finally {
      setAiBusy(false);
    }
  }

  /**
   * Manual Cross Check (triggered by the dictation overlay's button). Re-runs the
   * retained dictation audio through the on-device engines (sidecar, ROVER), then
   * a hosted LLM medical-accuracy review, and surfaces the merged suggestions as
   * inline highlights + a review panel. Non-blocking: a badge polls in the corner.
   */
  async function runCrossCheck() {
    const current = reportRef.current;
    if (!current) return;
    // Re-entrancy guard: a second invocation (button + voice command) while a
    // run is in flight would interleave state updates.
    if (xcBusyRef.current) return;
    const sess = getSessionAudio(current.id);
    if (!sess) {
      setError('Record a dictation with the HQ button first, then Cross Check.');
      return;
    }
    xcBusyRef.current = true;
    setError(null);
    setXc({ status: 'running', stage: 're-running engines' });
    try {
      const useUbag = isUseUbagEnabled();
      const { jobId } = await api.reports.crossCheck(current.id, sess.blob, {
        liveTranscript: sess.transcript,
        sectionKey: sess.sectionKey,
        useUbag,
      });

      // Poll the on-device ASR cross-check job (~2 min budget). A poll budget
      // that runs dry is a TIMEOUT, not "no changes" — report it as a failure
      // so the radiologist never mistakes an unfinished QA pass for a clean one.
      let asr: CrossCheckCorrection[] = [];
      let asrDone = false;
      for (let i = 0; i < 150; i++) {
        await new Promise((r) => setTimeout(r, 800));
        const s = await api.reports.crossCheckStatus(current.id, jobId);
        setXc({ status: s.status === 'completed' || s.status === 'failed' ? s.status : 'running', stage: s.stage });
        if (s.status === 'completed') { asr = s.corrections ?? []; asrDone = true; break; }
        if (s.status === 'failed') throw new Error(s.error || 'cross-check failed');
      }
      if (!asrDone) throw new Error('Cross-check timed out — the engines did not finish. Try again.');

      // Hosted LLM medical-accuracy review on the same base text (best-effort,
      // but its absence is SURFACED in the result badge rather than silent).
      let llm: CrossCheckCorrection[] = [];
      let reviewFailed = false;
      setXc({ status: 'running', stage: 'medical review' });
      try {
        const r = await api.reports.crossCheckReview(current.id, {
          text: sess.transcript,
          sectionKey: sess.sectionKey,
          useUbag,
        });
        llm = r.corrections ?? [];
      } catch {
        reviewFailed = true;
      }

      const merged = [...asr, ...llm];
      setCorrections({ [sess.sectionKey]: merged });
      const summary = merged.length ? `${merged.length} suggestion${merged.length === 1 ? '' : 's'}` : 'no changes';
      setXc({
        status: 'completed',
        stage: reviewFailed ? `${summary} · medical review unavailable` : summary,
      });
    } catch (e) {
      setError((e as Error).message);
      setXc({ status: 'failed', stage: 'cross-check failed' });
    } finally {
      xcBusyRef.current = false;
    }
  }

  function rejectCorrection(sectionKey: string, id: string) {
    setCorrections((prev) => ({ ...prev, [sectionKey]: (prev[sectionKey] ?? []).filter((c) => c.id !== id) }));
  }

  async function acceptCorrection(c: CrossCheckCorrection) {
    const current = reportRef.current;
    if (!current || !c.sectionKey) return;
    const key = c.sectionKey as keyof Report;
    const text = String((current as Record<string, unknown>)[c.sectionKey] ?? '');
    const next = applyCorrection(text, c);
    rejectCorrection(c.sectionKey, c.id); // drop it either way (can't re-anchor → skip)
    if (next == null) return;
    setReport({ ...current, [key]: next } as Report);
    await update({ [key]: next } as Partial<Report>);
  }

  async function acceptAllCorrections(sectionKey: string) {
    const current = reportRef.current;
    if (!current) return;
    let text = String((current as Record<string, unknown>)[sectionKey] ?? '');
    for (const c of corrections[sectionKey] ?? []) {
      const n = applyCorrection(text, c);
      if (n != null) text = n;
    }
    setReport({ ...current, [sectionKey]: text } as Report);
    await update({ [sectionKey]: text } as Partial<Report>);
    setCorrections((prev) => ({ ...prev, [sectionKey]: [] }));
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
    // Dictation is now a global floating overlay (see DictationOverlay). The
    // editor only reacts to the overlay's "Fix" action, which cleans the
    // dictated prose into structured medical sections via UBAG.
    // preventDefault marks the event HANDLED — the overlay dispatches these as
    // cancelable events and shows "open a report first" when nothing claims them.
    const cleanup = (e: Event) => { e.preventDefault(); void runDictationCleanup(); };
    const crossCheck = (e: Event) => { e.preventDefault(); void runCrossCheck(); };

    window.addEventListener('radiopad:generate-impression', generate);
    window.addEventListener('radiopad:rewrite', rewrite);
    window.addEventListener('radiopad:dictation-cleanup', cleanup);
    window.addEventListener('radiopad:cross-check', crossCheck);
    return () => {
      window.removeEventListener('radiopad:generate-impression', generate);
      window.removeEventListener('radiopad:rewrite', rewrite);
      window.removeEventListener('radiopad:dictation-cleanup', cleanup);
      window.removeEventListener('radiopad:cross-check', crossCheck);
    };
  }, [id, report?.id, providerId]);

  // Mirror the floating DictationOverlay's listening state so the ribbon's own
  // Dictate button (a remote trigger for the same toggle) shows the same
  // pressed/label state instead of looking inert while dictation is live.
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const onDictateState = (e: Event) => {
      const detail = (e as CustomEvent<{ listening: boolean }>).detail;
      if (detail) setDictating(detail.listening);
    };
    window.addEventListener('radiopad:dictate-listening', onDictateState);
    return () => window.removeEventListener('radiopad:dictate-listening', onDictateState);
  }, []);

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
        onDictate={() => window.dispatchEvent(new CustomEvent('radiopad:dictate'))}
        dictating={dictating}
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

      {Object.values(corrections).some((list) => list.length > 0) && (
        <div className="rp-panel" data-testid="crosscheck-panel">
          <div className="rp-panel-title">
            Cross-check suggestions <span className="badge ai">AI</span>
          </div>
          <p className="rp-page-sub">
            Review each change the cross-check engines and medical AI proposed. Accepting applies the edit.
          </p>
          {Object.entries(corrections).flatMap(([sectionKey, list]) =>
            list.map((c) => (
              <div key={c.id} className={`rp-xc-item ${c.severity}`}>
                <div className="rp-xc-change">
                  <span className="rp-xc-from">{c.originalText || '∅'}</span>
                  <span className="rp-xc-arrow" aria-hidden="true">→</span>
                  <span className="rp-xc-to">{c.correctedText}</span>
                </div>
                <div className="rp-xc-meta">
                  <span className="badge">{sectionKey}</span>
                  <span className="rp-xc-reason">{c.reason}</span>
                  <span className="rp-xc-source">{c.source}</span>
                </div>
                <div className="rp-row" style={{ gap: 8 }}>
                  <button className="primary" onClick={() => acceptCorrection(c)}>Accept</button>
                  <button className="ghost" onClick={() => rejectCorrection(sectionKey, c.id)}>Reject</button>
                </div>
              </div>
            )),
          )}
          <div className="rp-toolbar rp-mt-sm">
            {Object.keys(corrections).map((sectionKey) =>
              (corrections[sectionKey]?.length ?? 0) > 0 ? (
                <button key={sectionKey} className="subtle" onClick={() => acceptAllCorrections(sectionKey)}>
                  Accept all
                </button>
              ) : null,
            )}
            <button className="ghost" onClick={() => setCorrections({})}>Reject all</button>
            <button className="subtle" onClick={() => { void flushEdits(); }}>Save</button>
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

          {scaffoldNotice && (
            <div className="banner info" data-testid="scaffold-notice">
              Template changed to “{scaffoldNotice.templateName}” — kept your text in:{' '}
              {scaffoldNotice.kept.join(', ')}.
              <button
                className="ghost"
                type="button"
                style={{ marginLeft: 8 }}
                onClick={() => setScaffoldNotice(null)}
              >
                Dismiss
              </button>
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
                  {richEditor ? (
                    <SectionEditor
                      sectionKey={key as string}
                      className={cls}
                      ariaLabel={label}
                      value={(report as Record<string, unknown>)[key as string] as string}
                      corrections={anchorCorrections(
                        (report as Record<string, unknown>)[key as string] as string,
                        corrections[key as string] ?? [],
                      )}
                      onChange={(val) => {
                        const next = { ...aiHighlights };
                        if (next[key as string]) delete next[key as string];
                        setAiHighlights(next);
                        setReport({ ...report, [key]: val });
                      }}
                      onBlur={(val) =>
                        update({
                          [key]: val,
                          aiHighlightsJson: JSON.stringify(aiHighlights),
                        } as Partial<Report>)
                      }
                    />
                  ) : (
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
                  )}
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
          modalities={modalities}
          bodyParts={bodyParts}
          rulebooks={rulebooks}
          onStudyChange={(patch) => update(patch)}
          onTemplateChange={(templateId) => update({ templateId, templatePinned: true })}
          onTemplateReset={() => update({ templatePinned: false })}
          onRulebookChange={(rulebookId) => update({ rulebookId, rulebookPinned: true })}
          onRulebookReset={() => update({ rulebookPinned: false })}
          canEdit={canEdit}
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

      {xc && (
        <CrossCheckBadge status={xc.status} stage={xc.stage} onDismiss={() => setXc(null)} />
      )}
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
