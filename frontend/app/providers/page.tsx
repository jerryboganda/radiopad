'use client';

import { useEffect, useMemo, useState } from 'react';
import { api, COMPLIANCE_LABELS, type Provider, type Report } from '@/lib/api';

const SANDBOX_COMPLIANCE = 1;
const SANDBOX_MODES = ['impression', 'draft', 'concise', 'formal', 'patient_friendly', 'referring_summary'] as const;
type SandboxMode = (typeof SANDBOX_MODES)[number];

type CompareRun = {
  providerId: string;
  provider: string;
  model: string;
  output: string | null;
  latencyMs: number;
  inputTokens: number;
  outputTokens: number;
  error: string | null;
};

const COMPLIANCE_BADGE: Record<number, string> = {
  0: 'danger', // Blocked
  1: 'warn',   // Sandbox
  2: 'info',   // De-identified only
  3: 'ok',     // PHI-approved
  4: 'ai',     // Local-only (purple = local)
};

type Editable = Partial<Provider> & { apiKeySecretRef?: string };

type Preset = {
  id: string;
  label: string;
  patch: Partial<Editable>;
};

// Iter-32 AI-011 — operator presets that auto-fill `(adapter, endpointUrl,
// compliance, model)` for the supported deployment shapes. The "local"
// presets default to `LocalOnly` (compliance class 4) so the PHI policy
// in `AiGateway.EnforcePhiPolicy` accepts them. Cloud presets default to
// `PhiApproved` (3) where the vendor has BAA, otherwise `Sandbox` (1).
const PRESETS: Preset[] = [
  { id: 'ollama-chat',  label: 'Ollama (local)',         patch: { adapter: 'ollama-chat',  endpointUrl: 'http://127.0.0.1:11434', compliance: 4, model: 'llama3.1:8b-instruct' } },
  { id: 'vllm',         label: 'vLLM (local)',           patch: { adapter: 'vllm',         endpointUrl: 'http://127.0.0.1:8000',  compliance: 4, model: 'default' } },
  { id: 'llama-cpp',    label: 'llama.cpp (local)',      patch: { adapter: 'llama-cpp',    endpointUrl: 'http://127.0.0.1:8080',  compliance: 4, model: 'llama-cpp' } },
  { id: 'azure-openai', label: 'Azure OpenAI (PHI-OK)',  patch: { adapter: 'azure-openai', endpointUrl: '',                       compliance: 3, model: 'gpt-4o' } },
  { id: 'aws-bedrock',  label: 'AWS Bedrock (PHI-OK)',   patch: { adapter: 'aws-bedrock',  endpointUrl: '',                       compliance: 3, model: 'anthropic.claude-3-5-sonnet-20241022-v2:0' } },
  { id: 'google-vertex-ai', label: 'GCP Vertex AI',      patch: { adapter: 'google-vertex-ai', endpointUrl: '',                   compliance: 3, model: 'gemini-1.5-pro' } },
  { id: 'anthropic',    label: 'Anthropic (sandbox)',    patch: { adapter: 'anthropic',    endpointUrl: '',                       compliance: 1, model: 'claude-3-5-sonnet-20241022' } },
  { id: 'openai-direct',label: 'OpenAI direct',          patch: { adapter: 'openai-direct', endpointUrl: '',                      compliance: 1, model: 'gpt-4o-mini' } },
  { id: 'openai-compatible', label: 'OpenAI-compatible', patch: { adapter: 'openai-compatible', endpointUrl: '',                  compliance: 1, model: '' } },
];

const EMPTY_DRAFT: Editable = {
  id: undefined,
  name: '',
  adapter: 'mock',
  model: '',
  endpointUrl: '',
  compliance: 1,
  enabled: true,
  priority: 100,
  apiKeySecretRef: '',
  retentionLabel: '',
};

export default function ProvidersPage() {
  const [items, setItems] = useState<Provider[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<Editable | null>(null);
  const [saving, setSaving] = useState(false);

  async function refresh() {
    setItems(await api.providers.list());
  }

  useEffect(() => {
    api.providers.list().then(setItems).catch((e: Error) => setError(e.message));
  }, []);

  async function toggleEnabled(p: Provider) {
    await api.providers.save({ ...p, enabled: !p.enabled });
    await refresh();
  }

  function editProvider(p: Provider) {
    setDraft({ ...p, apiKeySecretRef: '' });
  }

  function newProvider() {
    setDraft({ ...EMPTY_DRAFT });
  }

  async function save() {
    if (!draft) return;
    setSaving(true);
    try {
      await api.providers.save(draft);
      setDraft(null);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  const [healthMsg, setHealthMsg] = useState<Record<string, string>>({});
  async function probeHealth(p: Provider) {
    setHealthMsg((m) => ({ ...m, [p.id]: '…' }));
    try {
      const r = await api.providers.health(p.id);
      setHealthMsg((m) => ({ ...m, [p.id]: r.ok ? 'OK' : `✖ ${r.error ?? 'unreachable'}` }));
    } catch (e) {
      setHealthMsg((m) => ({ ...m, [p.id]: `✖ ${(e as Error).message}` }));
    }
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">AI providers</h1>
      <p className="rp-page-sub">
        Tenant provider registry. PHI requests are blocked unless the destination provider's compliance class is{' '}
        <span className="badge ok">PHI-approved</span> or <span className="badge ai">Local-only</span>.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <div className="rp-row between" style={{ marginBottom: 12 }}>
          <div className="rp-panel-title" style={{ marginBottom: 0 }}>Configured providers</div>
          <button className="primary" onClick={newProvider}>+ New provider</button>
        </div>
        <table className="rp-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Adapter</th>
              <th>Model</th>
              <th>Compliance</th>
              <th>Enabled</th>
              <th>Key</th>
              <th>Health</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {items.map((p) => (
              <tr key={p.id}>
                <td>{p.name}</td>
                <td><code>{p.adapter}</code></td>
                <td>{p.model || <span className="rp-faint">(default)</span>}</td>
                <td>
                  <span className={`badge ${COMPLIANCE_BADGE[p.compliance] || ''}`}>
                    {COMPLIANCE_LABELS[p.compliance] || `class ${p.compliance}`}
                  </span>
                  {p.retentionLabel ? (
                    <div className="rp-faint" style={{ marginTop: 4 }}>
                      <code>{p.retentionLabel}</code>
                    </div>
                  ) : null}
                </td>
                <td>
                  <button className="subtle" onClick={() => toggleEnabled(p)}>
                    {p.enabled ? 'Enabled' : 'Disabled'}
                  </button>
                </td>
                <td>{p.apiKeyConfigured ? <span className="badge ok">configured</span> : <span className="badge warn">missing</span>}</td>
                <td>
                  <button className="subtle" onClick={() => probeHealth(p)} title="Iter-32 AI-011 health probe">Test</button>
                  {healthMsg[p.id] && <span className="rp-faint" style={{ marginLeft: 6 }}>{healthMsg[p.id]}</span>}
                </td>
                <td><button className="subtle" onClick={() => editProvider(p)}>Edit</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <SandboxComparePanel providers={items} />

      {draft && (
        <div className="rp-modal-backdrop" onClick={() => setDraft(null)}>
          <div className="rp-panel rp-modal" onClick={(e) => e.stopPropagation()}>
            <div className="rp-panel-title">{draft.id ? 'Edit provider' : 'New provider'}</div>

            <label className="rp-field">
              <span>Preset</span>
              <select
                className="rp-input"
                value=""
                onChange={(e) => {
                  const p = PRESETS.find((x) => x.id === e.target.value);
                  if (p) setDraft({ ...draft, ...p.patch });
                }}
              >
                <option value="">— apply preset —</option>
                {PRESETS.map((p) => (
                  <option key={p.id} value={p.id}>{p.label}</option>
                ))}
              </select>
            </label>

            <label className="rp-field">
              <span>Name</span>
              <input className="rp-input" value={draft.name ?? ''} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
            </label>

            <label className="rp-field">
              <span>Adapter</span>
              <select className="rp-input" value={draft.adapter ?? 'mock'} onChange={(e) => setDraft({ ...draft, adapter: e.target.value })}>
                <option value="mock">mock</option>
                <option value="anthropic">anthropic</option>
                <option value="openai-direct">openai-direct</option>
                <option value="openai-compatible">openai-compatible</option>
                <option value="azure-openai">azure-openai</option>
                <option value="aws-bedrock">aws-bedrock</option>
                <option value="google-vertex-ai">google-vertex-ai</option>
                <option value="ollama">ollama (legacy /api/generate)</option>
                <option value="ollama-chat">ollama-chat (iter-32 /api/chat)</option>
                <option value="vllm">vllm (iter-32 local)</option>
                <option value="llama-cpp">llama-cpp (iter-32 local)</option>
              </select>
            </label>

            <label className="rp-field">
              <span>Model</span>
              <input className="rp-input" value={draft.model ?? ''} onChange={(e) => setDraft({ ...draft, model: e.target.value })} placeholder="claude-3-5-sonnet-20241022 / llama3.1 / …" />
            </label>

            <label className="rp-field">
              <span>Endpoint URL</span>
              <input className="rp-input" value={draft.endpointUrl ?? ''} onChange={(e) => setDraft({ ...draft, endpointUrl: e.target.value })} placeholder="(default per adapter)" />
            </label>

            <label className="rp-field">
              <span>Compliance class</span>
              <select
                className="rp-input"
                value={draft.compliance ?? 1}
                onChange={(e) => setDraft({ ...draft, compliance: Number(e.target.value) })}
              >
                {Object.entries(COMPLIANCE_LABELS).map(([k, v]) => (
                  <option key={k} value={k}>{k} — {v}</option>
                ))}
              </select>
            </label>

            <label className="rp-field">
              <span>Retention label</span>
              <input
                className="rp-input"
                value={draft.retentionLabel ?? ''}
                onChange={(e) => setDraft({ ...draft, retentionLabel: e.target.value })}
                placeholder="no-egress / 30d-soft-delete / baa-30d / vendor-controlled-zdr"
              />
            </label>

            <label className="rp-field">
              <span>API key secret ref</span>
              <input
                className="rp-input"
                value={draft.apiKeySecretRef ?? ''}
                onChange={(e) => setDraft({ ...draft, apiKeySecretRef: e.target.value })}
                placeholder="env:ANTHROPIC_API_KEY (leave blank to keep existing)"
              />
            </label>

            <label className="rp-field">
              <span>Quality (0–1)</span>
              <input
                className="rp-input"
                type="number"
                step="0.05"
                min={0}
                max={1}
                value={Number(draft.quality ?? 0.5)}
                onChange={(e) => setDraft({ ...draft, quality: Number(e.target.value) })}
              />
            </label>

            <label className="rp-field">
              <span>Priority</span>
              <input
                className="rp-input"
                type="number"
                value={draft.priority ?? 100}
                onChange={(e) => setDraft({ ...draft, priority: Number(e.target.value) })}
              />
            </label>

            <label className="rp-field rp-row">
              <input
                type="checkbox"
                checked={!!draft.enabled}
                onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })}
              />
              <span style={{ marginLeft: 8 }}>Enabled</span>
            </label>

            <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
              <button className="ghost" onClick={() => setDraft(null)} disabled={saving}>Cancel</button>
              <button className="primary" onClick={save} disabled={saving || !draft.name}>
                {saving ? 'Saving…' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * PRD PROV-005 (iter-34) — sandbox model comparison panel. Only rendered
 * when the tenant has at least two enabled `Sandbox`-class providers. The
 * radiologist picks a draft report + a mode + 2..4 sandbox providers and
 * the backend runs each in series via `POST /api/ai/sandbox/compare`. PHI
 * policy is still enforced inside `AiGateway.EnforcePhiPolicy` — this
 * panel never bypasses it.
 */
function SandboxComparePanel({ providers }: { providers: Provider[] }) {
  const sandbox = useMemo(
    () => providers.filter((p) => p.enabled && p.compliance === SANDBOX_COMPLIANCE),
    [providers],
  );
  const [reports, setReports] = useState<Report[]>([]);
  const [reportId, setReportId] = useState('');
  const [mode, setMode] = useState<SandboxMode>('impression');
  const [selected, setSelected] = useState<string[]>([]);
  const [runs, setRuns] = useState<CompareRun[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (sandbox.length < 2) return;
    api.reports
      .list()
      .then((rows) => setReports(rows.filter((r) => r.status === 'Draft' || r.status === 0)))
      .catch((e: Error) => setErr(e.message));
  }, [sandbox.length]);

  function toggle(id: string) {
    setSelected((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : prev.length >= 4 ? prev : [...prev, id],
    );
  }

  async function run() {
    setErr(null);
    setBusy(true);
    setRuns(null);
    try {
      const out = await api.ai.sandboxCompare({ reportId, mode, providerIds: selected });
      setRuns(out.runs);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (sandbox.length < 2) return null;

  const canRun = !!reportId && selected.length >= 2 && !busy;
  const cols = runs && runs.length >= 3 ? 'rp-grid-3' : 'rp-grid-2';

  return (
    <div className="rp-panel" style={{ marginTop: 16 }}>
      <div className="rp-panel-title">Sandbox compare</div>
      <p className="rp-page-sub" style={{ marginTop: 0 }}>
        Run the same prompt across up to four sandbox providers and diff outputs side-by-side.
        Tenant must have <code>AllowSandboxRulebooks = true</code>; PHI policy is still enforced.
      </p>

      {err && <div className="banner warn">{err}</div>}

      <label className="rp-field">
        <span>Draft report</span>
        <select className="rp-input" value={reportId} onChange={(e) => setReportId(e.target.value)}>
          <option value="">— select a draft —</option>
          {reports.map((r) => (
            <option key={r.id} value={r.id}>
              {r.study.accessionNumber} · {r.study.modality} {r.study.bodyPart}
            </option>
          ))}
        </select>
      </label>

      <label className="rp-field">
        <span>Mode</span>
        <select
          className="rp-input"
          value={mode}
          onChange={(e) => setMode(e.target.value as SandboxMode)}
        >
          {SANDBOX_MODES.map((m) => (
            <option key={m} value={m}>{m}</option>
          ))}
        </select>
      </label>

      <div className="rp-field">
        <span>Sandbox providers (pick 2–4)</span>
        <div className="rp-row" style={{ flexWrap: 'wrap', gap: 8 }}>
          {sandbox.map((p) => {
            const on = selected.includes(p.id);
            return (
              <button
                key={p.id}
                type="button"
                className={on ? 'primary-ghost' : 'subtle'}
                onClick={() => toggle(p.id)}
              >
                {on ? '✓ ' : ''}{p.name}{' '}
                <span className="badge">{COMPLIANCE_LABELS[p.compliance]}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
        <button className="primary" onClick={run} disabled={!canRun}>
          {busy ? 'Comparing…' : 'Compare'}
        </button>
      </div>

      {runs && (
        <div className={cols} style={{ marginTop: 12 }}>
          {runs.map((r) => (
            <div key={r.providerId} className="rp-panel">
              <div className="rp-panel-title" style={{ marginBottom: 4 }}>{r.provider}</div>
              <div className="rp-row" style={{ gap: 6, marginBottom: 8, flexWrap: 'wrap' }}>
                <span className="badge"><code>{r.model || '(default)'}</code></span>
                {r.error ? (
                  <span className="badge danger">{r.error}</span>
                ) : (
                  <>
                    <span className="badge">{r.latencyMs} ms</span>
                    <span className="badge">in {r.inputTokens}</span>
                    <span className="badge">out {r.outputTokens}</span>
                  </>
                )}
              </div>
              {r.output && (
                <div className="ai-mark" style={{ whiteSpace: 'pre-wrap' }}>{r.output}</div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
