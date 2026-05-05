'use client';

/**
 * BILL-006 — Plan feature-flags page. Read-only view of
 * `GET /api/billing/features` (already typed in `api.ts`) plus a panel
 * describing what each flag gates.
 */

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

type FeatureMap = Awaited<ReturnType<typeof api.billing.features>>;

const FLAG_DESCRIPTIONS: Record<string, { label: string; gates: string }> = {
  scim: {
    label: 'SCIM 2.0 provisioning',
    gates: 'Automatic user lifecycle from Okta / Azure AD / Google Workspace via /scim/v2/Users.',
  },
  siemExport: {
    label: 'SIEM export',
    gates: 'NDJSON / CEF audit-log export at GET /api/audit/siem for Splunk / QRadar / Sentinel.',
  },
  marketplacePublish: {
    label: 'Marketplace publishing',
    gates: 'Submit rulebooks / templates to the marketplace and earn revenue share via Stripe Connect.',
  },
  advancedAnalytics: {
    label: 'Advanced analytics',
    gates: 'Per-month KPI breakdowns, cohort comparisons, and export of analytics rows beyond the 30-day default.',
  },
  stripeConnect: {
    label: 'Stripe Connect payouts',
    gates: 'Receive marketplace payouts via Express onboarding (POST /api/marketplace/connect/onboarding).',
  },
  customKms: {
    label: 'Customer-managed keys',
    gates: 'Bring-your-own KMS reference for at-rest encryption (AWS KMS / Azure Key Vault / GCP KMS).',
  },
  ipAllowlist: {
    label: 'IP allowlist',
    gates: 'Restrict the API to the CIDR ranges in RADIOPAD_IP_ALLOWLIST (loopback always permitted).',
  },
  priorCompare: {
    label: 'Prior-report comparison',
    gates: 'Side-by-side comparison of the current report with the most recent prior on the same body part.',
  },
  voiceDictation: {
    label: 'Voice dictation',
    gates: 'In-browser dictation via the Web Speech API and the optional Whisper-local desktop sidecar.',
  },
  mcpReadOnly: {
    label: 'MCP read-only server',
    gates: 'Expose RadioPad as a read-only Model Context Protocol server via `radiopad mcp serve`.',
  },
};

function describe(flag: string): { label: string; gates: string } {
  return (
    FLAG_DESCRIPTIONS[flag] ?? {
      label: flag,
      gates: 'No description recorded — see docs/03-architecture/api-reference.md.',
    }
  );
}

export default function FeatureFlagsPage() {
  const [data, setData] = useState<FeatureMap | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.billing.features().then(setData).catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Plan feature flags</h1>
      <p className="rp-page-sub">
        Read-only view of the active tenant&apos;s plan and the feature flags it gates.
        Source: <code>GET /api/billing/features</code>.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {!data && !error && <p className="rp-page-sub">Loading…</p>}

      {data && (
        <>
          <div className="rp-panel">
            <div className="rp-panel-title">
              Plan
              <span className="badge info">{data.plan}</span>
            </div>
            <p className="rp-page-sub">
              Plan changes are applied via Stripe Checkout / Billing Portal.
              See <code>/admin/billing</code>.
            </p>
          </div>

          <div className="rp-panel">
            <div className="rp-panel-title">Features</div>
            <table className="rp-table">
              <thead>
                <tr>
                  <th>Flag</th>
                  <th>Status</th>
                  <th>Gates</th>
                </tr>
              </thead>
              <tbody>
                {Object.keys(data.features).length === 0 && (
                  <tr>
                    <td colSpan={3} className="rp-page-sub">No flags returned.</td>
                  </tr>
                )}
                {Object.entries(data.features).map(([flag, enabled]) => {
                  const meta = describe(flag);
                  return (
                    <tr key={flag}>
                      <td>
                        <code>{flag}</code>
                        <div className="rp-stat-label">{meta.label}</div>
                      </td>
                      <td>
                        {enabled ? (
                          <span className="badge ok">enabled</span>
                        ) : (
                          <span className="badge">disabled</span>
                        )}
                      </td>
                      <td>{meta.gates}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
