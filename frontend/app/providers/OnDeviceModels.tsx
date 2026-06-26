'use client';

import { useCallback, useEffect, useState } from 'react';
import { api, type LocalModel, type LocalModelKind, type ModelTestResult } from '@/lib/api';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';

const KIND_ORDER: LocalModelKind[] = ['Stt', 'Tts', 'Orchestrator'];
const KIND_LABEL: Record<LocalModelKind, string> = {
  Stt: 'Speech-to-text (dictation)',
  Tts: 'Text-to-speech',
  Orchestrator: 'Orchestrator brain',
};

const ACTIVE_STATES = ['Downloading', 'Verifying', 'Extracting', 'Installing'];

function fmtBytes(n: number): string {
  if (!n) return '—';
  const mb = n / (1024 * 1024);
  return mb >= 1024 ? `${(mb / 1024).toFixed(2)} GB` : `${Math.round(mb)} MB`;
}

function errText(e: unknown): string {
  const err = e as { body?: { error?: string } };
  return err?.body?.error ?? (e as Error)?.message ?? 'Something went wrong.';
}

/**
 * On-device AI model manager — the "On-device models" tab of the AI models page.
 * Lists the local model catalog grouped by kind (STT actionable today; TTS +
 * orchestrator are "coming soon" placeholders), with download (live progress),
 * delete, test, and copyable diagnostics. Driven entirely by the backend
 * `enabled` flag, so on the web it renders read-only with a desktop-only notice.
 */
export default function OnDeviceModels() {
  const [enabled, setEnabled] = useState(true);
  const [models, setModels] = useState<LocalModel[]>([]);
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

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Poll while any model is mid-download so the progress bars advance.
  useEffect(() => {
    const active = models.some((m) => ACTIVE_STATES.includes(m.progress.state));
    if (!active) return;
    const t = setInterval(() => {
      void refresh();
    }, 1200);
    return () => clearInterval(t);
  }, [models, refresh]);

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

  const grouped = KIND_ORDER.map((kind) => ({
    kind,
    items: models.filter((m) => m.kind === kind),
  })).filter((g) => g.items.length > 0);

  return (
    <div>
      {!enabled && (
        <div className="banner info">
          On-device models are downloaded and run by the RadioPad desktop app. Open RadioPad on your
          workstation to download, test, or manage them — on the web they appear here for reference only.
        </div>
      )}
      {error && models.length > 0 && <div className="banner warn">{error}</div>}

      {grouped.map((g) => (
        <div key={g.kind} className="rp-panel" style={{ marginBottom: 16 }}>
          <div className="rp-panel-title">{KIND_LABEL[g.kind]}</div>
          <div className="rp-grid-2">
            {g.items.map((m) => (
              <ModelCard key={m.id} model={m} enabled={enabled} onChanged={refresh} />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

function StateBadge({ model }: { model: LocalModel }) {
  if (model.placeholder) return <span className="badge">Coming soon</span>;
  const s = model.progress.state;
  if (ACTIVE_STATES.includes(s)) return <span className="badge ai">{s}…</span>;
  if (s === 'Failed') return <span className="badge danger">Failed</span>;
  if (model.downloaded) return <span className="badge ok">Ready</span>;
  return <span className="badge warn">Not downloaded</span>;
}

function ModelCard({
  model,
  enabled,
  onChanged,
}: {
  model: LocalModel;
  enabled: boolean;
  onChanged: () => Promise<void> | void;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [test, setTest] = useState<ModelTestResult | null>(null);
  const [testOpen, setTestOpen] = useState(false);

  const inProgress = ACTIVE_STATES.includes(model.progress.state);
  const pct =
    model.progress.totalBytes > 0
      ? Math.min(100, Math.round((model.progress.bytesDownloaded / model.progress.totalBytes) * 100))
      : null;

  async function doDownload() {
    setBusy('download');
    setMsg(null);
    try {
      await api.localModels.download(model.id);
      await onChanged();
    } catch (e) {
      setMsg(errText(e));
    } finally {
      setBusy(null);
    }
  }

  async function doDelete() {
    if (!window.confirm(`Delete ${model.displayName}? You can re-download it anytime.`)) return;
    setBusy('delete');
    setMsg(null);
    try {
      await api.localModels.remove(model.id);
      await onChanged();
    } catch (e) {
      setMsg(errText(e));
    } finally {
      setBusy(null);
    }
  }

  async function doTest() {
    setBusy('test');
    setMsg(null);
    setTest(null);
    setTestOpen(true);
    try {
      setTest(await api.localModels.test(model.id));
    } catch (e) {
      setMsg(errText(e));
      setTestOpen(false);
    } finally {
      setBusy(null);
    }
  }

  async function copyDiagnostics() {
    try {
      const diagnostics = await api.localModels.diagnostics(model.id);
      await navigator.clipboard.writeText(JSON.stringify({ test, diagnostics }, null, 2));
      setMsg('Diagnostics copied to clipboard.');
    } catch (e) {
      setMsg(errText(e));
    }
  }

  return (
    <div className="rp-panel">
      <div className="rp-row" style={{ justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
        <div>
          <div style={{ fontWeight: 600 }}>{model.displayName}</div>
          <div className="rp-faint" style={{ fontSize: 12 }}>
            <code>{model.id}</code>
          </div>
        </div>
        <StateBadge model={model} />
      </div>

      <div className="rp-row" style={{ gap: 6, marginTop: 8, flexWrap: 'wrap' }}>
        {!model.placeholder && <span className="badge">{fmtBytes(model.sizeBytes)}</span>}
        {model.license && <span className="badge">{model.license}</span>}
        {model.available && <span className="badge ok">Loaded</span>}
      </div>

      {inProgress && (
        <div style={{ marginTop: 10 }}>
          <div className="rp-faint" style={{ fontSize: 12, marginBottom: 4 }}>
            {model.progress.state}
            {pct !== null ? ` — ${pct}%` : '…'}
          </div>
          <div style={{ height: 6, background: 'var(--border)', borderRadius: 4, overflow: 'hidden' }}>
            <div
              style={{
                height: '100%',
                width: `${pct ?? 30}%`,
                background: 'var(--accent)',
                transition: 'width .3s ease',
              }}
            />
          </div>
        </div>
      )}

      {model.progress.state === 'Failed' && model.progress.error && (
        <div className="banner danger" style={{ marginTop: 10, fontSize: 12 }}>
          {model.progress.error}
        </div>
      )}

      {model.placeholder ? (
        <p className="rp-faint" style={{ marginTop: 10, marginBottom: 0, fontSize: 13 }}>
          Coming in a future release — it will be downloadable and testable here, just like the
          speech-to-text models.
        </p>
      ) : (
        <div className="rp-row" style={{ gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
          {!model.downloaded ? (
            <button className="primary" onClick={doDownload} disabled={!enabled || busy !== null || inProgress}>
              {busy === 'download' || inProgress ? 'Downloading…' : 'Download'}
            </button>
          ) : (
            <>
              <button className="subtle" onClick={doTest} disabled={!enabled || busy !== null}>
                {busy === 'test' ? 'Testing…' : 'Test'}
              </button>
              <button className="ghost" onClick={doDelete} disabled={!enabled || busy !== null}>
                {busy === 'delete' ? 'Deleting…' : 'Delete'}
              </button>
            </>
          )}
          {model.progress.state === 'Failed' && (
            <button className="subtle" onClick={doDownload} disabled={!enabled || busy !== null || inProgress}>
              Retry
            </button>
          )}
        </div>
      )}

      {msg && (
        <div className="rp-faint" style={{ marginTop: 8, fontSize: 12 }}>
          {msg}
        </div>
      )}

      {testOpen && (
        <TestResultModal
          result={test}
          loading={busy === 'test'}
          onClose={() => setTestOpen(false)}
          onCopy={copyDiagnostics}
        />
      )}
    </div>
  );
}

function TestResultModal({
  result,
  loading,
  onClose,
  onCopy,
}: {
  result: ModelTestResult | null;
  loading: boolean;
  onClose: () => void;
  onCopy: () => void;
}) {
  return (
    <div className="rp-modal-backdrop" onClick={onClose}>
      <div className="rp-panel rp-modal" onClick={(e) => e.stopPropagation()}>
        <div className="rp-panel-title">Model test</div>

        {loading || !result ? (
          <p className="rp-faint">Running a sample through the engine…</p>
        ) : (
          <>
            <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap', marginBottom: 8 }}>
              <span className={`badge ${result.ok ? 'ok' : 'danger'}`}>{result.ok ? 'Working' : 'Failed'}</span>
              <span className="badge">{result.engine}</span>
              <span className="badge">{result.latencyMs} ms</span>
              <span className="badge">
                {result.sampleSource === 'model_sample' ? 'sample clip' : 'synthetic tone'}
              </span>
            </div>

            {result.ok ? (
              result.transcript ? (
                <div>
                  <div className="rp-faint" style={{ fontSize: 12, marginBottom: 4 }}>
                    Transcript
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
