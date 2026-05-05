'use client';

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
      <h1 className="rp-page-title">MCP tool registry</h1>
      <p className="rp-page-sub">
        Model Context Protocol tools registered for this tenant. Only{' '}
        <span className="badge ok">Approved</span> tools may be invoked. Tools whose scope contains{' '}
        <code>shell:</code>, <code>fs:</code>, or <code>net:</code> are <em>default-deny</em>.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">
          Registered tools
          <button
            type="button"
            className="primary"
            style={{ marginLeft: 'auto' }}
            onClick={() => setShowRegister((v) => !v)}
          >
            {showRegister ? 'Cancel' : 'Register tool'}
          </button>
        </div>

        {showRegister && (
          <form onSubmit={submitRegister} className="rp-field">
            <label className="rp-field">
              <span>Name</span>
              <input className="rp-input" value={regName} onChange={(e) => setRegName(e.target.value)} />
            </label>
            <label className="rp-field">
              <span>Version</span>
              <input className="rp-input" value={regVersion} onChange={(e) => setRegVersion(e.target.value)} />
            </label>
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
              <span>Detached Ed25519 signature (base64, optional for tenant-authored)</span>
              <input className="rp-input" value={regSig} onChange={(e) => setRegSig(e.target.value)} />
            </label>
            <button type="submit" className="primary">
              Submit
            </button>
          </form>
        )}

        {tools.length === 0 ? (
          <p className="rp-page-sub">No tools registered for this tenant.</p>
        ) : (
          <ul className="rp-list">
            {tools.map((t) => (
              <li key={t.id} className="rp-list-row">
                <div>
                  <strong>{t.name}</strong> <code>v{t.version}</code>{' '}
                  <span className={`badge ${STATUS_BADGE[t.status]}`}>{STATUS_LABEL[t.status]}</span>
                  {t.isBuiltIn && <span className="badge info" style={{ marginLeft: 6 }}>built-in</span>}
                  {t.manifestSigned && <span className="badge ok" style={{ marginLeft: 6 }}>signed</span>}
                  {isDangerous(t.scopeString) && (
                    <span className="badge danger" style={{ marginLeft: 6 }}>dangerous-scope</span>
                  )}
                  <div className="rp-page-sub">
                    Scope: <code>{t.scopeString || SCOPE_LABEL[t.scope] || 'ReadOnly'}</code>
                  </div>
                  {t.manifestSha256 && (
                    <div className="rp-page-sub">
                      sha256: <code>{t.manifestSha256.slice(0, 16)}…</code>
                    </div>
                  )}
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
                    Sandbox test
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
          <div className="rp-panel-title">Sandbox test — {testTool.name}</div>
          <p className="rp-page-sub">
            Runs the tool body in an in-process sandbox with a 5-second wall-clock timeout, soft 256
            MiB memory cap, and BelowNormal thread priority. Network access is denied unless scope
            contains <code>net:</code>.
          </p>
          <label className="rp-field">
            <span>Input JSON</span>
            <textarea
              className="rp-input"
              rows={4}
              value={testInput}
              onChange={(e) => setTestInput(e.target.value)}
            />
          </label>
          <button type="button" className="primary" onClick={() => runTest(testTool)}>
            Run sandbox
          </button>
          {testResult && (
            <div className="rp-field" style={{ marginTop: 12 }}>
              <span>
                Status:{' '}
                <span className={`badge ${testResult.status === 'ok' ? 'ok' : 'danger'}`}>
                  {testResult.status}
                </span>{' '}
                · {testResult.latencyMs} ms · {(testResult.memoryBytes / 1024).toFixed(1)} KiB
              </span>
              <pre className="rp-input" style={{ whiteSpace: 'pre-wrap' }}>
                {testResult.output || '(empty)'}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
