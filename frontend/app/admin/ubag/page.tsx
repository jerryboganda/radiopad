'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useEffect, useMemo, useState } from 'react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import { TableSkeleton } from '@/components/ui/Skeleton';
import { api, type UbagJob, type UbagStatus, type UbagWorkflowRun } from '@/lib/api';

const DEFAULT_TARGETS = ['chatgpt_web', 'gemini_web', 'deepseek_web', 'mock'];

function statusBadge(ok: boolean): string {
  return ok ? 'ok' : 'warn';
}

function isBlockedPrompt(prompt: string): string | null {
  const value = prompt.toLowerCase();
  if (value.includes('authorization:') || value.includes('api_key') || value.includes('client_secret') || value.includes('-----begin')) {
    return 'Remove secrets before sending work to UBAG.';
  }
  if (value.includes('patient name') || value.includes('mrn') || value.includes('date of birth') || /\b\d{2}\/\d{2}\/\d{4}\b/.test(value)) {
    return 'De-identify the prompt before sending work to UBAG.';
  }
  return null;
}

export default function UbagHubPage() {
  return (
    <PermissionGate permission="mcp_tools.invoke" title="UBAG Hub">
      <UbagHubPageInner />
    </PermissionGate>
  );
}

function UbagHubPageInner() {
  const [status, setStatus] = useState<UbagStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [target, setTarget] = useState('gemini_web');
  const [prompt, setPrompt] = useState('');
  const [busy, setBusy] = useState(false);
  const [job, setJob] = useState<UbagJob | null>(null);
  const [run, setRun] = useState<UbagWorkflowRun | null>(null);

  const allowedTargets = useMemo(
    () => status?.allowedTargets?.length ? status.allowedTargets : DEFAULT_TARGETS,
    [status],
  );
  const promptBlock = isBlockedPrompt(prompt);

  async function refresh() {
    setLoading(true);
    try {
      const next = await api.ubag.status();
      setStatus(next);
      setError(null);
      if (!next.allowedTargets.includes(target) && next.allowedTargets.length > 0) {
        setTarget(next.allowedTargets[0]);
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function submitJob() {
    setBusy(true);
    setInfo(null);
    setError(null);
    setJob(null);
    setRun(null);
    try {
      const out = await api.ubag.submitJob({ target, prompt });
      setJob(out);
      setInfo('UBAG job submitted.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function runOrderedWorkflow() {
    setBusy(true);
    setInfo(null);
    setError(null);
    setJob(null);
    setRun(null);
    try {
      const out = await api.ubag.runOrderedWorkflow({ prompt });
      setRun(out.run);
      setInfo(`Ordered workflow started: ${out.orderedTargets.join(' -> ')}`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function pollJob() {
    if (!job?.id) return;
    setBusy(true);
    try {
      setJob(await api.ubag.getJob(job.id));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function pollRun() {
    if (!run?.id) return;
    setBusy(true);
    try {
      setRun(await api.ubag.getWorkflowRun(run.id));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Container>
      <PageHeader
        title="UBAG Hub"
        description="Run governed non-PHI browser AI automation through RadioPad's backend."
        primaryAction={
          <button className="subtle" onClick={() => void refresh()} disabled={loading} aria-busy={loading}>
            {loading && <span className="rp-spinner sm" aria-hidden />}
            Refresh
          </button>
        }
      />

      {error && !loading && <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>}
      {info && <Banner tone="success" onDismiss={() => setInfo(null)}>{info}</Banner>}

      {loading && !status ? (
        <TableSkeleton rows={4} cols={3} />
      ) : error && !status ? (
        <ErrorState title="Couldn't load UBAG" message={error} onRetry={() => { void refresh(); }} />
      ) : status ? (
        <>
          <div className="rp-grid-3 rp-stagger">
            <div className="rp-panel">
              <div className="rp-panel-title">Gateway</div>
              <span className={`badge ${statusBadge(status.health.ok)}`}>
                {status.health.ok ? 'Healthy' : 'Unavailable'}
              </span>
              <p className="rp-page-sub">{status.health.status}{status.health.version ? ` · ${status.health.version}` : ''}</p>
            </div>
            <div className="rp-panel">
              <div className="rp-panel-title">Browser topology</div>
              <div className="rp-row" style={{ gap: 8, flexWrap: 'wrap' }}>
                <span className="badge">{status.browser.instances} instances</span>
                <span className="badge">{status.browser.contexts} contexts</span>
                <span className="badge">{status.browser.tabs} tabs</span>
              </div>
            </div>
            <div className="rp-panel">
              <div className="rp-panel-title">Ordered chain</div>
              <p className="rp-page-sub">{status.orderedTargets.join(' -> ')}</p>
            </div>
          </div>

          <div className="rp-page-grid" style={{ marginTop: 16 }}>
            <div className="rp-page-main">
              <div className="rp-panel">
                <div className="rp-panel-title">Non-PHI job</div>
                <label className="rp-field">
                  <span>Target</span>
                  <select className="rp-input" value={target} onChange={(e) => setTarget(e.target.value)}>
                    {allowedTargets.map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </select>
                </label>
                <label className="rp-field">
                  <span>Prompt</span>
                  <textarea
                    className="rp-input"
                    rows={8}
                    value={prompt}
                    onChange={(e) => setPrompt(e.target.value)}
                    placeholder="Use de-identified project, research, or wording prompts only."
                  />
                </label>
                {promptBlock && <Banner tone="warn">{promptBlock}</Banner>}
                <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8 }}>
                  <button className="primary-ghost" onClick={() => void runOrderedWorkflow()} disabled={busy || !prompt.trim() || !!promptBlock} aria-busy={busy}>
                    {busy && <span className="rp-spinner sm" aria-hidden />}
                    {busy ? 'Running...' : 'Run ChatGPT -> Gemini -> DeepSeek'}
                  </button>
                  <button className="primary" onClick={() => void submitJob()} disabled={busy || !prompt.trim() || !!promptBlock} aria-busy={busy}>
                    {busy && <span className="rp-spinner sm" aria-hidden />}
                    {busy ? 'Submitting...' : 'Submit job'}
                  </button>
                </div>
              </div>

              {(job || run) && (
                <div className="rp-panel rp-anim-fade-in-up" style={{ marginTop: 16 }} aria-live="polite" aria-busy={busy}>
                  <div className="rp-panel-title">Latest result</div>
                  {job && (
                    <>
                      <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
                        <span className="badge"><code>{job.id || 'pending'}</code></span>
                        <span className="badge">{job.target}</span>
                        <span className={`badge ${job.terminal ? 'ok' : 'info'}`}>{job.status}</span>
                        {job.manualAction && <span className="badge warn">manual action</span>}
                      </div>
                      <div className="rp-row" style={{ justifyContent: 'flex-end', marginTop: 8 }}>
                        <button className="subtle" onClick={() => void pollJob()} disabled={busy || !job.id} aria-busy={busy}>
                          {busy && <span className="rp-spinner sm" aria-hidden />}
                          Refresh job
                        </button>
                      </div>
                      {job.output && <div className="ai-mark" style={{ whiteSpace: 'pre-wrap', marginTop: 12 }}>{job.output}</div>}
                      {job.error && <Banner tone="warn">{job.error}</Banner>}
                    </>
                  )}
                  {run && (
                    <>
                      <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
                        <span className="badge"><code>{run.id || 'pending'}</code></span>
                        <span className={`badge ${run.terminal ? 'ok' : 'info'}`}>{run.status}</span>
                        {run.manualAction && <span className="badge warn">manual action</span>}
                      </div>
                      <div className="rp-row" style={{ justifyContent: 'flex-end', marginTop: 8 }}>
                        <button className="subtle" onClick={() => void pollRun()} disabled={busy || !run.id} aria-busy={busy}>
                          {busy && <span className="rp-spinner sm" aria-hidden />}
                          Refresh workflow
                        </button>
                      </div>
                      {run.output && <div className="ai-mark" style={{ whiteSpace: 'pre-wrap', marginTop: 12 }}>{run.output}</div>}
                      {run.error && <Banner tone="warn">{run.error}</Banner>}
                    </>
                  )}
                </div>
              )}
            </div>

            <aside className="rp-page-side">
              <div className="rp-panel">
                <div className="rp-panel-title">Targets</div>
                {status.targets.length === 0 ? (
                  <EmptyState title="No targets reported" description="UBAG did not return target metadata." />
                ) : (
                  <ul className="rp-list">
                    {status.targets.map((t) => (
                      <li key={t.id} className="rp-list-row">
                        <div>
                          <strong>{t.name}</strong>
                          <div className="rp-page-sub"><code>{t.id}</code></div>
                        </div>
                        <span className={`badge ${t.ready ? 'ok' : 'warn'}`}>{t.status}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div className="rp-panel" style={{ marginTop: 16 }}>
                <div className="rp-panel-title">Safety</div>
                <p className="rp-page-sub">
                  UBAG jobs from RadioPad are non-PHI only. Logins, CAPTCHA, 2FA, consent, credentials, and cookies stay manual in UBAG Browser Sessions.
                </p>
              </div>
            </aside>
          </div>
        </>
      ) : null}
    </Container>
  );
}
