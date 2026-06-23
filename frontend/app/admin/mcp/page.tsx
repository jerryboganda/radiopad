'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useEffect, useState } from 'react';
import { api, type McpToolRow } from '@/lib/api';

const STATUS_LABEL: Record<number, string> = { 0: 'Submitted', 1: 'Approved', 2: 'Blocked' };
const STATUS_BADGE: Record<number, string> = { 0: 'info', 1: 'ok', 2: 'danger' };

const SCOPE_LABEL: Record<number, string> = { 0: 'ReadOnly', 1: 'ReadWrite', 2: 'External' };

function isDangerous(scopeString: string): boolean {
  if (!scopeString) return false;
  return scopeString
    .split(/[,\s]+/)
    .filter(Boolean)
    .some((t) => /^(shell|fs|net):/i.test(t));
}

export default function McpAdminPage() {
  return (
    <PermissionGate permission="mcp_tools.manage" title="MCP tools">
      <McpAdminPageInner />
    </PermissionGate>
  );
}

function McpAdminPageInner() {
  const [tools, setTools] = useState<McpToolRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  // Register form state.
  const [showRegister, setShowRegister] = useState(false);
  const [regName, setRegName] = useState('');
  const [regVersion, setRegVersion] = useState('1.0.0');
  const [regScope, setRegScope] = useState('rulebook:read');
  const [regManifest, setRegManifest] = useState('');
  const [regSig, setRegSig] = useState('');

  // Sandbox-test panel.
  const [testTool, setTestTool] = useState<McpToolRow | null>(null);
  const [testInput, setTestInput] = useState('{}');
  const [testResult, setTestResult] = useState<{
    status: string;
    output: string;
    latencyMs: number;
    memoryBytes: number;
  } | null>(null);

  async function refresh() {
    try {
      setTools(await api.mcp.list());
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function approve(id: string) {
    setError(null);
    setInfo(null);
    try {
      await api.mcp.approve(id);
      setInfo('Tool approved.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function block(id: string) {
    setError(null);
    setInfo(null);
    try {
      await api.mcp.block(id, 'admin-blocked');
      setInfo('Tool blocked.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function remove(id: string) {
    if (!confirm('Permanently delete this tool registration?')) return;
    setError(null);
    setInfo(null);
    try {
      await api.mcp.delete(id);
      setInfo('Tool deleted.');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function submitRegister(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setInfo(null);
    if (!regName.trim()) {
      setError('Name is required.');
      return;
    }
    try {
      await api.mcp.register({
        name: regName.trim(),
        version: regVersion.trim() || '1.0.0',
        scopeString: regScope.trim(),
        manifestJson: regManifest,
        manifestSig: regSig,
      });
      setInfo('Tool registered (status: Submitted). Approve to enable invocation.');
      setShowRegister(false);
      setRegName('');
      setRegVersion('1.0.0');
      setRegScope('rulebook:read');
      setRegManifest('');
      setRegSig('');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function runTest(t: McpToolRow) {
    setError(null);
    setTestResult(null);
    setTestTool(t);
    try {
      const r = await api.mcp.test(t.id, testInput || '{}');
      setTestResult(r);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">External AI tools</h1>
          <p className="rp-page-sub">
            Approve, block, and test add-on tools that your AI assistant can use. Only approved tools can run — everything else is blocked by default.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-page-grid">
        <div className="rp-page-main">

      <div className="rp-panel">
        <div className="rp-panel-title">
          Registered tools
          <button
            type="button"
            className="primary"
            style={{ marginLeft: 'auto' }}
            onClick={() => setShowRegister((v) => !v)}
          >
            {showRegister ? 'Cancel' : 'Add a tool'}
          </button>
        </div>

        {showRegister && (
          <form onSubmit={submitRegister} className="rp-field">
            <label className="rp-field">
              <span>Tool name</span>
              <input className="rp-input" value={regName} onChange={(e) => setRegName(e.target.value)} />
            </label>
            <label className="rp-field">
              <span>Version</span>
              <input className="rp-input" value={regVersion} onChange={(e) => setRegVersion(e.target.value)} />
            </label>
            <details className="rp-advanced" open>
              <summary>Technical details (IT team only)</summary>
              <label className="rp-field">
                <span>Scope (comma-separated tokens, e.g. <code>net:dicomweb</code>)</span>
                <input className="rp-input" value={regScope} onChange={(e) => setRegScope(e.target.value)} />
              </label>
              <label className="rp-field">
                <span>Manifest JSON</span>
                <textarea
                  className="rp-input"
                  rows={6}
                  value={regManifest}
                  onChange={(e) => setRegManifest(e.target.value)}
                />
              </label>
              <label className="rp-field">
                <span>Signature (base64, optional)</span>
                <input className="rp-input" value={regSig} onChange={(e) => setRegSig(e.target.value)} />
              </label>
            </details>
            <button type="submit" className="primary">
              Submit for approval
            </button>
          </form>
        )}

        {tools.length === 0 ? (
          <p className="rp-page-sub">No external tools registered yet.</p>
        ) : (
          <ul className="rp-list">
            {tools.map((t) => (
              <li key={t.id} className="rp-list-row">
                <div>
                  <strong>{t.name}</strong> <span className="rp-page-sub">v{t.version}</span>{' '}
                  <span className={`badge ${STATUS_BADGE[t.status]}`}>{STATUS_LABEL[t.status]}</span>
                  {t.isBuiltIn && <span className="badge info" style={{ marginLeft: 6 }}>built-in</span>}
                  {t.manifestSigned && <span className="badge ok" style={{ marginLeft: 6 }}>trusted</span>}
                  {isDangerous(t.scopeString) && (
                    <span className="badge danger" style={{ marginLeft: 6 }}>powerful access</span>
                  )}
                  <div className="rp-page-sub">
                    Access level: {SCOPE_LABEL[t.scope] || 'ReadOnly'}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  {t.status !== 1 && (
                    <button type="button" className="primary-ghost" onClick={() => approve(t.id)}>
                      Approve
                    </button>
                  )}
                  {t.status !== 2 && (
                    <button type="button" className="ghost" onClick={() => block(t.id)}>
                      Block
                    </button>
                  )}
                  <button type="button" className="subtle" onClick={() => runTest(t)}>
                    Test safely
                  </button>
                  <button type="button" className="subtle" onClick={() => remove(t.id)}>
                    Delete
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {testTool && (
        <div className="rp-panel">
          <div className="rp-panel-title">Safe test — {testTool.name}</div>
          <p className="rp-page-sub">
            Runs this tool in a sandbox with strict limits: 5-second timeout, low priority, no network unless its access level allows it.
          </p>
          <details className="rp-advanced">
            <summary>Show test input (technical)</summary>
            <label className="rp-field">
              <span>Input JSON</span>
              <textarea
                className="rp-input"
                rows={4}
                value={testInput}
                onChange={(e) => setTestInput(e.target.value)}
              />
            </label>
          </details>
          <button type="button" className="primary" onClick={() => runTest(testTool)}>
            Run test
          </button>
          {testResult && (
            <div className="rp-field" style={{ marginTop: 12 }}>
              <span>
                Result:{' '}
                <span className={`badge ${testResult.status === 'ok' ? 'ok' : 'danger'}`}>
                  {testResult.status === 'ok' ? 'OK' : 'Failed'}
                </span>{' '}
                · took {testResult.latencyMs} ms
              </span>
              <details className="rp-advanced">
                <summary>Show raw output</summary>
                <pre className="rp-input" style={{ whiteSpace: 'pre-wrap' }}>
                  {testResult.output || '(empty)'}
                </pre>
              </details>
            </div>
          )}
        </div>
      )}

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What are external tools?</div>
            <p>Small add-ons that let an AI assistant do extra things — like look up a code, or fetch a study. They run inside RadioPad&apos;s sandbox.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Are they safe?</div>
            <p>Every tool is blocked until you approve it. Tools that need powerful access (file system, shell, network) are flagged in red. When in doubt, leave them blocked and ask IT.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
