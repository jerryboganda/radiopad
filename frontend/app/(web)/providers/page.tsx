'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, COMPLIANCE_LABELS, type Provider, type Report } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import { TableSkeleton } from '@/components/ui/Skeleton';
import OnDeviceModels from '@/components/models/OnDeviceModels';
import { FALLBACK_UBAG_TARGETS } from '@/lib/ubagTargets';

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
// compliance, model)` for the supported deployment shapes. The compliance
// value each preset carries is INFORMATIONAL only: PHI gating was removed on
// 2026-07-20, so it no longer decides what a provider may receive — a class-1
// (Sandbox) preset will be sent PHI exactly like a class-4 (LocalOnly) one.
// The labels are kept because they still tell an operator what they are
// pointing at, which is the only thing standing in for the old gate.
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
  const [tab, setTab] = useState<'cloud' | 'on-device'>('cloud');
  const [items, setItems] = useState<Provider[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState<Editable | null>(null);
  const [saving, setSaving] = useState(false);
  const [ubagTargets, setUbagTargets] = useState<string[]>(FALLBACK_UBAG_TARGETS);

  // Deep-link the active tab via ?tab=on-device without pulling in next/navigation
  // (keeps this page out of a Suspense boundary). Default 'cloud'; sync after mount.
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get('tab') === 'on-device') setTab('on-device');
  }, []);
  const selectTab = useCallback((next: 'cloud' | 'on-device') => {
    setTab(next);
    const url = new URL(window.location.href);
    if (next === 'cloud') url.searchParams.delete('tab');
    else url.searchParams.set('tab', next);
    window.history.replaceState(null, '', url.toString());
  }, []);

  // Lazily fetch allowed UBAG targets when the modal is open with adapter=ubag.
  // Swallow errors — non-admin callers get a 403 and the fallback list is used.
  useEffect(() => {
    if (draft?.adapter !== 'ubag') return;
    let cancelled = false;
    // Seed model synchronously so the controlled <select> and draft.model agree
    // even if the status fetch fails (e.g. 403 for non-admins).
    setDraft((prev) => (prev && !prev.model ? { ...prev, model: ubagTargets[0] } : prev));
    api.ubag.status().then((s) => {
      if (cancelled) return;
      const list = s.allowedTargets?.length ? s.allowedTargets : FALLBACK_UBAG_TARGETS;
      setUbagTargets(list);
      setDraft((prev) => (prev && !prev.model ? { ...prev, model: list[0] } : prev));
    }).catch(() => { /* swallow — use fallback list */ });
    return () => { cancelled = true; };
  }, [draft?.adapter]); // do NOT add ubagTargets to deps

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
        primaryAction={tab === 'cloud' ? <button className="primary" onClick={newProvider}>+ Add a model</button> : undefined}
      />

      <div className="tab-list" role="tablist" aria-label="Provider types" style={{ marginBottom: 16 }}>
        <button className="tab-button" role="tab" aria-selected={tab === 'cloud'} onClick={() => selectTab('cloud')}>Cloud providers</button>
        <button className="tab-button" role="tab" aria-selected={tab === 'on-device'} onClick={() => selectTab('on-device')}>On-device models</button>
      </div>

      {tab === 'on-device' && <OnDeviceModels />}

      {tab === 'cloud' && (
      <>
      {error && items.length > 0 && (
        <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>
      )}

      <div className="rp-panel" aria-live="polite" aria-busy={loading}>
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
      </>
      )}

      {draft && (
        <div className="rp-modal-backdrop" onClick={() => setDraft(null)}>
          <div className="rp-panel rp-modal rp-anim-scale-in" role="dialog" aria-modal="true" onClick={(e) => e.stopPropagation()}>
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
                <option value="gemini-cli">gemini-cli</option>
                <option value="codex-cli">codex-cli</option>
                <option value="ubag">ubag</option>
              </select>
            </label>

            <label className="rp-field">
              <span>Model</span>
              {draft.adapter === 'ubag' ? (
                <select
                  className="rp-input"
                  value={draft.model ?? ''}
                  onChange={(e) => setDraft({ ...draft, model: e.target.value })}
                >
                  {/* Include saved model as extra option if it is not in the fetched list */}
                  {draft.model && !ubagTargets.includes(draft.model) && (
                    <option key={draft.model} value={draft.model}>{draft.model}</option>
                  )}
                  {ubagTargets.map((t) => (
                    <option key={t} value={t}>{t}</option>
                  ))}
                </select>
              ) : (
                <input className="rp-input" value={draft.model ?? ''} onChange={(e) => setDraft({ ...draft, model: e.target.value })} placeholder="claude-3-5-sonnet-20241022 / llama3.1 / …" />
              )}
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
              <button className="primary" onClick={save} disabled={saving || !draft.name} aria-busy={saving}>
                {saving && <span className="rp-spinner sm" aria-hidden />}
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

      {err && <Banner tone="warn">{err}</Banner>}

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
        <button className="primary" onClick={run} disabled={!canRun} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />}
          {busy ? 'Comparing…' : 'Compare'}
        </button>
      </div>

      {runs && (
        <div className={`${cols} rp-stagger`} style={{ marginTop: 12 }} aria-live="polite">
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
