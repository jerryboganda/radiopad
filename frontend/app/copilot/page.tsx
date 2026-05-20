'use client';

import { useEffect, useState } from 'react';
import {
  api,
  type CopilotAccount,
  type CopilotContextPreview,
  type CopilotEntitlement,
  type CopilotSession,
  type CopilotStatus,
} from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import ErrorState from '@/components/ui/ErrorState';
import Skeleton from '@/components/ui/Skeleton';

type TauriInvoke = (cmd: string, args?: Record<string, unknown>) => Promise<unknown>;
type CliStatus = {
  available?: boolean;
  authenticated?: boolean;
  copilotAvailable?: boolean;
  login?: string | null;
  host?: string | null;
  warnings?: string[];
};

function tauriInvoke(): TauriInvoke | null {
  if (typeof window === 'undefined') return null;
  const tauri = (window as typeof window & {
    __TAURI__?: { core?: { invoke?: TauriInvoke }; invoke?: TauriInvoke };
  }).__TAURI__;
  return tauri?.core?.invoke ?? tauri?.invoke ?? null;
}

function errorText(e: unknown): string {
  const body = (e as Error & { body?: { kind?: string; message?: string } }).body;
  return body?.message ? `${body.kind}: ${body.message}` : (e as Error).message;
}

export default function CopilotPage() {
  const [status, setStatus] = useState<CopilotStatus | null>(null);
  const [account, setAccount] = useState<CopilotAccount | null>(null);
  const [entitlement, setEntitlement] = useState<CopilotEntitlement | null>(null);
  const [cliStatus, setCliStatus] = useState<CliStatus | null>(null);
  const [message, setMessage] = useState('');
  const [preview, setPreview] = useState<CopilotContextPreview | null>(null);
  const [session, setSession] = useState<CopilotSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    try {
      const [s, a, e] = await Promise.all([
        api.copilot.status(),
        api.copilot.account(),
        api.copilot.entitlement(),
      ]);
      setStatus(s);
      setAccount(a);
      setEntitlement(e);
      setError(null);
      const invoke = tauriInvoke();
      if (invoke) {
        try {
          setCliStatus((await invoke('copilot_cli_status', { host: s.gitHubHost })) as CliStatus);
        } catch {
          setCliStatus({ available: false, authenticated: false, copilotAvailable: false, warnings: ['Tauri CLI bridge unavailable'] });
        }
      }
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh().catch((e: Error) => setError(e.message));
  }, []);

  async function beginLocalCliAuth() {
    setBusy(true); setError(null); setInfo(null);
    try {
      const auth = await api.copilot.beginAuth({ mode: 'LocalCli' });
      const invoke = tauriInvoke();
      if (!invoke) {
        setInfo(auth.message);
        return;
      }
      await invoke('copilot_cli_login_begin', { host: status?.gitHubHost ?? 'github.com' });
      const next = (await invoke('copilot_cli_status', { host: status?.gitHubHost ?? 'github.com' })) as CliStatus;
      setCliStatus(next);
      const linked = await api.copilot.linkLocalCli({
        gitHubLogin: next.login ?? '',
        host: next.host ?? status?.gitHubHost ?? 'github.com',
        ssoStatus: 'local_cli',
        seatStatus: next.copilotAvailable ? 'cli_enforced' : 'unknown',
      });
      setAccount(linked);
      setEntitlement(await api.copilot.entitlement());
      setInfo('Local GitHub CLI account linked without exposing tokens to RadioPad.');
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function revoke() {
    setBusy(true); setError(null); setInfo(null);
    try {
      await api.copilot.revokeAccount();
      await refresh();
      setInfo('Copilot account link revoked in RadioPad. Use gh auth logout if you also want to clear the GitHub CLI keychain.');
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function previewContext() {
    setBusy(true); setError(null); setInfo(null);
    try {
      setPreview(await api.copilot.previewContext({ message, contextKind: 'manual', items: [] }));
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function startSession() {
    setBusy(true); setError(null); setInfo(null); setSession(null);
    try {
      const result = await api.copilot.startSession({
        message,
        mode: 'LocalCli',
        contextKind: 'chat',
        context: [],
      });
      setSession(result);
      setPreview(result.context);
      setInfo(result.status === 'completed' ? 'Copilot CLI session completed.' : result.message);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  if (error && !status) {
    return (
      <Container>
        <PageHeader
          title="GitHub Copilot"
          description="Copilot is brokered through RadioPad policy gates. Tokens never enter browser state, localStorage, or IPC payloads."
        />
        <ErrorState title="Couldn't load Copilot" message={error} onRetry={() => { void refresh(); }} />
      </Container>
    );
  }

  if (loading && !status) {
    return (
      <Container>
        <PageHeader
          title="GitHub Copilot"
          description="Copilot is brokered through RadioPad policy gates. Tokens never enter browser state, localStorage, or IPC payloads."
        />
        <div className="rp-grid-2">
          <div className="rp-panel"><Skeleton variant="block" height={140} /></div>
          <div className="rp-panel"><Skeleton variant="block" height={140} /></div>
        </div>
      </Container>
    );
  }

  return (
    <Container>
      <PageHeader
        title="GitHub Copilot"
        description="Copilot is brokered through RadioPad policy gates. Tokens never enter browser state, localStorage, or IPC payloads."
      />

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner info">{info}</div>}
      {status && status.runtimeStatus !== 'Ready' && (
        <div className="banner warn">
          Runtime not ready: enable the tenant LocalCli mode and sign in with the official GitHub CLI/Copilot extension.
        </div>
      )}

      <div className="rp-grid-2">
        <div className="rp-panel">
          <div className="rp-panel-title">Tenant policy</div>
          <p><span className={`badge ${status?.enabled ? 'ok' : 'warn'}`}>{status?.enabled ? 'Enabled' : 'Disabled'}</span></p>
          <p><span className={`badge ${status?.runtimeStatus === 'Ready' ? 'ok' : 'warn'}`}>{status?.runtimeStatus ?? 'Loading'}</span></p>
          <p className="rp-page-sub">{status?.message ?? 'Loading status...'}</p>
          <ul className="rp-list">
            <li>Mode: <code>{status?.defaultMode ?? '-'}</code></li>
            <li>Allowed modes: <code>{status?.allowedModes?.join(', ') || '-'}</code></li>
            <li>PHI routing: <span className="badge danger">{status?.phiBlocked ? 'blocked' : 'unknown'}</span></li>
            <li>Prompt logging: <code>{status?.promptLoggingEnabled ? 'on' : 'off'}</code></li>
          </ul>
        </div>

        <div className="rp-panel">
          <div className="rp-panel-title">GitHub account</div>
          <ul className="rp-list">
            <li>Mode: <code>{account?.mode ?? '-'}</code></li>
            <li>Login: <code>{account?.gitHubLogin || cliStatus?.login || 'not connected'}</code></li>
            <li>Token: <code>{account?.tokenStatus ?? 'none'}</code></li>
            <li>SSO: <code>{account?.ssoStatus ?? 'unknown'}</code></li>
            <li>Seat: <code>{account?.seatStatus ?? 'unknown'}</code></li>
            <li>Gate: <code>{entitlement?.allowed ? 'allowed' : entitlement?.denialReason ?? account?.denialReason ?? status?.kind ?? 'runtime_not_configured'}</code></li>
          </ul>
          <div className="rp-row rp-row-wrap">
            <button className="primary" onClick={beginLocalCliAuth} disabled={busy}>Link local GitHub CLI</button>
            <button className="ghost" onClick={revoke} disabled={busy}>Revoke link</button>
          </div>
          {cliStatus && (
            <p className="rp-page-sub">
              CLI bridge: <span className={`badge ${cliStatus.available ? 'ok' : 'warn'}`}>{cliStatus.available ? 'available' : 'missing'}</span>{' '}
              Auth: <span className={`badge ${cliStatus.authenticated ? 'ok' : 'warn'}`}>{cliStatus.authenticated ? 'signed in' : 'not signed in'}</span>
            </p>
          )}
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Chat session</div>
        <p className="rp-page-sub">
          RadioPad previews context, blocks PHI/secrets, enforces quotas, then invokes the official local GitHub Copilot CLI with fixed arguments.
        </p>
        <label className="rp-field">
          <span>Message</span>
          <textarea className="rp-input" rows={5} value={message} onChange={(e) => setMessage(e.target.value)} placeholder="Ask a non-PHI coding question..." />
        </label>
        <div className="rp-row rp-row-wrap">
          <button className="ghost" onClick={previewContext} disabled={busy || !message.trim()}>Preview context</button>
          <button className="primary" onClick={startSession} disabled={busy || !message.trim()}>Start Copilot chat</button>
        </div>
        {preview && (
          <div className="section-block">
            <label>Context gate</label>
            <ul className="rp-list">
              <li>Message hash: <code>{preview.messageHash || '-'}</code></li>
              <li>Context hash: <code>{preview.contextHash || '-'}</code></li>
              <li>PHI detected: <span className={`badge ${preview.containsPhi ? 'danger' : 'ok'}`}>{preview.containsPhi ? 'blocked' : 'no'}</span></li>
              <li>Included items: <code>{preview.included.length}</code>; removed items: <code>{preview.removed.length}</code></li>
            </ul>
          </div>
        )}
        {session?.output && (
          <div className="section-block">
            <label>Copilot output</label>
            <div className="ai-mark">{session.output}</div>
          </div>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Unsupported by policy</div>
        <ul className="rp-list">
          {(status?.unsupportedFeatures ?? [
            'IDE token scraping',
            'undocumented Copilot endpoints',
            'shared admin token impersonation',
            'frontend or IPC token exposure',
            'PHI prompt routing',
          ]).map((item) => <li key={item}><span className="badge danger">blocked</span> {item}</li>)}
        </ul>
      </div>
    </Container>
  );
}
