'use client';

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
      const json = (settings as unknown as { ipAllowlistJson?: string }).ipAllowlistJson ?? '';
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
      await api.tenant.settings.save({
        ipAllowlistJson: allowlistJson,
      } as unknown as Parameters<typeof api.tenant.settings.save>[0]);
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
      if (result.sent) setInfo('Test alert dispatched. Check your security webhook receiver.');
      else setInfo('Security webhook is not configured for this environment.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Security &amp; SIEM</h1>
      <p className="rp-page-sub">
        Continuous SIEM delivery from the append-only audit log. Sinks are
        opt-in via env vars; failures retry up to 3× with backoff and never
        block <code>/api/*</code>. PHI minimisation: only ids + action codes +
        timestamps + integrity hash are exported. <code>DetailsJson</code> is
        intentionally excluded.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">IP allowlist</div>
        <p className="rp-page-sub">
          JSON array of CIDR strings (IPv4 + IPv6). ANDed with the global
          {' '}<code>RADIOPAD_IP_ALLOWLIST</code> envvar. Loopback
          (<code>127.0.0.1</code>, <code>::1</code>) is always allowed.
          {' '}<code>X-Forwarded-For</code> is honoured only when
          {' '}<code>RADIOPAD_TRUST_FORWARDED_FOR=1</code>.
        </p>
        <p className="rp-page-sub"><strong>Active:</strong> {savedSummary || '— (none configured)'}</p>
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
            Save allowlist
          </button>
          <button className="ghost" onClick={refresh} disabled={busy} style={{ marginLeft: 8 }}>
            Refresh
          </button>
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Rate limit</div>
        <p className="rp-page-sub">Active limits (60-second fixed window):</p>
        <ul className="rp-list">
          <li><strong>Per-IP:</strong> 100 req/min (override <code>RADIOPAD_RATE_LIMIT_IP_PER_MIN</code>)</li>
          <li><strong>Per-tenant:</strong> 5000 req/min (override <code>RADIOPAD_RATE_LIMIT_TENANT_PER_MIN</code>)</li>
          <li><strong>Bypass:</strong> <code>/api/health</code>, <code>/api/health/ready</code>, loopback</li>
        </ul>
        <p className="rp-page-sub">
          Rejections return RFC-7807 problem+json with{' '}
          <code>kind: &quot;rate_limited&quot;</code> and a{' '}
          <code>Retry-After</code> header.
        </p>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Security alerts</div>
        <p className="rp-page-sub">
          Latest 50 entries from the anomaly detector (audit action{' '}
          <code>SecurityAlert</code>).
        </p>
        {alerts.length === 0 ? (
          <p className="rp-page-sub">No alerts in the audit window.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Time (UTC)</th>
                <th>Reason</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {alerts.map((a) => (
                <tr key={a.id}>
                  <td><code>{a.createdAt}</code></td>
                  <td>{extractField(a.detailsJson, 'reason') || '—'}</td>
                  <td><code>{a.detailsJson || ''}</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Security webhook</div>
        <p className="rp-page-sub">
          Anomaly detector POSTs JSON to <code>RADIOPAD_SECURITY_WEBHOOK_URL</code>{' '}
          with an <code>X-RadioPad-Signature: sha256=&lt;hex&gt;</code> HMAC header
          derived from <code>RADIOPAD_SECURITY_WEBHOOK_SECRET</code>. The secret is
          never echoed back in responses or audit rows.
        </p>
        <button className="primary-ghost" onClick={testWebhook} disabled={busy}>
          Test webhook
        </button>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Availability</div>
        <p className="rp-page-sub">
          In-process synthetic monitor. Probes the listed health endpoints
          every <code>RADIOPAD_AVAILABILITY_PROBE_INTERVAL_SEC</code> seconds
          and maintains a 5-minute rolling failure window. Burn-rate
          breaches above the configured threshold append a
          {' '}<code>SystemAlert</code> audit row with{' '}
          <code>kind=availability_burn_rate</code>.
        </p>
        {availabilityError && (
          <div className="rp-banner danger">{availabilityError}</div>
        )}
        {availability === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : (
          <>
            {availability.errorRate > AVAILABILITY_BURN_THRESHOLD && (
              <div className="rp-banner warn">
                Burn-rate threshold exceeded — error rate{' '}
                <code>{(availability.errorRate * 100).toFixed(1)}%</code>{' '}
                over the last <code>{availability.windowSec}s</code>.
              </div>
            )}
            <div className="rp-grid-3">
              <div className="rp-stat-tile">
                <div className="rp-stat-label">Error rate</div>
                <div className="rp-stat-value">
                  {(availability.errorRate * 100).toFixed(2)}%
                </div>
                <div className="rp-stat-sub">
                  {availability.errorCount} / {availability.totalProbes} probes
                </div>
              </div>
              <div className="rp-stat-tile">
                <div className="rp-stat-label">Window</div>
                <div className="rp-stat-value">{availability.windowSec}s</div>
                <div className="rp-stat-sub">rolling 5-minute window</div>
              </div>
              <div className="rp-stat-tile">
                <div className="rp-stat-label">Last checked</div>
                <div className="rp-stat-value">
                  {availability.lastCheckedAt
                    ? new Date(availability.lastCheckedAt).toLocaleTimeString()
                    : '—'}
                </div>
                <div className="rp-stat-sub">
                  {availability.targets.length} target
                  {availability.targets.length === 1 ? '' : 's'}
                </div>
              </div>
            </div>
            <ul className="rp-list" style={{ marginTop: 8 }}>
              {availability.targets.map((t) => (
                <li key={t} className="rp-divider-row">
                  <code>{t}</code>
                </li>
              ))}
            </ul>
            <div className="rp-row" style={{ marginTop: 8 }}>
              <button className="ghost" onClick={refreshAvailability} disabled={busy}>
                Refresh
              </button>
            </div>
          </>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">SIEM sinks</div>
        {sinks === null ? (
          <p className="rp-page-sub">Loading…</p>
        ) : sinks.length === 0 ? (
          <p className="rp-page-sub">No sinks registered.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Sink</th>
                <th>Configured</th>
                <th>Last push</th>
                <th>Total pushed</th>
                <th>Errors</th>
                <th>Last error</th>
              </tr>
            </thead>
            <tbody>
              {sinks.map((s) => (
                <tr key={s.name}>
                  <td><code>{s.name}</code></td>
                  <td>
                    <span className={`badge ${s.configured ? 'ok' : 'info'}`}>
                      {s.configured ? 'configured' : 'not configured'}
                    </span>
                  </td>
                  <td>{s.lastPushAt ? new Date(s.lastPushAt).toLocaleString() : '—'}</td>
                  <td><code>{s.totalPushed}</code></td>
                  <td><code>{s.totalErrors}</code></td>
                  <td>{s.lastError ? <code>{s.lastError}</code> : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Snapshot export</div>
        <p className="rp-page-sub">
          Continuous SIEM delivery is the default. For ad-hoc compliance pulls
          use the snapshot endpoint <code>GET /api/audit/siem?format=json|cef</code>.
        </p>
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
