'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type PacsHealth = {
  dicomWeb: { configured: boolean; reachable: boolean };
  orthanc: { configured: boolean; reachable: boolean; url?: string | null };
};

type PacsPlugin = {
  id: string;
  name: string;
  vendor: string;
  version: string;
  capabilities: string[];
  enabled: boolean;
  verified: boolean;
  error?: string | null;
};

/**
 * Iter-32 DESK-007 / INT-007 — PACS bridge admin surface.
 *
 * Surfaces three layers of PACS connectivity:
 *  1. The tenant's configured DICOMweb base URL (read-only here; edited on
 *     `/admin/settings`).
 *  2. The bundled Orthanc proxy availability (driven by the `RADIOPAD_ORTHANC_URL`
 *     env on the API; reported through `GET /api/pacs/health`).
 *  3. Installed signed vendor plugins (Sectra, AGFA, Visage, Merge, Hyland)
 *     loaded by the desktop shell. The web/admin surface lists what the
 *     desktop-side bridge advertised; verification status is shown in-line.
 *
 * Locked design tokens / classes only.
 */
export default function PacsAdminPage() {
  const [tenant, setTenant] = useState<Awaited<ReturnType<typeof api.tenant.settings.get>> | null>(null);
  const [health, setHealth] = useState<PacsHealth | null>(null);
  const [plugins, setPlugins] = useState<PacsPlugin[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    try {
      const [s, h, p] = await Promise.all([
        api.tenant.settings.get(),
        api.pacs.health(),
        api.pacs.plugins().catch(() => [] as PacsPlugin[]),
      ]);
      setTenant(s);
      setHealth(h);
      setPlugins(p);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => { refresh(); /* eslint-disable-line react-hooks/exhaustive-deps */ }, []);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">PACS bridge</h1>
      <p className="rp-page-sub">
        DICOMweb tenant config, bundled Orthanc proxy availability, and signed
        vendor plugins. PACS connectivity is documented in
        <code> docs/06-operations/pacs-bridge.md</code>.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">DICOMweb</div>
        {tenant === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : (
          <>
            <p className="rp-page-sub">
              Base URL:{' '}
              {tenant.dicomWeb.baseUrl
                ? <code>{tenant.dicomWeb.baseUrl}</code>
                : <span className="badge warn">not configured</span>}
              {' · '}
              Bearer:{' '}
              <span className={`badge ${tenant.dicomWeb.bearerConfigured ? 'ok' : 'info'}`}>
                {tenant.dicomWeb.bearerConfigured ? 'configured' : 'optional'}
              </span>
              {' · '}
              Reachability:{' '}
              <span className={`badge ${health?.dicomWeb.reachable ? 'ok' : 'warn'}`}>
                {health
                  ? (health.dicomWeb.reachable ? 'reachable' : (health.dicomWeb.configured ? 'unreachable' : 'n/a'))
                  : '…'}
              </span>
            </p>
          </>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Bundled Orthanc proxy</div>
        <p className="rp-page-sub">
          Optional. Started via <code>docker compose --profile pacs up -d orthanc</code>.
          Activated by setting <code>RADIOPAD_ORTHANC_URL</code> in the API
          environment.
        </p>
        {health === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : (
          <p className="rp-page-sub">
            Status:{' '}
            <span className={`badge ${health.orthanc.reachable ? 'ok' : (health.orthanc.configured ? 'warn' : 'info')}`}>
              {health.orthanc.configured
                ? (health.orthanc.reachable ? 'reachable' : 'unreachable')
                : 'not configured'}
            </span>
            {health.orthanc.url && <> · URL: <code>{health.orthanc.url}</code></>}
          </p>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Signed vendor plugins</div>
        <p className="rp-page-sub">
          Plugins installed in the desktop&apos;s plugins folder. Each manifest
          is verified by SHA-256 + Ed25519 against{' '}
          <code>RADIOPAD_PLUGIN_PUBKEY</code>. Manifest format and onboarding:
          {' '}<code>desktop/plugin-sdk/</code>.
        </p>
        {plugins === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : plugins.length === 0 ? (
          <p className="rp-page-sub">No plugins installed.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Vendor</th>
                <th>Version</th>
                <th>Capabilities</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {plugins.map((p) => (
                <tr key={p.id}>
                  <td><code>{p.id}</code></td>
                  <td>{p.vendor}</td>
                  <td><code>{p.version}</code></td>
                  <td>{p.capabilities.join(', ')}</td>
                  <td>
                    <span className={`badge ${p.verified ? 'ok' : 'danger'}`}>
                      {p.verified ? 'verified' : 'unsigned / failed'}
                    </span>
                    {' '}
                    <span className={`badge ${p.enabled ? 'ok' : 'info'}`}>
                      {p.enabled ? 'enabled' : 'disabled'}
                    </span>
                    {p.error && <> · <code>{p.error}</code></>}
                  </td>
                  <td>
                    <button
                      className="ghost"
                      onClick={() => api.pacs.setPluginEnabled(p.id, !p.enabled).then(refresh)}
                      disabled={!p.verified}
                      title={!p.verified ? 'Plugin signature not verified — cannot enable' : undefined}
                    >
                      {p.enabled ? 'Disable' : 'Enable'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
