'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, COMPLIANCE_LABELS, type Provider, type Report } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';

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
  { id: 'google-vertex', label: 'GCP Vertex AI',         patch: { adapter: 'google-vertex', endpointUrl: '',                      compliance: 3, model: 'gemini-1.5-pro' } },
  { id: 'anthropic',    label: 'Anthropic (sandbox)',    patch: { adapter: 'anthropic',    endpointUrl: '',                       compliance: 1, model: 'claude-3-5-sonnet-20241022' } },
  { id: 'openai',       label: 'OpenAI direct',          patch: { adapter: 'openai',       endpointUrl: '',                       compliance: 1, model: 'gpt-4o-mini' } },
  { id: 'openai-compatible', label: 'OpenAI-compatible', patch: { adapter: 'openai-compatible', endpointUrl: '',                  compliance: 1, model: '' } },
  { id: 'github-copilot-sdk', label: 'GitHub Copilot SDK', patch: { adapter: 'github-copilot-sdk', endpointUrl: '',                compliance: 1, model: 'copilot' } },
  { id: 'github-copilot-cli', label: 'GitHub Copilot CLI', patch: { adapter: 'github-copilot-cli', endpointUrl: '',                compliance: 1, model: 'copilot' } },
  { id: 'gemini-cli',   label: 'Gemini CLI',             patch: { adapter: 'gemini-cli',   endpointUrl: '',                       compliance: 1, model: '' } },
  { id: 'codex-cli',    label: 'Codex CLI',              patch: { adapter: 'codex-cli',    endpointUrl: '',                       compliance: 1, model: '' } },
  { id: 'ubag',         label: 'UBAG automation hub',     patch: { adapter: 'ubag',         endpointUrl: '',                       compliance: 1, model: 'gemini_web', retentionLabel: 'non-phi-browser-automation' } },
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
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState<Editable | null>(null);
  const [saving, setSaving] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const rows = await api.providers.list();
      setItems(rows);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

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
    setHealthMsg((m) => ({ ...m, [p.id]: 'Checking' }));
    try {
      const r = await api.providers.health(p.id);
      const httpStatus = 'status' in r && typeof r.status === 'number' ? r.status : null;
      const detail = httpStatus ? ` (${httpStatus})` : r.note ? ` (${r.note})` : '';
      setHealthMsg((m) => ({ ...m, [p.id]: r.ok ? `OK${detail}` : `Unavailable${detail}: ${r.error ?? 'not reachable'}` }));
    } catch (e) {
      setHealthMsg((m) => ({ ...m, [p.id]: `Unavailable: ${(e as Error).message}` }));
    }
  }

  return (
    <Container>
      <PageHeader
        title="AI models"
        description={<>The AI models your workspace can use for drafting reports. Patient information is only sent to models marked <span className="badge ok">Safe for patient data</span> or <span className="badge ai">Runs on-site</span>.</>}
        primaryAction={<button className="primary" onClick={newProvider}>+ Add a model</button>}
      />

      {error && items.length > 0 && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">Available models</div>
        {loading && items.length === 0 ? (
          <TableSkeleton rows={6} cols={8} />
        ) : error && items.length === 0 ? (
          <ErrorState title="Couldn't load models" message={error} onRetry={() => { void refresh(); }} />
        ) : items.length === 0 ? (
          <EmptyState
            title="No AI models yet"
            description="Add the first model for this workspace."
            action={<button className="ghost" onClick={newProvider}>Add model</button>}
          />
        ) : (
          <div style={{ overflowX: 'auto' }}>
          <table className="rp-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Model</th>
                <th>Patient data?</th>
                <th>On / off</th>
                <th>Key</th>
                <th>Connection</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((p) => (
                <tr key={p.id}>
                  <td>{p.name}</td>
                  <td>{p.adapter}</td>
                  <td>{p.model || <span className="rp-faint">(default)</span>}</td>
                  <td>
                    <span className={`badge ${COMPLIANCE_BADGE[p.compliance] || ''}`}>
                      {COMPLIANCE_LABELS[p.compliance] || 'Unknown'}
                    </span>
                  </td>
                  <td>
                    <button className="subtle" onClick={() => toggleEnabled(p)}>
                      {p.enabled ? 'On' : 'Off'}
                    </button>
                  </td>
                  <td>{p.apiKeyConfigured ? <span className="badge ok">Set</span> : <span className="badge warn">Missing</span>}</td>
                  <td>
                    <button className="subtle" onClick={() => probeHealth(p)} title="Test connection">Test</button>
                    {healthMsg[p.id] && <span className="rp-faint" style={{ marginLeft: 6 }}>{healthMsg[p.id]}</span>}
                  </td>
                  <td><button className="subtle" onClick={() => editProvider(p)}>Edit</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          </div>
        )}
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
                <option value="openai">openai</option>
                <option value="openai-compatible">openai-compatible</option>
                <option value="azure-openai">azure-openai</option>
                <option value="aws-bedrock">aws-bedrock</option>
                <option value="google-vertex">google-vertex</option>
                <option value="ollama">ollama (legacy /api/generate)</option>
                <option value="ollama-chat">ollama-chat (iter-32 /api/chat)</option>
                <option value="vllm">vllm (iter-32 local)</option>
                <option value="llama-cpp">llama-cpp (iter-32 local)</option>
                <option value="github-copilot-sdk">github-copilot-sdk</option>
                <option value="github-copilot-cli">github-copilot-cli</option>
                <option value="gemini-cli">gemini-cli</option>
                <option value="codex-cli">codex-cli</option>
                <option value="ubag">ubag</option>
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
                  <option key={k} value={k}>{v}</option>
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
              <span>Secret reference</span>
              <input
                className="rp-input"
                value={draft.apiKeySecretRef ?? ''}
                onChange={(e) => setDraft({ ...draft, apiKeySecretRef: e.target.value })}
                placeholder="Leave blank to keep existing"
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
    </Container>
  );
}

/**
 * PRD PROV-005 (iter-34) — sandbox model comparison panel. Only rendered
 * when the tenant has at least one enabled `Sandbox`-class provider. The
 * radiologist picks a draft report + a mode + 1..4 sandbox providers and
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
  const [reportsLoading, setReportsLoading] = useState(false);
  const [reportId, setReportId] = useState('');
  const [mode, setMode] = useState<SandboxMode>('impression');
  const [selected, setSelected] = useState<string[]>([]);
  const [runs, setRuns] = useState<CompareRun[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (sandbox.length < 1) return;
    setReportsLoading(true);
    setErr(null);
    api.reports
      .list()
      .then((rows) => setReports(rows.filter((r) => r.status === 'Draft' || r.status === 0)))
      .catch((e: Error) => setErr(e.message))
      .finally(() => setReportsLoading(false));
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

  if (sandbox.length < 1) return null;

  const canRun = !!reportId && selected.length >= 1 && !busy && !reportsLoading;
  const cols = runs && runs.length >= 3 ? 'rp-grid-3' : 'rp-grid-2';

  return (
    <div className="rp-panel" style={{ marginTop: 16 }}>
      <div className="rp-panel-title">Sandbox compare</div>
      <p className="rp-page-sub" style={{ marginTop: 0 }}>
        Draft outputs stay governed by workspace policy.
      </p>

      {err && <div className="banner warn">{err}</div>}

      {reportsLoading ? (
        <TableSkeleton rows={2} cols={3} />
      ) : reports.length === 0 ? (
        <EmptyState title="No draft reports available" description="Create or reopen a draft report before comparing sandbox model output." />
      ) : (
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
      )}

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
        <span>Sandbox models</span>
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
