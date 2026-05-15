'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  api,
  type Rulebook,
  type GoldenCaseResult,
  type PromptOverrideVersion,
  type ValidationResult,
} from '@/lib/api';
import { rulebookHref } from '@/lib/routes';
import { computeDiff, type DiffLine } from '@/lib/textDiff';
import { isMedicalDirector } from '@/lib/roles';

/**
 * PRD §16.4 — Full Prompt Studio.
 *
 * Split workspace for admins and medical directors to author, test,
 * compare, and approve prompt overrides and rulebook prompt blocks.
 *
 * Left pane — Prompt Block Editor
 * Right pane — Test & Compare (tabbed: Test Runner | Output Diff | Golden Cases | Approval)
 *
 * UI uses only locked Open Design tokens. No Tailwind / no inline colour styles.
 */

type PromptOverride = {
  id: string;
  rulebookId: string;
  blockKey: string;
  body: string;
  status: 'Draft' | 'Approved';
  approvedByUserId: string | null;
  approvedAt: string | null;
  updatedAt: string;
};

type PromptBlock = { name: string; body: string };

type TabId = 'test' | 'diff' | 'golden' | 'approval';

const TABS: { id: TabId; label: string }[] = [
  { id: 'test', label: 'Test Runner' },
  { id: 'diff', label: 'Output Diff' },
  { id: 'golden', label: 'Golden Cases' },
  { id: 'approval', label: 'Approval' },
];

export default function PromptStudioPage() {
  // ---- Global state ----
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [yaml, setYaml] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [userRole, setUserRole] = useState<number | null>(null);

  // ---- Prompt overrides ----
  const [overrides, setOverrides] = useState<PromptOverride[]>([]);
  const [editedBlocks, setEditedBlocks] = useState<Record<string, string>>({});

  // ---- Right-pane state ----
  const [activeTab, setActiveTab] = useState<TabId>('test');

  // ---- Test Runner state ----
  const [testInput, setTestInput] = useState('');
  const [testOutput, setTestOutput] = useState<string | null>(null);
  const [testRunning, setTestRunning] = useState(false);
  const [testValidation, setTestValidation] = useState<ValidationResult | null>(null);

  // ---- Diff Viewer state ----
  const [diffV1, setDiffV1] = useState<number>(0);
  const [diffV2, setDiffV2] = useState<number>(1);
  const [versions, setVersions] = useState<PromptOverrideVersion[]>([]);
  const [diffResult, setDiffResult] = useState<DiffLine[] | null>(null);
  const [diffRunning, setDiffRunning] = useState(false);

  // ---- Golden Cases state ----
  const [goldenResults, setGoldenResults] = useState<GoldenCaseResult[]>([]);
  const [goldenRunning, setGoldenRunning] = useState(false);

  // ---- Approval state ----
  const [approving, setApproving] = useState(false);

  // ---- Bootstrap ----
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [list, me] = await Promise.all([
          api.rulebooks.list(),
          api.me(),
        ]);
        if (cancelled) return;
        setRulebooks(list);
        setUserRole(me.user.role);
        if (list.length > 0) setActiveId(list[0].id);
      } catch (e) {
        setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  // ---- Load active rulebook YAML + overrides ----
  useEffect(() => {
    if (!activeId) { setYaml(''); setOverrides([]); return; }
    let cancelled = false;
    (async () => {
      try {
        const [rb, ovList] = await Promise.all([
          api.rulebooks.get(activeId),
          api.promptOverrides.list(),
        ]);
        if (cancelled) return;
        setYaml(rb.sourceYaml);
        // Filter overrides to the active rulebook
        const active = rulebooks.find((r) => r.id === activeId);
        const rbId = active?.rulebookId ?? activeId;
        setOverrides(ovList.filter((o) => o.rulebookId === rbId));
        setEditedBlocks({});
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    })();
    return () => { cancelled = true; };
  }, [activeId, rulebooks]);

  const active = useMemo(
    () => rulebooks.find((r) => r.id === activeId) ?? null,
    [rulebooks, activeId],
  );

  const promptBlocks = useMemo(() => extractPromptBlocks(yaml), [yaml]);

  // Merge YAML blocks with overrides — overrides win
  const mergedBlocks = useMemo((): PromptBlock[] => {
    const map = new Map<string, string>();
    for (const b of promptBlocks) map.set(b.name, b.body);
    for (const o of overrides) map.set(o.blockKey, o.body);
    // Preserve YAML order, then append any override-only keys
    const result: PromptBlock[] = [];
    const seen = new Set<string>();
    for (const b of promptBlocks) {
      result.push({ name: b.name, body: map.get(b.name) ?? b.body });
      seen.add(b.name);
    }
    for (const o of overrides) {
      if (!seen.has(o.blockKey)) {
        result.push({ name: o.blockKey, body: o.body });
      }
    }
    return result;
  }, [promptBlocks, overrides]);

  const overrideForBlock = useCallback(
    (blockKey: string) => overrides.find((o) => o.blockKey === blockKey) ?? null,
    [overrides],
  );

  // ---- Handlers ----
  const handleBlockEdit = (blockKey: string, body: string) => {
    setEditedBlocks((prev) => ({ ...prev, [blockKey]: body }));
  };

  const handleSaveBlock = async (blockKey: string) => {
    if (!active) return;
    const body = editedBlocks[blockKey];
    if (body === undefined) return;
    try {
      await api.promptOverrides.save({
        rulebookId: active.rulebookId ?? active.id,
        blockKey,
        body,
      });
      // Refresh overrides
      const ovList = await api.promptOverrides.list();
      const rbId = active.rulebookId ?? active.id;
      setOverrides(ovList.filter((o) => o.rulebookId === rbId));
      setEditedBlocks((prev) => {
        const next = { ...prev };
        delete next[blockKey];
        return next;
      });
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleRunTest = async () => {
    if (!testInput.trim()) return;
    setTestRunning(true);
    setTestOutput(null);
    setTestValidation(null);
    try {
      // Use the first report for validation context, or create a transient one
      const reports = await api.reports.list();
      if (reports.length === 0) {
        setTestOutput('No reports available to run test against.');
        return;
      }
      const report = reports[0];
      // Patch findings with test input, then validate
      await api.reports.patch(report.id, { findings: testInput });
      const result = await api.reports.validate(report.id);
      setTestValidation(result);
      setTestOutput(`Quality Score: ${(result.qualityScore * 100).toFixed(1)}%\n\nFindings validated with ${result.findings.length} finding(s).${result.blockerPresent ? '\n⚠ Blocker(s) present.' : ''}`);
    } catch (e) {
      setTestOutput(`Error: ${(e as Error).message}`);
    } finally {
      setTestRunning(false);
    }
  };

  const handleLoadVersions = async () => {
    const ov = overrides[0];
    if (!ov) { setVersions([]); return; }
    setDiffRunning(true);
    try {
      const v = await api.promptOverrides.listVersions(ov.id);
      setVersions(v);
    } catch {
      setVersions([]);
    } finally {
      setDiffRunning(false);
    }
  };

  const handleRunDiff = async () => {
    if (versions.length < 2) return;
    setDiffRunning(true);
    try {
      const a = versions[diffV1]?.body ?? '';
      const b = versions[diffV2]?.body ?? '';
      setDiffResult(computeDiff(a, b));
    } catch {
      setDiffResult(null);
    } finally {
      setDiffRunning(false);
    }
  };

  const handleRunGolden = async () => {
    if (!active) return;
    setGoldenRunning(true);
    try {
      const ov = overrides[0];
      const results = await api.promptStudio.testGolden({
        rulebookId: active.rulebookId ?? active.id,
        promptOverrideId: ov?.id ?? null,
      });
      setGoldenResults(results);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setGoldenRunning(false);
    }
  };

  const handleApprove = async (overrideId: string) => {
    setApproving(true);
    try {
      await api.promptOverrides.approve(overrideId);
      // Refresh overrides
      const ovList = await api.promptOverrides.list();
      const rbId = active?.rulebookId ?? active?.id ?? '';
      setOverrides(ovList.filter((o) => o.rulebookId === rbId));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setApproving(false);
    }
  };

  const handleAddCustomBlock = () => {
    const name = prompt('Block key name (e.g. custom_system):');
    if (!name || !name.trim()) return;
    const key = name.trim();
    setEditedBlocks((prev) => ({ ...prev, [key]: '' }));
  };

  // ---- Render ----
  return (
    <div className="pane">
      <div className="panel">
        <div className="panel-header">
          <div>
            <h1 className="rp-page-title">Prompt Studio</h1>
            <p className="rp-page-sub">
              Author, test, compare, and approve prompt overrides and rulebook
              prompt blocks (PRD §16.4).
            </p>
          </div>
          {active ? (
            <Link className="primary-ghost" href={rulebookHref(active.id)}>
              Edit in rulebook editor
            </Link>
          ) : null}
        </div>

        {loading ? <div className="rp-page-sub">Loading…</div> : null}
        {error ? <div className="banner warn">{error}</div> : null}

        {!loading && rulebooks.length === 0 ? (
          <div className="rp-page-sub">
            No rulebooks yet. Create one in{' '}
            <Link href="/rulebooks">Rulebooks</Link>.
          </div>
        ) : null}

        {rulebooks.length > 0 ? (
          <div className="rp-grid-2" data-testid="prompt-studio-split">
            {/* ========== LEFT PANE — Prompt Block Editor ========== */}
            <div className="rp-panel">
              <div className="rp-panel-title">
                Prompt Blocks
                {active ? ` · ${active.name}` : ''}
              </div>

              {/* Rulebook selector */}
              <div className="section-block">
                <label htmlFor="ps-rulebook-select">Rulebook</label>
                <select
                  id="ps-rulebook-select"
                  className="rp-textarea"
                  value={activeId ?? ''}
                  onChange={(e) => setActiveId(e.target.value)}
                >
                  {rulebooks.map((rb) => (
                    <option key={rb.id} value={rb.id}>
                      {rb.name} ({rb.version}) —{' '}
                      {statusLabel(rb.status)}
                    </option>
                  ))}
                </select>
              </div>

              {/* Block list */}
              {mergedBlocks.length === 0 ? (
                <div className="rp-page-sub">
                  No prompt blocks found in this rulebook.
                </div>
              ) : (
                <ul className="rp-list" data-testid="prompt-block-list">
                  {mergedBlocks.map((block) => {
                    const ov = overrideForBlock(block.name);
                    const edited = editedBlocks[block.name];
                    const currentBody = edited !== undefined ? edited : block.body;
                    const isDirty = edited !== undefined && edited !== block.body;
                    return (
                      <li key={block.name} className="section-block">
                        <label>
                          {block.name}
                          {ov ? (
                            <span
                              className={`badge ${ov.status === 'Approved' ? 'ok' : 'warn'}`}
                            >
                              {ov.status}
                            </span>
                          ) : null}
                        </label>
                        <textarea
                          className="rp-textarea"
                          value={currentBody}
                          onChange={(e) =>
                            handleBlockEdit(block.name, e.target.value)
                          }
                          rows={4}
                        />
                        {isDirty ? (
                          <div className="rp-actions rp-mt-sm">
                            <button
                              type="button"
                              className="primary"
                              onClick={() => handleSaveBlock(block.name)}
                            >
                              Save Draft
                            </button>
                          </div>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              )}

              <div className="rp-actions rp-mt-sm">
                <button
                  type="button"
                  className="ghost"
                  onClick={handleAddCustomBlock}
                >
                  + Add Custom Block
                </button>
              </div>
            </div>

            {/* ========== RIGHT PANE — Test & Compare ========== */}
            <div className="rp-panel">
              {/* Tab bar */}
              <nav className="rp-toolbar" role="tablist">
                {TABS.map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    role="tab"
                    aria-selected={activeTab === tab.id}
                    className={`ghost${activeTab === tab.id ? ' active' : ''}`}
                    onClick={() => setActiveTab(tab.id)}
                  >
                    {tab.label}
                  </button>
                ))}
              </nav>

              {/* ---- Tab 1: Test Runner ---- */}
              {activeTab === 'test' ? (
                <div data-testid="tab-test-runner">
                  <div className="rp-row rp-mb-md">
                    {active ? (
                      <>
                        <span className="badge info">
                          {active.name} {active.version}
                        </span>
                        <span className="badge">
                          {statusLabel(active.status)}
                        </span>
                      </>
                    ) : null}
                  </div>
                  <div className="section-block">
                    <label htmlFor="ps-test-input">Findings Input</label>
                    <textarea
                      id="ps-test-input"
                      className="rp-textarea findings"
                      value={testInput}
                      onChange={(e) => setTestInput(e.target.value)}
                      placeholder="Paste findings text here…"
                      rows={6}
                    />
                  </div>
                  <div className="rp-actions rp-mb-md">
                    <button
                      type="button"
                      className="primary"
                      disabled={testRunning || !testInput.trim()}
                      onClick={handleRunTest}
                    >
                      {testRunning ? 'Running…' : 'Run Test'}
                    </button>
                  </div>
                  {testOutput ? (
                    <div className="ai-mark">
                      <pre>{testOutput}</pre>
                    </div>
                  ) : null}
                  {testValidation && testValidation.findings.length > 0 ? (
                    <ul className="rp-list rp-mt-sm">
                      {testValidation.findings.map((f, i) => (
                        <li
                          key={`${f.ruleId}-${i}`}
                          className={`finding ${f.severity === 'Blocker' ? 'blocker' : f.severity === 'Warning' ? 'warning' : 'info'}`}
                        >
                          {f.message}
                          <div className="rule">{f.ruleId}</div>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}

              {/* ---- Tab 2: Output Diff Viewer ---- */}
              {activeTab === 'diff' ? (
                <div data-testid="tab-diff-viewer">
                  <div className="rp-row rp-mb-md">
                    <button
                      type="button"
                      className="ghost"
                      onClick={handleLoadVersions}
                      disabled={diffRunning || overrides.length === 0}
                    >
                      Load Versions
                    </button>
                  </div>
                  {versions.length >= 2 ? (
                    <>
                      <div className="rp-row rp-mb-md">
                        <div className="section-block">
                          <label htmlFor="ps-diff-v1">Version A</label>
                          <select
                            id="ps-diff-v1"
                            className="rp-textarea"
                            value={diffV1}
                            onChange={(e) => setDiffV1(Number(e.target.value))}
                          >
                            {versions.map((v, i) => (
                              <option key={i} value={i}>
                                v{v.version} — {v.status}
                              </option>
                            ))}
                          </select>
                        </div>
                        <div className="section-block">
                          <label htmlFor="ps-diff-v2">Version B</label>
                          <select
                            id="ps-diff-v2"
                            className="rp-textarea"
                            value={diffV2}
                            onChange={(e) => setDiffV2(Number(e.target.value))}
                          >
                            {versions.map((v, i) => (
                              <option key={i} value={i}>
                                v{v.version} — {v.status}
                              </option>
                            ))}
                          </select>
                        </div>
                        <button
                          type="button"
                          className="primary"
                          onClick={handleRunDiff}
                          disabled={diffRunning}
                        >
                          Compare
                        </button>
                      </div>
                    </>
                  ) : overrides.length === 0 ? (
                    <div className="rp-page-sub">
                      No prompt overrides to compare. Edit a block and save a
                      draft first.
                    </div>
                  ) : (
                    <div className="rp-page-sub">
                      {diffRunning
                        ? 'Loading versions…'
                        : 'Click "Load Versions" to fetch version history.'}
                    </div>
                  )}
                  {diffResult ? (
                    <div className="rp-panel rp-mt-sm">
                      {diffResult.map((line, i) => (
                        <div
                          key={i}
                          className={
                            line.type === 'added'
                              ? 'rp-diff-add'
                              : line.type === 'removed'
                                ? 'rp-diff-remove'
                                : ''
                          }
                        >
                          <code>
                            {line.type === 'added'
                              ? '+ '
                              : line.type === 'removed'
                                ? '- '
                                : '  '}
                            {line.text}
                          </code>
                        </div>
                      ))}
                    </div>
                  ) : null}
                </div>
              ) : null}

              {/* ---- Tab 3: Golden-Case Library ---- */}
              {activeTab === 'golden' ? (
                <div data-testid="tab-golden-cases">
                  <div className="rp-actions rp-mb-md">
                    <button
                      type="button"
                      className="primary"
                      disabled={goldenRunning || !active}
                      onClick={handleRunGolden}
                    >
                      {goldenRunning ? 'Running…' : 'Run All'}
                    </button>
                  </div>
                  {goldenResults.length > 0 ? (
                    <table className="rp-table">
                      <thead>
                        <tr>
                          <th>Case</th>
                          <th>Expected Rules</th>
                          <th>Actual Rules</th>
                          <th>Score</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {goldenResults.map((r) => (
                          <tr key={r.caseName}>
                            <td>{r.caseName}</td>
                            <td>
                              <code>{r.expectedRules.join(', ')}</code>
                            </td>
                            <td>
                              <code>{r.actualRules.join(', ')}</code>
                            </td>
                            <td>{(r.qualityScore * 100).toFixed(0)}%</td>
                            <td>
                              <span
                                className={`badge ${r.passed ? 'ok' : 'danger'}`}
                              >
                                {r.passed ? 'Pass' : 'Fail'}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  ) : (
                    <div className="rp-page-sub">
                      {goldenRunning
                        ? 'Running golden cases…'
                        : 'Click "Run All" to test golden cases against the current prompt.'}
                    </div>
                  )}
                </div>
              ) : null}

              {/* ---- Tab 4: Approval Workflow ---- */}
              {activeTab === 'approval' ? (
                <div data-testid="tab-approval">
                  {overrides.length === 0 ? (
                    <div className="rp-page-sub">
                      No prompt overrides for this rulebook. Edit a block and
                      save a draft to begin the approval workflow.
                    </div>
                  ) : (
                    <ul className="rp-list">
                      {overrides.map((ov) => (
                        <li key={ov.id} className="rp-panel rp-mb-md">
                          <div className="rp-row between">
                            <div>
                              <strong>{ov.blockKey}</strong>
                              <span
                                className={`badge ${ov.status === 'Approved' ? 'ok' : 'warn'}`}
                              >
                                {ov.status}
                              </span>
                            </div>
                            <div className="rp-row">
                              {ov.status === 'Draft' &&
                              isMedicalDirector(userRole) ? (
                                <button
                                  type="button"
                                  className="primary"
                                  disabled={approving}
                                  onClick={() => handleApprove(ov.id)}
                                >
                                  {approving ? 'Approving…' : 'Approve'}
                                </button>
                              ) : null}
                              {ov.status === 'Draft' &&
                              !isMedicalDirector(userRole) ? (
                                <span className="badge warn">
                                  Awaiting Medical Director approval
                                </span>
                              ) : null}
                            </div>
                          </div>
                          {ov.approvedAt ? (
                            <div className="rp-page-sub rp-mt-sm">
                              Approved at{' '}
                              {new Date(ov.approvedAt).toLocaleString()}
                            </div>
                          ) : null}
                          <div className="rp-page-sub rp-mt-sm">
                            Last updated{' '}
                            {new Date(ov.updatedAt).toLocaleString()}
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              ) : null}
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}

// ---- Helpers ----

function statusLabel(status: Rulebook['status']): string {
  if (typeof status === 'string') return status;
  return ['Draft', 'In review', 'Approved', 'Deprecated'][status] ?? String(status);
}

function badgeFor(status: Rulebook['status']) {
  if (typeof status === 'number')
    return ['', 'warn', 'ok', 'danger'][status] ?? 'warn';
  const normalized = status.toLowerCase();
  if (normalized === 'approved') return 'ok';
  if (normalized === 'review' || normalized === 'in review') return 'info';
  if (normalized === 'deprecated') return 'danger';
  return 'warn';
}

/**
 * Tiny YAML scraper for the read-only prompt_blocks preview. Avoids a
 * full YAML parser dependency. Captures indented lines under
 * `prompt_blocks:` and stops at the next top-level key.
 */
function extractPromptBlocks(yaml: string): { name: string; body: string }[] {
  const lines = yaml.split(/\r?\n/);
  const out: { name: string; body: string }[] = [];
  let inBlock = false;
  let current: { name: string; body: string } | null = null;
  for (const line of lines) {
    if (/^prompt_blocks:\s*$/i.test(line)) {
      inBlock = true;
      continue;
    }
    if (!inBlock) continue;
    if (/^[a-zA-Z_]/.test(line)) {
      if (current) out.push(current);
      break;
    }
    const m = line.match(
      /^\s{2,4}([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*"?(.*?)"?\s*$/,
    );
    if (m) {
      if (current) out.push(current);
      current = { name: m[1], body: m[2] };
    } else if (current) {
      current.body += '\n' + line.trim();
    }
  }
  if (current) out.push(current);
  return out;
}
