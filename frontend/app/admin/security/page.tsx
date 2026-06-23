'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type SinkStatus = {
  name: string;
  configured: boolean;
  lastPushAt: string | null;
  lastError: string | null;
  totalPushed: number;
  totalErrors: number;
};

type AuditRow = {
  id: string;
  action: string | number;
  detailsJson?: string;
  createdAt: string;
};

type AvailabilitySnapshot = {
  windowSec: number;
  totalProbes: number;
  errorCount: number;
  errorRate: number;
  lastCheckedAt: string | null;
  targets: string[];
};

const AVAILABILITY_BURN_THRESHOLD = 0.05;

const SAMPLE_PLACEHOLDER = `[
  "10.0.0.0/8",
  "192.168.1.0/24",
  "2001:db8::/32"
]`;

/**
 * Iter-32 INT-010 — SIEM push sink panel.
 *
 * Read-only view over the in-process status of every registered SIEM sink
 * (Splunk HEC / Sentinel Log Analytics / Elastic _bulk / Syslog UDP).
 * Configuration lives in env vars on the API host — this page only shows
 * which sinks are active and their last-push outcome.
 */
export default function SecurityAdminPage() {
  return (
    <PermissionGate permission="security.manage" title="Security">
      <SecurityAdminPageInner />
    </PermissionGate>
  );
}

function SecurityAdminPageInner() {
  const [sinks, setSinks] = useState<SinkStatus[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [allowlistJson, setAllowlistJson] = useState<string>('');
  const [allowlistDirty, setAllowlistDirty] = useState(false);
  const [savedSummary, setSavedSummary] = useState<string>('');
  const [alerts, setAlerts] = useState<AuditRow[]>([]);
  const [busy, setBusy] = useState(false);
  const [availability, setAvailability] = useState<AvailabilitySnapshot | null>(null);
  const [availabilityError, setAvailabilityError] = useState<string | null>(null);

  useEffect(() => {
    api.siem.status()
      .then((r) => setSinks(r.sinks))
      .catch((e: Error) => setError(e.message));
    void refresh();
    void refreshAvailability();
  }, []);

  async function refresh() {
    try {
      const settings = await api.tenant.settings.get();
      const json = settings.ipAllowlistJson ?? '';
      setAllowlistJson(json);
      setSavedSummary(summarise(json));
      const recent = (await api.audit.query({ take: 200 })) as AuditRow[];
      const securityAlerts = recent.filter((r) =>
        String(r.action).toLowerCase().includes('securityalert'),
      );
      setAlerts(securityAlerts.slice(0, 50));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function refreshAvailability() {
    try {
      const snap = await api.admin.observability.availability();
      setAvailability(snap);
      setAvailabilityError(null);
    } catch (e) {
      setAvailabilityError((e as Error).message);
    }
  }

  async function saveAllowlist() {
    setBusy(true); setError(null); setInfo(null);
    try {
      if (allowlistJson.trim() !== '') {
        const parsed = JSON.parse(allowlistJson);
        if (!Array.isArray(parsed)) throw new Error('Allowlist must be a JSON array of CIDR strings.');
      }
      await api.tenant.settings.save({ ipAllowlistJson: allowlistJson });
      setAllowlistDirty(false);
      setSavedSummary(summarise(allowlistJson));
      setInfo('Allowlist saved.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function testWebhook() {
    setBusy(true); setError(null); setInfo(null);
    try {
      const result = await api.security.testWebhook();
      if (result.sent) setInfo('Test alert sent. Ask your security team to confirm they received it.');
      else setInfo("A security alert destination hasn't been set up for this workspace yet.");
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
          <h1 className="rp-page-title">Security</h1>
          <p className="rp-page-sub">
            Restrict who can access RadioPad, see recent security alerts, and forward audit events to your security team&apos;s tools.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-page-grid">
        <div className="rp-page-main">

      <div className="rp-panel">
        <div className="rp-panel-title">Allowed networks</div>
        <p className="rp-page-sub">
          Only let users sign in from specific office networks. Leave blank to allow any network.
        </p>
        <p className="rp-page-sub"><strong>Currently active:</strong> {savedSummary || 'No restrictions — any network is allowed.'}</p>
        <details className="rp-advanced">
          <summary>Edit allowed networks (technical — IT team only)</summary>
          <p className="rp-page-sub">
            Enter a JSON list of network ranges (CIDR), one per line.
          </p>
          <textarea
            className="rp-textarea"
            rows={8}
            spellCheck={false}
            placeholder={SAMPLE_PLACEHOLDER}
            value={allowlistJson}
            onChange={(e) => { setAllowlistJson(e.target.value); setAllowlistDirty(true); }}
          />
          <div className="rp-row" style={{ marginTop: 8 }}>
            <button className="primary" onClick={saveAllowlist} disabled={busy || !allowlistDirty}>
              Save
            </button>
            <button className="ghost" onClick={refresh} disabled={busy} style={{ marginLeft: 8 }}>
              Refresh
            </button>
          </div>
        </details>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Recent security alerts</div>
        <p className="rp-page-sub">
          Unusual activity our system flagged — like many failed sign-in attempts in a short window.
        </p>
        {alerts.length === 0 ? (
          <p className="rp-page-sub">No alerts to report. Things look quiet.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Reason</th>
              </tr>
            </thead>
            <tbody>
              {alerts.map((a) => (
                <tr key={a.id}>
                  <td>{new Date(a.createdAt).toLocaleString()}</td>
                  <td>{extractField(a.detailsJson, 'reason') || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Alert your security team</div>
        <p className="rp-page-sub">
          When something suspicious happens, RadioPad can send an automatic notification to your hospital&apos;s security team.
        </p>
        <button className="primary-ghost" onClick={testWebhook} disabled={busy}>
          Send test alert
        </button>
        <details className="rp-advanced">
          <summary>For IT teams — webhook configuration</summary>
          <p className="rp-page-sub">
            Alerts POST JSON to <code>RADIOPAD_SECURITY_WEBHOOK_URL</code> signed with{' '}
            <code>X-RadioPad-Signature: sha256=&lt;hex&gt;</code> HMAC derived from{' '}
            <code>RADIOPAD_SECURITY_WEBHOOK_SECRET</code>. The secret is never echoed back.
          </p>
        </details>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">System availability</div>
        {availabilityError && <div className="rp-banner danger">{availabilityError}</div>}
        {availability === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : (
          <>
            {availability.errorRate > AVAILABILITY_BURN_THRESHOLD && (
              <div className="rp-banner warn">
                System health check warning — RadioPad is responding more slowly than expected. Ask IT to take a look.
              </div>
            )}
            <div className="rp-grid-3">
              <div className="rp-stat-tile">
                <div className="rp-stat-label">Uptime (last 5 minutes)</div>
                <div className="rp-stat-value">
                  {(100 - availability.errorRate * 100).toFixed(2)}%
                </div>
                <div className="rp-stat-sub">
                  {availability.totalProbes - availability.errorCount} of {availability.totalProbes} checks passed
                </div>
              </div>
              <div className="rp-stat-tile">
                <div className="rp-stat-label">Last checked</div>
                <div className="rp-stat-value">
                  {availability.lastCheckedAt
                    ? new Date(availability.lastCheckedAt).toLocaleTimeString()
                    : '—'}
                </div>
                <div className="rp-stat-sub">
                  Monitoring {availability.targets.length} service{availability.targets.length === 1 ? '' : 's'}
                </div>
              </div>
            </div>
            <div className="rp-row" style={{ marginTop: 8 }}>
              <button className="ghost" onClick={refreshAvailability} disabled={busy}>
                Refresh
              </button>
            </div>
          </>
        )}
      </div>

      <details className="rp-panel rp-advanced">
        <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>Advanced — rate limits &amp; SIEM forwarding</summary>
        <p className="rp-page-sub rp-mt-sm"><strong>Rate limits (per minute):</strong> 100 per IP, 5000 per workspace. Override via{' '}
          <code>RADIOPAD_RATE_LIMIT_IP_PER_MIN</code> /{' '}
          <code>RADIOPAD_RATE_LIMIT_TENANT_PER_MIN</code>.
        </p>
        <p className="rp-page-sub">
          <strong>SIEM forwarding:</strong> audit events are forwarded continuously to Splunk HEC / Sentinel Log Analytics / Elastic / Syslog when sink env vars are set. PHI is excluded — only ids, action codes, timestamps and integrity hashes ship.
        </p>
        {sinks === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : sinks.length === 0 ? (
          <p className="rp-page-sub">No SIEM destinations registered.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Destination</th>
                <th>Configured</th>
                <th>Last push</th>
                <th>Total pushed</th>
                <th>Errors</th>
              </tr>
            </thead>
            <tbody>
              {sinks.map((s) => (
                <tr key={s.name}>
                  <td>{s.name}</td>
                  <td>
                    <span className={`badge ${s.configured ? 'ok' : 'info'}`}>
                      {s.configured ? 'yes' : 'no'}
                    </span>
                  </td>
                  <td>{s.lastPushAt ? new Date(s.lastPushAt).toLocaleString() : '—'}</td>
                  <td>{s.totalPushed}</td>
                  <td>{s.totalErrors}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </details>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What this page does</div>
            <p>Manages who can access RadioPad and how your security team is alerted to suspicious activity.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Who should use it</div>
            <p>IT Admin, Security Admin. If something here looks wrong, ask them — don&apos;t change settings unless you&apos;re sure.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}

function summarise(json: string): string {
  if (!json || !json.trim()) return '';
  try {
    const arr = JSON.parse(json);
    if (!Array.isArray(arr)) return '(invalid)';
    if (arr.length === 0) return '(empty)';
    const sample = arr.slice(0, 3).map(String).join(', ');
    return arr.length > 3 ? `${sample}, … (${arr.length} total)` : sample;
  } catch {
    return '(invalid JSON)';
  }
}

function extractField(json: string | undefined, key: string): string {
  if (!json) return '';
  try {
    const obj = JSON.parse(json) as Record<string, unknown>;
    return typeof obj[key] === 'string' ? (obj[key] as string) : '';
  } catch {
    return '';
  }
}
