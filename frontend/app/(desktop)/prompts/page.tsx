'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { api, type Rulebook, type GoldenCaseResult, type ValidationResult } from '@/lib/api';
import { rulebookEditorHref } from '@/lib/routes';
import { isMedicalDirector } from '@/lib/roles';
import { yamlToRulebookEditor } from '@/lib/rulebookYaml';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import ContextBar from './ContextBar';
import PromptBlockEditor, { type EditorBlock } from './PromptBlockEditor';
import TestRunnerTab from './TestRunnerTab';
import GoldenCasesTab from './GoldenCasesTab';
import ApprovalTab from './ApprovalTab';
import type { PromptOverride, TabId } from './promptStudioTypes';

/**
 * PRD §16.4 — Prompt Studio.
 *
 * Split workspace for admins and medical directors to author, test, compare,
 * and approve prompt overrides for a rulebook's prompt blocks.
 *
 *   Left pane  — prompt-block editor (one card per block)
 *   Right pane — Test runner · Golden cases · Approval
 *
 * Locked Open Design tokens only. This page orchestrates data + state; the
 * panes and tabs are presentational components in this folder.
 */

const TABS: { id: TabId; label: string }[] = [
  { id: 'test', label: 'Test runner' },
  { id: 'golden', label: 'Golden cases' },
  { id: 'approval', label: 'Approval' },
];

export default function PromptStudioPage() {
  // ---- Bootstrap / context ----
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [yaml, setYaml] = useState<string>('');
  const [userRole, setUserRole] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // ---- Prompt overrides + edits ----
  const [overrides, setOverrides] = useState<PromptOverride[]>([]);
  const [editedBlocks, setEditedBlocks] = useState<Record<string, string>>({});
  const [savingKey, setSavingKey] = useState<string | null>(null);

  // ---- Right pane ----
  const [activeTab, setActiveTab] = useState<TabId>('test');

  // ---- Test runner ----
  const [testInput, setTestInput] = useState('');
  const [testResult, setTestResult] = useState<ValidationResult | null>(null);
  const [testRunning, setTestRunning] = useState(false);
  const [testError, setTestError] = useState<string | null>(null);

  // ---- Golden cases ----
  const [goldenResults, setGoldenResults] = useState<GoldenCaseResult[] | null>(null);
  const [goldenRunning, setGoldenRunning] = useState(false);

  // ---- Approval ----
  const [approvingId, setApprovingId] = useState<string | null>(null);

  const active = useMemo(() => rulebooks.find((r) => r.id === activeId) ?? null, [rulebooks, activeId]);
  const rulebookKey = active?.rulebookId ?? active?.id ?? '';

  // ---- Load rulebooks + current user ----
  const loadContext = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([api.rulebooks.list(), api.me()])
      .then(([list, me]) => {
        setRulebooks(list);
        setUserRole(me.user.role);
        setActiveId((prev) => prev ?? (list[0]?.id ?? null));
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { loadContext(); }, [loadContext]);

  // ---- Load active rulebook YAML + its overrides ----
  const reloadOverrides = useCallback(async () => {
    const ovList = (await api.promptOverrides.list()) as PromptOverride[];
    setOverrides(ovList.filter((o) => o.rulebookId === rulebookKey));
  }, [rulebookKey]);

  useEffect(() => {
    if (!activeId) { setYaml(''); setOverrides([]); return; }
    let cancelled = false;
    (async () => {
      try {
        const [rb, ovList] = await Promise.all([
          api.rulebooks.get(activeId),
          api.promptOverrides.list() as Promise<PromptOverride[]>,
        ]);
        if (cancelled) return;
        setYaml(rb.sourceYaml);
        const rbKey = rulebooks.find((r) => r.id === activeId)?.rulebookId ?? activeId;
        setOverrides(ovList.filter((o) => o.rulebookId === rbKey));
        setEditedBlocks({});
        // Reset right-pane derived state when the context changes.
        setTestResult(null);
        setTestError(null);
        setGoldenResults(null);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    })();
    return () => { cancelled = true; };
  }, [activeId, rulebooks]);

  // ---- Derived: merged editor blocks (rulebook defaults + overrides + edits) ----
  const editorBlocks = useMemo<EditorBlock[]>(() => {
    const yamlBlocks = yaml ? yamlToRulebookEditor(yaml).prompt_blocks : [];
    const ovMap = new Map(overrides.map((o) => [o.blockKey, o]));
    const baseMap = new Map<string, string>();
    const order: string[] = [];
    for (const b of yamlBlocks) { baseMap.set(b.key, b.text); order.push(b.key); }
    for (const o of overrides) { if (!baseMap.has(o.blockKey)) order.push(o.blockKey); }
    for (const key of Object.keys(editedBlocks)) {
      if (!baseMap.has(key) && !ovMap.has(key)) order.push(key);
    }
    return order.map((key) => {
      const ov = ovMap.get(key);
      const base = ov ? ov.body : (baseMap.get(key) ?? '');
      const edited = editedBlocks[key];
      const body = edited !== undefined ? edited : base;
      return {
        key,
        body,
        dirty: edited !== undefined && edited !== base,
        status: ov ? ov.status : 'Default',
      };
    });
  }, [yaml, overrides, editedBlocks]);

  const dirtyCount = useMemo(() => editorBlocks.filter((b) => b.dirty).length, [editorBlocks]);
  const draftCount = useMemo(() => overrides.filter((o) => o.status === 'Draft').length, [overrides]);

  // ---- Block editor handlers ----
  const handleEdit = (key: string, value: string) =>
    setEditedBlocks((prev) => ({ ...prev, [key]: value }));

  const handleReset = (key: string) =>
    setEditedBlocks((prev) => {
      const next = { ...prev };
      delete next[key];
      return next;
    });

  const handleAdd = (key: string) =>
    setEditedBlocks((prev) => (prev[key] !== undefined ? prev : { ...prev, [key]: '' }));

  const handleSave = async (key: string) => {
    if (!active) return;
    const body = editedBlocks[key];
    if (body === undefined) return;
    setSavingKey(key);
    try {
      await api.promptOverrides.save({ rulebookId: rulebookKey, blockKey: key, body });
      await reloadOverrides();
      handleReset(key);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSavingKey(null);
    }
  };

  // ---- Test runner (non-destructive dry-run) ----
  const handleRunTest = async () => {
    if (!active || !testInput.trim()) return;
    setTestRunning(true);
    setTestResult(null);
    setTestError(null);
    try {
      const result = await api.promptStudio.testValidation({
        rulebookId: active.id,
        findings: testInput,
        promptOverrideId: overrides[0]?.id ?? null,
      });
      setTestResult(result);
    } catch (e) {
      setTestError((e as Error).message);
    } finally {
      setTestRunning(false);
    }
  };

  // ---- Golden cases ----
  const handleRunGolden = async () => {
    if (!active) return;
    setGoldenRunning(true);
    try {
      const results = await api.promptStudio.testGolden({
        rulebookId: rulebookKey,
        promptOverrideId: overrides[0]?.id ?? null,
      });
      setGoldenResults(results);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setGoldenRunning(false);
    }
  };

  // ---- Approval ----
  const handleApprove = async (overrideId: string) => {
    setApprovingId(overrideId);
    try {
      await api.promptOverrides.approve(overrideId);
      await reloadOverrides();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setApprovingId(null);
    }
  };

  // ---- Render ----
  const editLink = active ? (
    <Link className="primary-ghost" href={rulebookEditorHref(active.id)}>
      Edit in rulebook editor
    </Link>
  ) : null;

  return (
    <Container>
      <PageHeader
        title="Prompt studio"
        description="Write and refine the instructions you give the AI. Test changes and submit them for medical-director approval."
        secondaryActions={editLink}
      />

      {error && !loading ? (
        <ErrorState title="Couldn't load Prompt Studio" message={error} onRetry={loadContext} />
      ) : null}

      {!error && loading ? (
        <div className="rp-grid-2">
          <div className="rp-panel"><Skeleton variant="block" height={320} /></div>
          <div className="rp-panel"><Skeleton variant="block" height={320} /></div>
        </div>
      ) : null}

      {!error && !loading && rulebooks.length === 0 ? (
        <EmptyState
          title="No rulebooks yet"
          description="Prompt Studio edits the AI instructions attached to a rulebook. Create a rulebook first."
          action={<Link className="primary" href="/rulebooks">Go to Rulebooks</Link>}
        />
      ) : null}

      {!error && !loading && rulebooks.length > 0 ? (
        <>
          <ContextBar
            rulebooks={rulebooks}
            activeId={activeId}
            active={active}
            dirtyCount={dirtyCount}
            onSelect={setActiveId}
          />

          <div className="rp-grid-2 rp-studio-grid" data-testid="prompt-studio-split">
            {/* Left — prompt block editor */}
            <PromptBlockEditor
              blocks={editorBlocks}
              savingKey={savingKey}
              onEdit={handleEdit}
              onSave={handleSave}
              onReset={handleReset}
              onAdd={handleAdd}
            />

            {/* Right — test & review workspace */}
            <section className="rp-panel rp-studio-workspace" aria-label="Test and review">
              <div className="rp-tabs" role="tablist" aria-label="Prompt studio tools">
                {TABS.map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    role="tab"
                    aria-selected={activeTab === tab.id}
                    className={`rp-tab ${activeTab === tab.id ? 'active' : ''}`}
                    onClick={() => setActiveTab(tab.id)}
                  >
                    {tab.label}
                    {tab.id === 'approval' && draftCount > 0 ? (
                      <span className="rp-tab-count">{draftCount}</span>
                    ) : null}
                  </button>
                ))}
              </div>

              <div className="rp-anim-fade-in" key={activeTab}>
              {activeTab === 'test' ? (
                <TestRunnerTab
                  value={testInput}
                  onChange={setTestInput}
                  onRun={handleRunTest}
                  running={testRunning}
                  result={testResult}
                  error={testError}
                  disabled={!active}
                />
              ) : null}

              {activeTab === 'golden' ? (
                <GoldenCasesTab
                  results={goldenResults}
                  onRun={handleRunGolden}
                  running={goldenRunning}
                  disabled={!active}
                />
              ) : null}

              {activeTab === 'approval' ? (
                <ApprovalTab
                  overrides={overrides}
                  canApprove={isMedicalDirector(userRole)}
                  approvingId={approvingId}
                  onApprove={handleApprove}
                />
              ) : null}
              </div>
            </section>
          </div>
        </>
      ) : null}
    </Container>
  );
}
