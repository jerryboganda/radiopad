'use client';

import { useEffect, useState } from 'react';
import { api, type CopilotQuotaPolicy, type CopilotSettings, type CopilotStatus, type CopilotUsageSummary } from '@/lib/api';

const EMPTY_SETTINGS: CopilotSettings = {
  enabled: false,
  emergencyDisabled: true,
  defaultMode: 'Disabled',
  allowedModes: ['Disabled'],
  gitHubEnterpriseSlug: '',
  gitHubOrganization: '',
  gitHubHost: 'github.com',
  sdkRuntimeEnabled: false,
  cliRuntimeEnabled: false,
  allowByoAccounts: false,
  allowEnvironmentTokenAuth: false,
  requireOsKeychainForCli: true,
  promptLoggingEnabled: false,
  contextLoggingEnabled: false,
  retentionPolicy: 'metadata_only',
  policyJson: '{"phi":"blocked","promptLogging":"off","contentStorage":"metadata_only"}',
  gitHubAppId: '',
  gitHubAppInstallationId: '',
  oAuthClientId: '',
  gitHubAppPrivateKeyConfigured: false,
  oAuthClientSecretConfigured: false,
  gitHubAppPrivateKeySecretRef: '',
  oAuthClientSecretRef: '',
};

const MODES = ['Disabled', 'EnterpriseManaged', 'BringYourOwnAccount', 'LocalCli', 'Byok'];

export default function CopilotAdminPage() {
  const [settings, setSettings] = useState<CopilotSettings>(EMPTY_SETTINGS);
  const [status, setStatus] = useState<CopilotStatus | null>(null);
  const [quotas, setQuotas] = useState<CopilotQuotaPolicy[]>([]);
  const [usage, setUsage] = useState<CopilotUsageSummary | null>(null);
  const [diagnostics, setDiagnostics] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function refresh() {
    const [s, st] = await Promise.all([
      api.copilot.admin.settings(),
      api.copilot.admin.status(),
    ]);
    setSettings({ ...s, gitHubAppPrivateKeySecretRef: '', oAuthClientSecretRef: '' });
    setStatus(st);
    const [q, u] = await Promise.all([
      api.copilot.admin.quotas(),
      api.copilot.admin.usage(),
    ]);
    setQuotas(q);
    setUsage(u);
  }

  useEffect(() => {
    refresh().catch((e: Error) => setError(e.message));
  }, []);

  function setAllowed(mode: string, enabled: boolean) {
    const next = new Set(settings.allowedModes);
    if (enabled) next.add(mode);
    else next.delete(mode);
    if (next.size === 0) next.add('Disabled');
    setSettings({ ...settings, allowedModes: Array.from(next) });
  }

  async function save() {
    setBusy(true); setError(null); setInfo(null);
    try {
      const saved = await api.copilot.admin.saveSettings(settings);
      setSettings({ ...saved, gitHubAppPrivateKeySecretRef: '', oAuthClientSecretRef: '' });
      setStatus(await api.copilot.admin.status());
      setInfo('Copilot settings saved. Runtime remains fail-closed until an official transport is configured.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function runDiagnostics() {
    setBusy(true); setError(null); setInfo(null);
    try {
      const r = await api.copilot.admin.diagnostics();
      setDiagnostics(r.results);
      setStatus(r.status);
      setInfo('Diagnostics completed without returning secrets.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function saveQuotas() {
    setBusy(true); setError(null); setInfo(null);
    try {
      setQuotas(await api.copilot.admin.saveQuotas(quotas));
      setUsage(await api.copilot.admin.usage());
      setInfo('Copilot quotas saved and enforced immediately.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">GitHub Copilot administration</h1>
          <p className="rp-page-sub">
            Control whether and how your workspace uses GitHub Copilot inside RadioPad. By default, Copilot is off and patient information is never sent to it.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner info">{info}</div>}
      <div className="banner warn">
        Fail-closed: no IDE token scraping, no undocumented endpoints, no shared admin impersonation, no token exposure to the browser or desktop bridge, and PHI routing is blocked.
      </div>

      <div className="rp-page-grid">
        <div className="rp-page-main">

      <div className="rp-grid-2">
        <div className="rp-panel">
          <div className="rp-panel-title">Status</div>
          <p><span className={`badge ${status?.enabled ? 'ok' : 'warn'}`}>{status?.enabled ? 'Turned on by admin' : 'Off'}</span></p>
          <p><span className={`badge ${status?.runtimeStatus === 'Ready' ? 'ok' : 'warn'}`}>{status?.runtimeStatus ?? 'Loading'}</span></p>
          <p className="rp-page-sub">{status?.message ?? 'Loading Copilot status…'}</p>
          <details className="rp-advanced">
            <summary>Show connection details</summary>
            <p className="rp-page-sub">Host: <code>{status?.gitHubHost ?? settings.gitHubHost}</code></p>
            <p className="rp-page-sub">Organisation: <code>{status?.gitHubOrganization || 'not configured'}</code></p>
          </details>
        </div>

        <div className="rp-panel">
          <div className="rp-panel-title">Run a check</div>
          <p className="rp-page-sub">
            Runs a safe local check of your Copilot setup. No secrets or patient data are sent or shown.
          </p>
          <button className="primary" onClick={runDiagnostics} disabled={busy}>Run check</button>
          {diagnostics ? <details className="rp-advanced"><summary>Show technical output</summary><pre className="rp-faint">{JSON.stringify(diagnostics, null, 2)}</pre></details> : null}
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">How your team uses Copilot</div>
        <div className="rp-grid-2">
          <label className="rp-field rp-row"><input type="checkbox" checked={settings.enabled} onChange={(e) => setSettings({ ...settings, enabled: e.target.checked, emergencyDisabled: !e.target.checked || settings.emergencyDisabled })} /> <span>Turn Copilot on for this workspace</span></label>
          <label className="rp-field rp-row"><input type="checkbox" checked={settings.emergencyDisabled} onChange={(e) => setSettings({ ...settings, emergencyDisabled: e.target.checked })} /> <span>Emergency switch — stop all Copilot activity now</span></label>
          <label className="rp-field rp-row"><input type="checkbox" checked={settings.allowByoAccounts} onChange={(e) => setSettings({ ...settings, allowByoAccounts: e.target.checked })} /> <span>Let users sign in with their personal GitHub account</span></label>
        </div>

        <details className="rp-advanced">
          <summary>Advanced — mode selection (IT team only)</summary>
          <div className="rp-grid-2">
            <label className="rp-field">
              <span>Default mode</span>
              <select className="rp-input" value={settings.defaultMode} onChange={(e) => setSettings({ ...settings, defaultMode: e.target.value })}>
                {MODES.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </label>
            <label className="rp-field">
              <span>GitHub host</span>
              <input className="rp-input" value={settings.gitHubHost} onChange={(e) => setSettings({ ...settings, gitHubHost: e.target.value })} />
            </label>
            <label className="rp-field rp-row"><input type="checkbox" checked={settings.sdkRuntimeEnabled} onChange={(e) => setSettings({ ...settings, sdkRuntimeEnabled: e.target.checked })} /> <span>SDK runtime configured server-side</span></label>
            <label className="rp-field rp-row"><input type="checkbox" checked={settings.cliRuntimeEnabled} onChange={(e) => setSettings({ ...settings, cliRuntimeEnabled: e.target.checked })} /> <span>Token-free CLI bridge permitted</span></label>
            <label className="rp-field rp-row"><input type="checkbox" checked={settings.requireOsKeychainForCli} onChange={(e) => setSettings({ ...settings, requireOsKeychainForCli: e.target.checked })} /> <span>Require OS keychain for CLI credentials</span></label>
          </div>

          <div className="section-block">
            <label>Allowed modes</label>
            <div className="rp-row rp-row-wrap">
              {MODES.map((m) => (
                <label key={m} className="badge">
                  <input type="checkbox" checked={settings.allowedModes.includes(m)} onChange={(e) => setAllowed(m, e.target.checked)} />
                  {m}
                </label>
              ))}
            </div>
          </div>
        </details>
      </div>

      <details className="rp-panel rp-advanced">
        <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>GitHub App / OAuth references (IT team only)</summary>
        <p className="rp-page-sub">Secret fields are write-only references. Existing refs show as configured/missing only.</p>
        <div className="rp-grid-2">
           <label className="rp-field"><span>GitHub Enterprise slug</span><input className="rp-input" value={settings.gitHubEnterpriseSlug} onChange={(e) => setSettings({ ...settings, gitHubEnterpriseSlug: e.target.value })} /></label>
           <label className="rp-field"><span>GitHub organisation</span><input className="rp-input" value={settings.gitHubOrganization} onChange={(e) => setSettings({ ...settings, gitHubOrganization: e.target.value })} /></label>
           <label className="rp-field"><span>GitHub App ID</span><input className="rp-input" value={settings.gitHubAppId} onChange={(e) => setSettings({ ...settings, gitHubAppId: e.target.value })} /></label>
           <label className="rp-field"><span>Installation ID</span><input className="rp-input" value={settings.gitHubAppInstallationId} onChange={(e) => setSettings({ ...settings, gitHubAppInstallationId: e.target.value })} /></label>
           <label className="rp-field"><span>OAuth client ID</span><input className="rp-input" value={settings.oAuthClientId} onChange={(e) => setSettings({ ...settings, oAuthClientId: e.target.value })} /></label>
           <label className="rp-field"><span>Private key secret ref ({settings.gitHubAppPrivateKeyConfigured ? 'configured' : 'missing'})</span><input className="rp-input" placeholder="vault:copilot/github-app-key" value={settings.gitHubAppPrivateKeySecretRef ?? ''} onChange={(e) => setSettings({ ...settings, gitHubAppPrivateKeySecretRef: e.target.value })} /></label>
           <label className="rp-field"><span>OAuth secret ref ({settings.oAuthClientSecretConfigured ? 'configured' : 'missing'})</span><input className="rp-input" placeholder="vault:copilot/oauth-client-secret" value={settings.oAuthClientSecretRef ?? ''} onChange={(e) => setSettings({ ...settings, oAuthClientSecretRef: e.target.value })} /></label>
        </div>
      </details>

      <div className="rp-panel">
        <div className="rp-panel-title">Safety policy</div>
        <p className="rp-page-sub">Prompt logging and content storage are off; only metadata is kept.</p>
        <details className="rp-advanced">
          <summary>Edit policy JSON (IT team only)</summary>
          <label className="rp-field"><span>Policy JSON</span><textarea className="rp-input" rows={5} value={settings.policyJson} onChange={(e) => setSettings({ ...settings, policyJson: e.target.value })} /></label>
        </details>
        <button className="primary" onClick={save} disabled={busy}>Save Copilot settings</button>
      </div>

      <div className="rp-grid-2">
        <div className="rp-panel">
          <div className="rp-panel-title">Usage limits</div>
          <p className="rp-page-sub">Limits are enforced before any Copilot run starts. Leave the scope key blank to apply to the whole workspace.</p>
          <details className="rp-advanced" open>
            <summary>Show limits (IT team only)</summary>
          {quotas.map((q, i) => (
            <div className="section-block" key={`${q.scopeType}-${q.scopeKey}-${q.feature}-${i}`}>
              <div className="rp-grid-2">
                <label className="rp-field"><span>Scope</span><input className="rp-input" value={q.scopeType} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, scopeType: e.target.value } : row))} /></label>
                <label className="rp-field"><span>Scope key</span><input className="rp-input" value={q.scopeKey} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, scopeKey: e.target.value } : row))} /></label>
                <label className="rp-field"><span>Feature</span><input className="rp-input" value={q.feature} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, feature: e.target.value } : row))} /></label>
                <label className="rp-field"><span>Window seconds</span><input className="rp-input" type="number" value={q.windowSeconds} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, windowSeconds: Number(e.target.value) } : row))} /></label>
                <label className="rp-field"><span>Max requests</span><input className="rp-input" type="number" value={q.maxRequests} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, maxRequests: Number(e.target.value) } : row))} /></label>
                <label className="rp-field"><span>Max concurrent</span><input className="rp-input" type="number" value={q.maxConcurrent} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, maxConcurrent: Number(e.target.value) } : row))} /></label>
              </div>
              <label className="rp-field rp-row"><input type="checkbox" checked={q.enabled} onChange={(e) => setQuotas(quotas.map((row, idx) => idx === i ? { ...row, enabled: e.target.checked } : row))} /> <span>Enabled</span></label>
            </div>
          ))}
          <div className="rp-row rp-row-wrap">
            <button className="ghost" onClick={() => setQuotas([...quotas, { scopeType: 'user', scopeKey: '', feature: 'chat', windowSeconds: 3600, maxRequests: 20, maxConcurrent: 1, enabled: true }])}>Add limit</button>
            <button className="primary" onClick={saveQuotas} disabled={busy}>Save quotas</button>
          </div>
          </details>
        </div>

        <div className="rp-panel">
          <div className="rp-panel-title">Activity summary</div>
          <ul className="rp-list">
            <li>Total runs: <strong>{usage?.total ?? 0}</strong></li>
            <li>Completed: <strong>{usage?.completed ?? 0}</strong></li>
            <li>Blocked: <strong>{usage?.blocked ?? 0}</strong></li>
            <li>Failed: <strong>{usage?.failed ?? 0}</strong></li>
            <li>Cancelled: <strong>{usage?.cancelled ?? 0}</strong></li>
            <li>Running now: <strong>{usage?.running ?? 0}</strong></li>
          </ul>
          <p className="rp-page-sub">Prompts and outputs are not stored — only run counts, durations, and status.</p>
        </div>
      </div>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What is this?</div>
            <p>Controls whether your workspace uses GitHub Copilot. RadioPad keeps Copilot off until your IT team explicitly turns it on.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Is it safe for patient data?</div>
            <p>Yes. Patient information (PHI) is never routed to Copilot. The system blocks it automatically.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
