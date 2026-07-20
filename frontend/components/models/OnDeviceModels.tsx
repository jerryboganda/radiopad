'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  BrainCircuit,
  CircleCheck,
  CircleDashed,
  Download,
  Mic,
  Play,
  RotateCcw,
  Server,
  Sparkles,
  Trash2,
  Volume2,
} from 'lucide-react';
import {
  api,
  type LocalModel,
  type LocalModelKind,
  type ModelProvisioning,
  type ModelTestResult,
  type Provider,
} from '@/lib/api';
import { ensureOnDeviceProvider, findOnDeviceProvider } from '@/lib/models/onDeviceProvider';
import { probeWebSpeechAvailable } from '@/lib/dictation/speech';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';

const KIND_ORDER: LocalModelKind[] = ['Stt', 'Tts', 'Orchestrator'];
const KIND_LABEL: Record<LocalModelKind, string> = {
  Stt: 'Speech-to-text (dictation)',
  Tts: 'Text-to-speech',
  Orchestrator: 'Report formatting (on-device AI)',
};
const KIND_ICON: Record<LocalModelKind, typeof Mic> = {
  Stt: Mic,
  Tts: Volume2,
  Orchestrator: BrainCircuit,
};

const ACTIVE_STATES = ['Downloading', 'Verifying', 'Extracting', 'Installing'];
/** States where a percentage would be invented — the total is not known yet. */
const INDETERMINATE_STATES = ['Verifying', 'Extracting', 'Installing'];

function fmtBytes(n: number): string {
  if (!n) return '—';
  const mb = n / (1024 * 1024);
  return mb >= 1024 ? `${(mb / 1024).toFixed(2)} GB` : `${Math.round(mb)} MB`;
}

function errText(e: unknown): string {
  const err = e as { body?: { error?: string } };
  return err?.body?.error ?? (e as Error)?.message ?? 'Something went wrong.';
}

function provisioningOf(m: LocalModel): ModelProvisioning {
  return m.provisioning ?? 'HostedFile';
}

/** Orchestrator entries need a runtime as well as the model — see {@link ModelRuntime}. */
function isOrchestrator(m: LocalModel): boolean {
  return m.kind === 'Orchestrator' && !m.placeholder;
}

/**
 * Is this entry usable right now? Deliberately not just `downloaded`: an orchestrator
 * with its GGUF on disk but no runtime is not ready, and saying otherwise is what made
 * the old card claim "Ready" for a model that could not run.
 */
function isReady(m: LocalModel): boolean {
  if (m.placeholder) return false;
  if (provisioningOf(m) !== 'HostedFile') return m.available;
  if (m.kind === 'Orchestrator') return m.available;
  return m.downloaded;
}

/**
 * On-device AI model manager — the "On-device models" tab of the AI models page.
 * Lists the local model catalog grouped by kind, with download (live progress),
 * re-download/repair, delete, test, and copyable diagnostics.
 *
 * Orchestrator models additionally expose their prerequisite chain (model → runtime →
 * server) and can be registered as a report-generation provider, which is the only
 * route to that from the desktop app: the provider admin screen lives in the `(web)`
 * route group and is staged out of the desktop bundle.
 *
 * Driven entirely by the backend `enabled` flag, so on the web it renders read-only
 * with a desktop-only notice.
 */
export default function OnDeviceModels() {
  const [enabled, setEnabled] = useState(true);
  const [models, setModels] = useState<LocalModel[]>([]);
  const [providers, setProviders] = useState<Provider[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const res = await api.localModels.list();
      setEnabled(res.enabled);
      setModels(res.models);
      setError(null);
    } catch (e) {
      setError(errText(e));
    } finally {
      setLoading(false);
    }
  }, []);

  // Which on-device models are already registered as report-generation providers.
  // Best-effort: a user without ProvidersRead still gets a working model manager,
  // they just do not see the "already selectable" state.
  const refreshProviders = useCallback(async () => {
    try {
      setProviders(await api.providers.list());
    } catch {
      setProviders(null);
    }
  }, []);

  useEffect(() => {
    void refresh();
    void refreshProviders();
  }, [refresh, refreshProviders]);

  // Poll while any model is mid-download so the progress bars advance.
  useEffect(() => {
    const active = models.some((m) => ACTIVE_STATES.includes(m.progress.state));
    if (!active) return;
    const t = setInterval(() => {
      void refresh();
    }, 1200);
    return () => clearInterval(t);
  }, [models, refresh]);

  const grouped = useMemo(
    () =>
      KIND_ORDER.map((kind) => ({
        kind,
        items: models.filter((m) => m.kind === kind),
      })).filter((g) => g.items.length > 0),
    [models],
  );

  if (loading && models.length === 0) return <TableSkeleton rows={3} cols={3} />;
  if (error && models.length === 0)
    return (
      <ErrorState
        title="Couldn't load on-device models"
        message={error}
        onRetry={() => {
          void refresh();
        }}
      />
    );

  return (
    <div>
      {!enabled && (
        <div className="banner info">
          On-device models are downloaded and run by the RadioPad desktop app. Open RadioPad on your
          workstation to download, test, or manage them — on the web they appear here for reference only.
        </div>
      )}
      {error && models.length > 0 && <div className="banner warn">{error}</div>}

      {grouped.map((g) => {
        const SectionIcon = KIND_ICON[g.kind];
        const ready = g.items.filter(isReady).length;
        const real = g.items.filter((m) => !m.placeholder).length;
        return (
          <section key={g.kind} className="rp-model-section">
            <div className="rp-model-section-head">
              <span className="rp-model-section-icon">
                <SectionIcon aria-hidden size={16} />
              </span>
              <h3>{KIND_LABEL[g.kind]}</h3>
              {real > 0 && (
                <span className="rp-model-section-count">
                  {ready} of {real} ready
                </span>
              )}
            </div>
            <div className="rp-model-grid">
              {g.items.map((m) => (
                <ModelCard
                  key={m.id}
                  model={m}
                  enabled={enabled}
                  provider={providers ? findOnDeviceProvider(providers, m.id) : undefined}
                  onChanged={async () => {
                    await refresh();
                    await refreshProviders();
                  }}
                />
              ))}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function StateBadge({ model, edgeOk }: { model: LocalModel; edgeOk?: boolean | null }) {
  if (model.placeholder) return <span className="badge">Coming soon</span>;
  const provisioning = provisioningOf(model);
  if (provisioning === 'BrowserWebSpeech') {
    if (edgeOk == null) return <span className="badge">Checking…</span>;
    return edgeOk ? (
      <span className="badge ok">Available</span>
    ) : (
      <span className="badge warn">Unavailable here</span>
    );
  }
  if (provisioning === 'WindowsBuiltIn')
    return model.available ? (
      <span className="badge ok">Built in</span>
    ) : (
      <span className="badge warn">Unavailable</span>
    );
  if (provisioning === 'WindowsLanguagePack')
    return model.available ? (
      <span className="badge ok">Ready</span>
    ) : (
      <span className="badge warn">Needs language pack</span>
    );

  const s = model.progress.state;
  if (ACTIVE_STATES.includes(s)) return <span className="badge ai">{s}…</span>;
  if (s === 'Failed') return <span className="badge danger">Failed</span>;
  if (!model.downloaded) return <span className="badge warn">Not downloaded</span>;
  // Downloaded but the rest of the chain is incomplete — say so rather than "Ready".
  if (isOrchestrator(model) && !model.available)
    return <span className="badge warn">Setup incomplete</span>;
  return <span className="badge ok">Ready</span>;
}

/** Live download/install progress. Percentage only when the total is actually known. */
function ProgressRow({ model }: { model: LocalModel }) {
  const { state, bytesDownloaded, totalBytes } = model.progress;
  const indeterminate = INDETERMINATE_STATES.includes(state) || totalBytes <= 0;
  const pct = totalBytes > 0 ? Math.min(100, Math.round((bytesDownloaded / totalBytes) * 100)) : null;

  return (
    <div>
      <div className="rp-progress-label">
        <span>
          {state}
          {!indeterminate && totalBytes > 0 && (
            <> — {fmtBytes(bytesDownloaded)} of {fmtBytes(totalBytes)}</>
          )}
        </span>
        {!indeterminate && pct !== null && <b>{pct}%</b>}
      </div>
      <div
        className="rp-progress"
        data-indeterminate={indeterminate ? 'true' : 'false'}
        role="progressbar"
        aria-label={`${model.displayName} — ${state}`}
        aria-valuenow={indeterminate || pct === null ? undefined : pct}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <span className="rp-progress-fill" style={indeterminate ? undefined : { width: `${pct ?? 0}%` }} />
      </div>
    </div>
  );
}

/**
 * The prerequisite chain for an orchestrator model. Each link is shown separately
 * because any one of them can be the reason formatting is unavailable, and a single
 * combined pill gives the user nothing to act on.
 */
function RuntimeChain({ model }: { model: LocalModel }) {
  const runtime = model.runtime ?? null;
  const steps = [
    { label: 'Model file', ok: model.downloaded, note: fmtBytes(model.sizeBytes) },
    {
      label: 'llama.cpp runtime',
      ok: runtime?.installed ?? false,
      note: runtime?.installed ? 'installed' : 'arrives with the model',
    },
    {
      label: 'Local server',
      ok: runtime?.running ?? false,
      note: runtime?.running ? 'running' : 'starts on first use',
    },
  ];

  return (
    <div className="rp-model-chain">
      {steps.map((s) => (
        <div key={s.label} className="rp-model-chain-step" data-ok={s.ok ? 'true' : 'false'}>
          <span className="rp-model-chain-icon">
            {s.ok ? <CircleCheck aria-hidden size={13} /> : <CircleDashed aria-hidden size={13} />}
          </span>
          <span>{s.label}</span>
          <span className="rp-model-chain-note">{s.note}</span>
        </div>
      ))}
    </div>
  );
}

function ModelCard({
  model,
  enabled,
  provider,
  onChanged,
}: {
  model: LocalModel;
  enabled: boolean;
  /** The registered report-generation provider for this model, if any. */
  provider: Provider | undefined;
  onChanged: () => Promise<void> | void;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ text: string; tone: 'error' | 'success' | 'plain' } | null>(null);
  const [test, setTest] = useState<ModelTestResult | null>(null);
  const [testOpen, setTestOpen] = useState(false);
  // Edge Web Speech availability — probed in the WebView, not reported by the
  // sidecar (null = not yet checked).
  const [edgeOk, setEdgeOk] = useState<boolean | null>(null);

  const provisioning = provisioningOf(model);
  const isBrowser = provisioning === 'BrowserWebSpeech';
  const isBuiltIn = provisioning === 'WindowsBuiltIn';
  const isLangPack = provisioning === 'WindowsLanguagePack';
  const isPlatform = provisioning !== 'HostedFile';
  const orchestrator = isOrchestrator(model);
  // Older sidecars omit `supportsPrimary`; primary has always been STT-only.
  const supportsPrimary = model.supportsPrimary ?? model.kind === 'Stt';
  const registered = Boolean(provider?.enabled);

  useEffect(() => {
    if (!isBrowser || !enabled) return;
    let cancelled = false;
    void probeWebSpeechAvailable().then((r) => {
      if (!cancelled) setEdgeOk(r.ok);
    });
    return () => {
      cancelled = true;
    };
  }, [isBrowser, enabled]);

  const inProgress = ACTIVE_STATES.includes(model.progress.state);

  async function run(key: string, fn: () => Promise<void>) {
    setBusy(key);
    setMsg(null);
    try {
      await fn();
    } catch (e) {
      setMsg({ text: errText(e), tone: 'error' });
    } finally {
      setBusy(null);
    }
  }

  const doDownload = (force = false) =>
    run(force ? 'redownload' : 'download', async () => {
      await api.localModels.download(model.id, force);
      await onChanged();
    });

  const doDelete = () =>
    run('delete', async () => {
      if (!window.confirm(`Delete ${model.displayName}? You can re-download it anytime.`)) return;
      await api.localModels.remove(model.id);
      await onChanged();
    });

  const doTest = () =>
    run('test', async () => {
      setTest(null);
      setTestOpen(true);
      try {
        setTest(await api.localModels.test(model.id));
      } catch (e) {
        setTestOpen(false);
        throw e;
      }
    });

  const doSetPrimary = () =>
    run('primary', async () => {
      await api.localModels.setPrimary(model.id);
      await onChanged();
    });

  /**
   * Register this model as a selectable provider for report generation. Creates the
   * tenant `ProviderConfig` row nothing else creates from the desktop app.
   */
  const doUseForReports = () =>
    run('provider', async () => {
      const res = await ensureOnDeviceProvider(model.id, `${model.displayName} (on-device)`);
      if (res.status === 'forbidden') {
        setMsg({
          text:
            'Adding a provider is an administrator action. Ask an admin to enable this model for '
            + 'report generation — the model itself is installed and ready on this workstation.',
          tone: 'error',
        });
        return;
      }
      setMsg({
        text:
          res.status === 'already'
            ? 'Already available — pick it in the provider list when you generate a report.'
            : 'Added. Pick it in the provider list when you generate a report; cloud stays the default until you do.',
        tone: 'success',
      });
      await onChanged();
    });

  // Edge Web Speech "Test" — run the live microphone probe in the WebView (the
  // engine runs here, not in the sidecar).
  const doProbeTest = () =>
    run('test', async () => {
      const r = await probeWebSpeechAvailable();
      setEdgeOk(r.ok);
      setMsg({
        text: r.ok
          ? 'Microsoft Edge speech is available in this app window.'
          : `Edge speech is not available here (${r.error ?? 'unknown'}). Use an on-device engine instead.`,
        tone: r.ok ? 'success' : 'error',
      });
    });

  // WinRT language pack — "download" opens Windows speech settings.
  const doOpenSettings = () =>
    run('download', async () => {
      await api.localModels.download(model.id);
      setMsg({
        text: 'Opened Windows speech settings. Install/enable a language pack, then press Test.',
        tone: 'plain',
      });
      await onChanged();
    });

  async function copyDiagnostics() {
    try {
      const diagnostics = await api.localModels.diagnostics(model.id);
      await navigator.clipboard.writeText(JSON.stringify({ test, diagnostics }, null, 2));
      setMsg({ text: 'Diagnostics copied to clipboard.', tone: 'success' });
    } catch (e) {
      setMsg({ text: errText(e), tone: 'error' });
    }
  }

  const KindIcon = KIND_ICON[model.kind];
  const cardState = model.progress.state === 'Failed' ? 'failed' : undefined;

  return (
    <div
      className="rp-model-card"
      data-selected={model.isPrimary || registered ? 'true' : 'false'}
      data-state={cardState}
    >
      <div className="rp-model-card-head">
        <span className="rp-model-icon" data-kind={model.kind}>
          <KindIcon aria-hidden size={17} />
        </span>
        <div className="rp-model-headings">
          <div className="rp-model-title">{model.displayName}</div>
          <code className="rp-model-id">{model.id}</code>
        </div>
        <StateBadge model={model} edgeOk={edgeOk} />
      </div>

      <div className="rp-model-meta">
        {model.isPrimary && <span className="badge ai">Primary</span>}
        {registered && <span className="badge ai">In report generation</span>}
        {!model.placeholder && !isPlatform && <span className="badge">{fmtBytes(model.sizeBytes)}</span>}
        {model.license && <span className="badge">{model.license}</span>}
        {model.available && !orchestrator && <span className="badge ok">Loaded</span>}
      </div>

      {model.note && (
        <p className="rp-model-note" data-testid="model-note">
          {model.note}
        </p>
      )}

      {orchestrator && model.downloaded && <RuntimeChain model={model} />}

      {inProgress && <ProgressRow model={model} />}

      {model.progress.state === 'Failed' && model.progress.error && (
        <div className="banner danger" style={{ fontSize: 12 }}>
          {model.progress.error}
        </div>
      )}

      {model.placeholder ? (
        <p className="rp-model-note">
          Coming in a future release — it will be downloadable and testable here, just like the
          speech-to-text models.
        </p>
      ) : isBrowser ? (
        <div className="rp-model-actions">
          {!model.isPrimary && (
            <button
              className="primary-ghost"
              onClick={doSetPrimary}
              disabled={!enabled || busy !== null}
              aria-busy={busy === 'primary'}
            >
              {busy === 'primary' ? 'Setting…' : 'Make primary'}
            </button>
          )}
          <button className="subtle" onClick={doProbeTest} disabled={busy !== null} aria-busy={busy === 'test'}>
            <Play aria-hidden size={14} /> {busy === 'test' ? 'Testing…' : 'Test in app'}
          </button>
        </div>
      ) : isPlatform ? (
        <div className="rp-model-actions">
          {isLangPack && (
            <button
              className="primary"
              onClick={doOpenSettings}
              disabled={!enabled || busy !== null}
              aria-busy={busy === 'download'}
            >
              {busy === 'download' ? 'Opening…' : 'Open Windows speech settings'}
            </button>
          )}
          {isBuiltIn && !model.isPrimary && (
            <button
              className="primary-ghost"
              onClick={doSetPrimary}
              disabled={!enabled || busy !== null}
              aria-busy={busy === 'primary'}
            >
              {busy === 'primary' ? 'Setting…' : 'Make primary'}
            </button>
          )}
          <button className="subtle" onClick={doTest} disabled={!enabled || busy !== null} aria-busy={busy === 'test'}>
            <Play aria-hidden size={14} /> {busy === 'test' ? 'Testing…' : 'Test'}
          </button>
        </div>
      ) : (
        <div className="rp-model-actions">
          {!model.downloaded ? (
            <button
              className="primary"
              onClick={() => doDownload(false)}
              disabled={!enabled || busy !== null || inProgress}
              aria-busy={busy === 'download' || inProgress}
            >
              <Download aria-hidden size={14} />{' '}
              {busy === 'download' || inProgress ? 'Downloading…' : `Download ${fmtBytes(model.sizeBytes)}`}
            </button>
          ) : (
            <>
              {/* Orchestrator models are chosen per report through the provider picker,
                  not as a dictation "primary" — the backend rejects the latter. */}
              {orchestrator && !registered && (
                <button
                  className="primary"
                  onClick={doUseForReports}
                  // Gated on the model being usable, NOT on whether we could read the
                  // provider list. Those are unrelated: failing to read providers is a
                  // permissions question, and letting it enable a button for a model
                  // whose runtime is missing would offer a choice that cannot work.
                  disabled={!enabled || busy !== null || !model.available}
                  aria-busy={busy === 'provider'}
                  title={
                    model.available
                      ? undefined
                      : 'Finish the setup above before using this model for report generation.'
                  }
                >
                  <Sparkles aria-hidden size={14} />{' '}
                  {busy === 'provider' ? 'Adding…' : 'Use for report generation'}
                </button>
              )}
              {supportsPrimary && !model.isPrimary && (
                <button
              className="primary-ghost"
              onClick={doSetPrimary}
              disabled={!enabled || busy !== null}
              aria-busy={busy === 'primary'}
            >
                  {busy === 'primary' ? 'Setting…' : 'Make primary'}
                </button>
              )}
              <button className="subtle" onClick={doTest} disabled={!enabled || busy !== null} aria-busy={busy === 'test'}>
                <Play aria-hidden size={14} /> {busy === 'test' ? 'Testing…' : 'Test'}
              </button>
              {/* Always available once installed: the only in-app recovery for a
                  corrupt or partial model, which otherwise reports "Ready" forever. */}
              <button
                className="subtle"
                onClick={() => doDownload(true)}
                disabled={!enabled || busy !== null || inProgress}
                aria-busy={busy === 'redownload'}
                title="Delete the installed files and fetch the model again"
              >
                <RotateCcw aria-hidden size={14} /> {busy === 'redownload' ? 'Repairing…' : 'Re-download'}
              </button>
              <button className="ghost" onClick={doDelete} disabled={!enabled || busy !== null} aria-busy={busy === 'delete'}>
                <Trash2 aria-hidden size={14} /> {busy === 'delete' ? 'Deleting…' : 'Delete'}
              </button>
            </>
          )}
          {model.progress.state === 'Failed' && (
            <button
              className="subtle"
              onClick={() => doDownload(true)}
              disabled={!enabled || busy !== null || inProgress}
            >
              <RotateCcw aria-hidden size={14} /> Retry
            </button>
          )}
        </div>
      )}

      {msg && (
        <p className="rp-model-msg" data-tone={msg.tone} role="status" aria-live="polite">
          {msg.text}
        </p>
      )}

      {testOpen && (
        <TestResultModal
          result={test}
          model={model}
          loading={busy === 'test'}
          onClose={() => setTestOpen(false)}
          onCopy={copyDiagnostics}
        />
      )}
    </div>
  );
}

/** What each failed stage actually means, in the user's terms. */
const STAGE_HINT: Record<string, string> = {
  model: 'The model file is missing — download it from this screen.',
  runtime: 'The llama.cpp runtime is missing. Re-download the model to fetch it again.',
  adapter: 'This build has no llama.cpp adapter registered — report it to IT.',
  server: 'The local server could not start or is still loading the model.',
  completion: 'The server is running but the request failed.',
};

function TestResultModal({
  result,
  model,
  loading,
  onClose,
  onCopy,
}: {
  result: ModelTestResult | null;
  model: LocalModel;
  loading: boolean;
  onClose: () => void;
  onCopy: () => void;
}) {
  const orchestrator = isOrchestrator(model);
  return (
    <div className="rp-modal-backdrop" onClick={onClose}>
      <div className="rp-panel rp-modal" onClick={(e) => e.stopPropagation()}>
        <div className="rp-panel-title">
          <Server aria-hidden size={15} /> {model.displayName} — self test
        </div>

        {loading || !result ? (
          <p className="rp-faint">
            {orchestrator
              ? 'Starting the local server and running a prompt through the model. The first run after '
                + 'a restart loads several gigabytes and can take a couple of minutes.'
              : 'Running a sample through the engine…'}
          </p>
        ) : (
          <>
            <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 8 }}>
              <span className={`badge ${result.ok ? 'ok' : 'danger'}`}>{result.ok ? 'Working' : 'Failed'}</span>
              <span className="badge">{result.engine}</span>
              <span className="badge">{result.latencyMs} ms</span>
              {!orchestrator && (
                <span className="badge">
                  {result.sampleSource === 'model_sample' ? 'sample clip' : 'synthetic tone'}
                </span>
              )}
              {result.endpoint && <span className="badge">{result.endpoint}</span>}
            </div>

            {result.ok ? (
              result.transcript ? (
                <div>
                  <div className="rp-faint" style={{ fontSize: 12, marginBottom: 4 }}>
                    {orchestrator ? 'Model reply' : 'Transcript'}
                  </div>
                  <div className="ai-mark" style={{ whiteSpace: 'pre-wrap' }}>
                    {result.transcript}
                  </div>
                </div>
              ) : (
                <p className="rp-faint">
                  Engine ran successfully — no transcript text for the synthetic tone, which is expected.
                </p>
              )
            ) : (
              <div>
                <div className="banner danger" style={{ whiteSpace: 'pre-wrap' }}>
                  {result.error}
                </div>
                {result.stage && STAGE_HINT[result.stage] && (
                  <p className="rp-model-note" style={{ marginTop: 8 }}>
                    {STAGE_HINT[result.stage]}
                  </p>
                )}
                {result.detail && (
                  <details style={{ marginTop: 8 }}>
                    <summary className="rp-faint" style={{ cursor: 'pointer' }}>
                      Technical detail (for IT)
                    </summary>
                    <pre style={{ whiteSpace: 'pre-wrap', fontSize: 11, overflowX: 'auto' }}>{result.detail}</pre>
                  </details>
                )}
              </div>
            )}
          </>
        )}

        <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
          <button className="subtle" onClick={onCopy}>
            Copy diagnostics
          </button>
          <button className="primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
