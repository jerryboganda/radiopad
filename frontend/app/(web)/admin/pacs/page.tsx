'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';
import Banner from '@/components/ui/Banner';
import EmptyState from '@/components/ui/EmptyState';
import Skeleton, { TableSkeleton } from '@/components/ui/Skeleton';

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

type PacsVendor = 'sectra' | 'visage' | 'carestream' | '';

const PACS_VENDOR_OPTIONS: { value: PacsVendor; label: string }[] = [
  { value: '', label: 'Generic DICOMweb' },
  { value: 'sectra', label: 'Sectra IDS7' },
  { value: 'visage', label: 'Visage 7' },
  { value: 'carestream', label: 'Carestream Vue' },
];

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
  return (
    <PermissionGate permission="tenant_settings.manage" title="PACS integration">
      <PacsAdminPageInner />
    </PermissionGate>
  );
}

function PacsAdminPageInner() {
  const [tenant, setTenant] = useState<Awaited<ReturnType<typeof api.tenant.settings.get>> | null>(null);
  const [health, setHealth] = useState<PacsHealth | null>(null);
  const [plugins, setPlugins] = useState<PacsPlugin[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedVendor, setSelectedVendor] = useState<PacsVendor>('');
  const [savingVendor, setSavingVendor] = useState(false);

  async function refresh() {
    try {
      const [s, h, p] = await Promise.all([
        api.tenant.settings.get(),
        api.pacs.health(),
        api.pacs.plugins().catch(() => [] as PacsPlugin[]),
      ]);
      setTenant(s);
      setSelectedVendor((s.pacs.vendor ?? '') as PacsVendor);
      setHealth(h);
      setPlugins(p);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => { refresh(); /* eslint-disable-line react-hooks/exhaustive-deps */ }, []);

  async function saveVendor() {
    setSavingVendor(true);
    setError(null);
    try {
      await api.tenant.settings.save({ pacsVendor: selectedVendor });
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSavingVendor(false);
    }
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Imaging archive</h1>
          <p className="rp-page-sub">
            How RadioPad connects to your hospital&apos;s imaging archive so the right images appear next to each report.
          </p>
        </div>
      </header>

      <div className="rp-page-grid">
        <div className="rp-page-main">

      {error && <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>}

      <div className="rp-panel">
        <div className="rp-panel-title">Hospital imaging archive</div>
        {tenant === null ? (
          <Skeleton variant="block" height={72} />
        ) : (
          <>
            <p className="rp-page-sub">
              {tenant.dicomWeb.baseUrl
                ? <>Connected to: <strong>{tenant.dicomWeb.baseUrl}</strong></>
                : <span className="badge warn">Not connected yet</span>}
              {' · '}
              <span className={`badge ${health?.dicomWeb.reachable ? 'ok' : 'warn'}`}>
                {health
                  ? (health.dicomWeb.reachable ? 'Online' : (health.dicomWeb.configured ? 'Offline' : 'Not connected'))
                  : 'Checking…'}
              </span>
            </p>
            <details className="rp-advanced">
              <summary>Show technical details</summary>
              <p className="rp-page-sub">
                Authentication: <span className={`badge ${tenant.dicomWeb.bearerConfigured ? 'ok' : 'info'}`}>
                  {tenant.dicomWeb.bearerConfigured ? 'configured' : 'optional'}
                </span>{' '}
                · Edit the connection on the <a href="/admin/settings">Workspace settings</a> page (advanced section).
              </p>
              <label className="rp-field">
                <span>Vendor adapter</span>
                <select
                  className="rp-input"
                  value={selectedVendor}
                  onChange={(e) => setSelectedVendor(e.target.value as PacsVendor)}
                >
                  {PACS_VENDOR_OPTIONS.map((option) => (
                    <option key={option.value || 'generic'} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>
              <button className="primary" type="button" onClick={saveVendor} disabled={savingVendor} aria-busy={savingVendor}>
                {savingVendor && <span className="rp-spinner sm" aria-hidden />}
                {savingVendor ? 'Saving…' : 'Save vendor'}
              </button>
            </details>
          </>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Built-in test viewer</div>
        <p className="rp-page-sub">
          An optional sample image server, useful for trying out RadioPad without connecting to your real archive.
        </p>
        {health === null ? (
          <Skeleton variant="text" width="40%" />
        ) : (
          <p className="rp-page-sub">
            Status:{' '}
            <span className={`badge ${health.orthanc.reachable ? 'ok' : (health.orthanc.configured ? 'warn' : 'info')}`}>
              {health.orthanc.configured
                ? (health.orthanc.reachable ? 'Online' : 'Offline')
                : 'Not enabled'}
            </span>
          </p>
        )}
        <details className="rp-advanced">
          <summary>For IT teams — how to enable</summary>
          <p className="rp-page-sub">
            Start the bundled Orthanc proxy via <code>docker compose --profile pacs up -d orthanc</code>{' '}
            and set <code>RADIOPAD_ORTHANC_URL</code> on the API host.
          </p>
        </details>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Vendor add-ons</div>
        <p className="rp-page-sub">
          Plug-ins from your imaging vendor (Sectra, AGFA, Visage, Merge, Hyland) that let RadioPad open studies in the way that&apos;s native to your hospital.
        </p>
        {plugins === null ? (
          <TableSkeleton rows={3} cols={6} />
        ) : plugins.length === 0 ? (
          <EmptyState
            title="No add-ons installed yet"
            description="Vendor plug-ins advertised by the desktop bridge will appear here once installed."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Add-on</th>
                <th>Vendor</th>
                <th>Version</th>
                <th>What it can do</th>
                <th>Status</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {plugins.map((p) => (
                <tr key={p.id}>
                  <td>{p.name}</td>
                  <td>{p.vendor}</td>
                  <td>{p.version}</td>
                  <td>{p.capabilities.join(', ')}</td>
                  <td>
                    <span className={`badge ${p.verified ? 'ok' : 'danger'}`}>
                      {p.verified ? 'Trusted' : 'Not trusted'}
                    </span>
                    {' '}
                    <span className={`badge ${p.enabled ? 'ok' : 'info'}`}>
                      {p.enabled ? 'On' : 'Off'}
                    </span>
                    {p.error && <> · <span className="rp-page-sub">{p.error}</span></>}
                  </td>
                  <td>
                    <button
                      className="ghost"
                      onClick={() => api.pacs.setPluginEnabled(p.id, !p.enabled).then(refresh)}
                      disabled={!p.verified}
                      title={!p.verified ? "This add-on hasn't been verified — ask your IT team" : undefined}
                    >
                      {p.enabled ? 'Turn off' : 'Turn on'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <details className="rp-advanced">
          <summary>For IT teams — trust &amp; signing</summary>
          <p className="rp-page-sub">
            Each add-on is verified by SHA-256 + Ed25519 against{' '}
            <code>RADIOPAD_PLUGIN_PUBKEY</code>. Manifest format and onboarding live in <code>desktop/plugin-sdk/</code>.
          </p>
        </details>
      </div>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">Why this matters</div>
            <p>When this is working, opening a report in RadioPad will automatically show the matching images from your hospital&apos;s archive — no extra clicks needed.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Need help?</div>
            <p>This page is mostly for your IT team. If something here shows red or &quot;Not connected&quot;, ask them to take a look.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
