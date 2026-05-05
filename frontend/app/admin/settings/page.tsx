'use client';

import { useEffect, useState } from 'react';
import { api, PLAN_LABELS, publicEnv } from '@/lib/api';

type Settings = Awaited<ReturnType<typeof api.tenant.settings.get>>;

const SEVERITIES: ('Info' | 'Warning' | 'Blocker')[] = ['Info', 'Warning', 'Blocker'];

const SEVERITY_BADGE: Record<string, string> = {
  Info: 'info',
  Warning: 'warn',
  Blocker: 'danger',
};

export default function SettingsPage() {
  const [s, setS] = useState<Settings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api.tenant.settings.get().then(setS).catch((e: Error) => setError(e.message));
  }, []);

  const [ingestSecret, setIngestSecret] = useState('');
  const [dicomBearer, setDicomBearer] = useState('');
  // Iter-32 SEC-003 — customer-managed key (CMK) input is opaque; we never
  // surface the active value back to the UI other than the configured ref.
  const [cmkKeyRef, setCmkKeyRef] = useState('');
  const [verifying, setVerifying] = useState(false);

  async function save() {
    if (!s) return;
    setSaving(true);
    setError(null);
    setInfo(null);
    try {
      await api.tenant.settings.save({
        hallucinationDetectionEnabled: s.hallucinationDetectionEnabled,
        hallucinationSeverity: s.hallucinationSeverity,
        hallucinationAllowList: s.hallucinationAllowList,
        hallucinationMinSupport: s.hallucinationMinSupport,
        plan: s.plan,
        featureFlagsJson: s.featureFlagsJson,
        ingestBearerSecret: ingestSecret || null,
        dicomWebBaseUrl: s.dicomWeb.baseUrl,
        dicomWebBearerSecret: dicomBearer || null,
        cmkKeyRef: cmkKeyRef || null,
      });
      setIngestSecret('');
      setDicomBearer('');
      setCmkKeyRef('');
      const fresh = await api.tenant.settings.get();
      setS(fresh);
      setInfo('Settings saved.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  async function verifyKms() {
    setVerifying(true);
    setError(null);
    setInfo(null);
    try {
      const result = await api.tenant.settings.verifyKms();
      const fresh = await api.tenant.settings.get();
      setS(fresh);
      setInfo(
        result.ok
          ? `KMS round-trip OK (scheme: ${result.scheme}). Last verified ${new Date(result.lastVerifiedAt!).toLocaleString()}.`
          : 'KMS round-trip did not match — check the configured keyRef.',
      );
    } catch (e) {
      // 422 / 503 surface as Error with body — show the reason.
      setError((e as Error).message);
    } finally {
      setVerifying(false);
    }
  }

  async function openPortal() {
    try {
      const { url } = await api.billing.portal(window.location.href);
      window.location.href = url;
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function checkout(priceId: string) {
    try {
      const { url } = await api.billing.checkout(
        priceId,
        window.location.origin + '/admin/settings?billing=success',
        window.location.origin + '/admin/settings?billing=cancelled',
      );
      window.location.href = url;
    } catch (e) {
      setError((e as Error).message);
    }
  }

  if (!s) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Tenant settings</h1>
        {error && <div className="banner warn">{error}</div>}
        {!error && <p className="rp-page-sub">Loading…</p>}
      </div>
    );
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Tenant settings</h1>
      <p className="rp-page-sub">
        Per-tenant administration: AI safety controls, billing plan, and feature flags.
        These settings are scoped to your tenant only.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">AI hallucination detector</div>
        <p className="rp-page-sub">
          Flags Impression sentences that are not clearly supported by Findings or
          Study context. Runs deterministically on every <code>POST /reports/:id/validate</code>.
        </p>

        <label className="rp-field rp-row">
          <input
            type="checkbox"
            checked={s.hallucinationDetectionEnabled}
            onChange={(e) => setS({ ...s, hallucinationDetectionEnabled: e.target.checked })}
          />
          <span style={{ marginLeft: 8 }}>Enable hallucination detector</span>
        </label>

        <label className="rp-field">
          <span>
            Severity when triggered{' '}
            <span className={`badge ${SEVERITY_BADGE[s.hallucinationSeverity] ?? ''}`}>
              {s.hallucinationSeverity}
            </span>
          </span>
          <select
            className="rp-input"
            value={s.hallucinationSeverity}
            onChange={(e) =>
              setS({ ...s, hallucinationSeverity: e.target.value as Settings['hallucinationSeverity'] })
            }
          >
            {SEVERITIES.map((sv) => (
              <option key={sv} value={sv}>
                {sv}
              </option>
            ))}
          </select>
        </label>

        <label className="rp-field">
          <span>
            Minimum support fraction (0.00 – 1.00) — current: <code>{s.hallucinationMinSupport.toFixed(2)}</code>
          </span>
          <input
            className="rp-input"
            type="number"
            min={0}
            max={1}
            step={0.05}
            value={s.hallucinationMinSupport}
            onChange={(e) =>
              setS({ ...s, hallucinationMinSupport: Math.max(0, Math.min(1, Number(e.target.value))) })
            }
          />
        </label>

        <label className="rp-field">
          <span>Allow-list (one term/phrase per line — never flag these)</span>
          <textarea
            className="rp-input"
            rows={5}
            value={s.hallucinationAllowList}
            onChange={(e) => setS({ ...s, hallucinationAllowList: e.target.value })}
            placeholder="incidental finding&#10;recommend follow-up&#10;clinical correlation advised"
          />
        </label>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Plan &amp; billing</div>
        <p className="rp-page-sub">
          Current plan: <span className="badge info">{PLAN_LABELS[s.plan]}</span>
          {s.stripe.status && (
            <>
              {' '}
              · Stripe status: <code>{s.stripe.status}</code>
            </>
          )}
          {s.stripe.currentPeriodEnd && (
            <>
              {' '}
              · Renews: <code>{new Date(s.stripe.currentPeriodEnd).toLocaleDateString()}</code>
            </>
          )}
        </p>

        <label className="rp-field">
          <span>Plan tier (manual override; webhook is authoritative)</span>
          <select
            className="rp-input"
            value={s.plan}
            onChange={(e) => setS({ ...s, plan: Number(e.target.value) as Settings['plan'] })}
          >
            <option value={0}>0 — Trial</option>
            <option value={1}>1 — Team</option>
            <option value={2}>2 — Enterprise</option>
          </select>
        </label>

        <div className="rp-row" style={{ gap: 8, marginTop: 8 }}>
          <button
            className="primary-ghost"
            onClick={() => checkout(publicEnv('NEXT_PUBLIC_STRIPE_PRICE_TEAM') ?? 'price_team_placeholder')}
          >
            Subscribe / upgrade — Team
          </button>
          <button
            className="primary-ghost"
            onClick={() => checkout(publicEnv('NEXT_PUBLIC_STRIPE_PRICE_ENTERPRISE') ?? 'price_enterprise_placeholder')}
          >
            Subscribe / upgrade — Enterprise
          </button>
          <button className="ghost" onClick={openPortal} disabled={!s.stripe.customerId}>
            Manage billing in Stripe portal
          </button>
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Integrations</div>
        <p className="rp-page-sub">
          Inbound HL7/FHIR ingest webhook (PRD INT-001..004) and DICOMweb
          (WADO-RS / QIDO-RS) study-context lookup (PRD DCM-001..006).
          Secrets are stored server-side and never echoed back; leave the
          field blank to keep the existing value.
        </p>

        <label className="rp-field">
          <span>
            Ingest bearer token{' '}
            <span className={`badge ${s.ingest.bearerConfigured ? 'ok' : 'warn'}`}>
              {s.ingest.bearerConfigured ? 'configured' : 'disabled'}
            </span>
          </span>
          <input
            className="rp-input"
            type="password"
            value={ingestSecret}
            onChange={(e) => setIngestSecret(e.target.value)}
            placeholder={s.ingest.bearerConfigured ? '(unchanged)' : 'enter to enable /api/ingest/order'}
            autoComplete="new-password"
          />
        </label>

        <label className="rp-field">
          <span>DICOMweb base URL (QIDO-RS / WADO-RS)</span>
          <input
            className="rp-input"
            value={s.dicomWeb.baseUrl}
            onChange={(e) => setS({ ...s, dicomWeb: { ...s.dicomWeb, baseUrl: e.target.value } })}
            placeholder="https://pacs.example.org/dicom-web"
          />
        </label>

        <label className="rp-field">
          <span>
            DICOMweb bearer token{' '}
            <span className={`badge ${s.dicomWeb.bearerConfigured ? 'ok' : 'info'}`}>
              {s.dicomWeb.bearerConfigured ? 'configured' : 'optional'}
            </span>
          </span>
          <input
            className="rp-input"
            type="password"
            value={dicomBearer}
            onChange={(e) => setDicomBearer(e.target.value)}
            placeholder={s.dicomWeb.bearerConfigured ? '(unchanged)' : 'optional'}
            autoComplete="new-password"
          />
        </label>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Customer-managed encryption key (CMK)</div>
        <p className="rp-page-sub">
          PRD SEC-003 — supply your KMS key reference. Supported schemes:
          {' '}
          <code>env:NAME</code>, <code>local:/path</code>,
          {' '}
          <code>aws:arn:aws:kms:&lt;region&gt;:&lt;acct&gt;:key/&lt;id&gt;</code>,
          {' '}
          <code>azkv:https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;/&lt;version&gt;</code>,
          {' '}
          <code>gcp:projects/&lt;p&gt;/locations/&lt;l&gt;/keyRings/&lt;r&gt;/cryptoKeys/&lt;k&gt;</code>.
          {' '}
          The key reference is opaque — we never echo wrapped key material.
        </p>

        <label className="rp-field">
          <span>
            CMK key reference{' '}
            {s.cmk?.configured ? (
              <span className="badge ok">configured</span>
            ) : (
              <span className="badge warn">not configured</span>
            )}
            {s.cmk?.keyRef && (
              <>
                {' '}— scheme:{' '}
                <code>{s.cmk.keyRef.split(':')[0]}</code>
              </>
            )}
          </span>
          <input
            className="rp-input"
            value={cmkKeyRef}
            onChange={(e) => setCmkKeyRef(e.target.value)}
            placeholder={s.cmk?.keyRef ?? 'env:RADIOPAD_TENANT_KEK'}
            autoComplete="off"
          />
        </label>

        <div className="rp-row" style={{ gap: 8, alignItems: 'center', marginTop: 8 }}>
          <button
            className="primary-ghost"
            onClick={verifyKms}
            disabled={verifying || !s.cmk?.configured}
          >
            {verifying ? 'Verifying…' : 'Verify round-trip'}
          </button>
          <span className="rp-page-sub" style={{ marginLeft: 8 }}>
            Last verified:{' '}
            {s.cmk?.lastVerifiedAt
              ? <code>{new Date(s.cmk.lastVerifiedAt).toLocaleString()}</code>
              : <span className="badge warn">never</span>}
          </span>
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Feature flags (raw JSON)</div>
        <p className="rp-page-sub">
          Free-form JSON object. Keys are interpreted by the application;
          unknown keys are preserved.
        </p>
        <textarea
          className="rp-input"
          rows={6}
          value={s.featureFlagsJson}
          onChange={(e) => setS({ ...s, featureFlagsJson: e.target.value })}
        />
      </div>

      <div className="rp-row" style={{ justifyContent: 'flex-end', marginTop: 16 }}>
        <button className="primary" onClick={save} disabled={saving}>
          {saving ? 'Saving…' : 'Save settings'}
        </button>
      </div>
    </div>
  );
}
