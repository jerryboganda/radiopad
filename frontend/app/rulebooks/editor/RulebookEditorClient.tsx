'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { api } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import {
  type RulebookEditorState,
  emptyEditorState,
  rulebookToYaml,
  yamlToRulebookEditor,
} from '@/lib/rulebookYaml';
import MetadataPanel from './MetadataPanel';
import StylePanel from './StylePanel';
import SectionsPanel from './SectionsPanel';
import RulesPanel from './RulesPanel';
import PromptBlocksPanel from './PromptBlocksPanel';

export default function RulebookEditorClient() {
  const router = useRouter();
  const [state, setState] = useState<RulebookEditorState>(emptyEditorState());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [problems, setProblems] = useState<string[] | null>(null);
  const [loadedId, setLoadedId] = useState<string | null>(null);

  // Load existing rulebook when ?id= is present
  useEffect(() => {
    const id = readQueryParam('id');
    if (!id) return;
    setLoadedId(id);
    api.rulebooks.get(id)
      .then((rb) => {
        if (rb.sourceYaml) {
          setState(yamlToRulebookEditor(rb.sourceYaml));
        }
      })
      .catch((e: Error) => setError(e.message));
  }, []);

  const yaml = useCallback(() => rulebookToYaml(state), [state]);

  async function handleValidate() {
    setBusy(true);
    setError(null);
    try {
      const r = await api.rulebooks.validateYaml(yaml());
      setProblems(r.problems);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function handleSave() {
    setBusy(true);
    setError(null);
    try {
      await api.rulebooks.save(yaml());
      setProblems([]);
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  async function handlePublish() {
    setBusy(true);
    setError(null);
    try {
      const saved = await api.rulebooks.save(yaml());
      await api.rulebooks.approve(saved.id);
      setProblems([]);
      setState((prev) => ({ ...prev, status: 'approved' }));
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  function handleCancel() {
    router.push('/rulebooks');
  }

  const statusBadge =
    state.status === 'approved' ? 'ok'
      : state.status === 'deprecated' ? 'danger'
        : state.status === 'in_review' ? 'warn'
          : '';

  return (
    <div className="rp-container">
      {/* Top bar */}
      <div className="rp-row between rp-mb-md">
        <div>
          <div className="rp-row rp-gap-sm">
            <button className="ghost" onClick={handleCancel}>← Back</button>
            <h1 className="rp-page-title" style={{ margin: 0 }}>
              {state.name || 'New Rulebook'}
            </h1>
            {state.version && (
              <span style={{ color: 'var(--text-muted)', fontSize: 13 }}>v{state.version}</span>
            )}
            {state.status && (
              <span className={`badge ${statusBadge}`}>{state.status}</span>
            )}
          </div>
        </div>
        <div className="rp-row rp-gap-sm">
          <button className="ghost" onClick={handleCancel} disabled={busy}>Cancel</button>
          <button className="ghost" onClick={handleValidate} disabled={busy}>Validate</button>
          <button className="primary" onClick={handleSave} disabled={busy}>Save</button>
          <button className="primary-ghost" onClick={handlePublish} disabled={busy}>Publish</button>
        </div>
      </div>

      {error && <div className="banner warn">{error}</div>}
      {problems !== null && problems.length === 0 && (
        <div className="banner info" style={{ marginBottom: 12 }}>
          <span className="badge ok">OK — schema valid</span>
        </div>
      )}
      {problems !== null && problems.length > 0 && (
        <div style={{ marginBottom: 12 }}>
          {problems.map((p, i) => <div key={i} className="finding warning">{p}</div>)}
        </div>
      )}

      {/* Main split layout */}
      <div className="split">
        {/* Left pane — visual editor */}
        <section className="pane" style={{ overflow: 'auto', maxHeight: 'calc(100vh - 160px)' }}>
          <MetadataPanel data={state} onChange={setState} />
          <StylePanel
            style={state.style}
            onChange={(s) => setState((prev) => ({ ...prev, style: s }))}
          />
          <SectionsPanel
            sections={state.required_sections}
            onChange={(s) => setState((prev) => ({ ...prev, required_sections: s }))}
          />
          <RulesPanel
            rules={state.rules}
            onChange={(r) => setState((prev) => ({ ...prev, rules: r }))}
          />
          <PromptBlocksPanel
            blocks={state.prompt_blocks}
            onChange={(b) => setState((prev) => ({ ...prev, prompt_blocks: b }))}
          />
        </section>

        {/* Right pane — live YAML preview */}
        <section className="pane">
          <div className="rp-panel-title">YAML Preview</div>
          <pre
            style={{
              fontFamily: 'var(--mono)',
              fontSize: 12,
              lineHeight: 1.5,
              background: 'var(--bg-subtle)',
              border: '1px solid var(--border)',
              borderRadius: 'var(--radius-sm)',
              padding: 14,
              overflow: 'auto',
              maxHeight: 'calc(100vh - 200px)',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              margin: 0,
            }}
          >
            {yaml()}
          </pre>
        </section>
      </div>
    </div>
  );
}
