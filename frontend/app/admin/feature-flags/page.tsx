'use client';

import PermissionGate from '@/components/ui/PermissionGate';

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
    label: 'Auto-add users from your identity provider',
    gates: 'Automatically create / disable user accounts based on what your identity system (Okta, Azure AD, Google Workspace) says.',
  },
  siemExport: {
    label: 'Forward audit log to security tools',
    gates: 'Lets your security team pull RadioPad activity into their monitoring tools (Splunk, QRadar, Sentinel).',
  },
  marketplacePublish: {
    label: 'Publish rulebooks to the marketplace',
    gates: 'Lets your workspace publish rulebooks and templates other clinics can use — with optional revenue share.',
  },
  advancedAnalytics: {
    label: 'Advanced analytics',
    gates: 'Per-month breakdowns, comparisons across teams, and data exports beyond the past 30 days.',
  },
  stripeConnect: {
    label: 'Marketplace payouts',
    gates: 'Receive payouts when others use rulebooks or templates your workspace published.',
  },
  customKms: {
    label: 'Bring-your-own encryption key',
    gates: 'Manage the encryption key for your data with your own cloud key vault (AWS / Azure / GCP).',
  },
  ipAllowlist: {
    label: 'Restrict access by network',
    gates: 'Only allow sign-in from specific office networks you list.',
  },
  priorCompare: {
    label: 'Compare with prior report',
    gates: 'Show the most recent prior report on the same body part side-by-side while drafting.',
  },
  voiceDictation: {
    label: 'Voice dictation',
    gates: 'Dictate into reports using your microphone, in the browser or with an offline desktop helper.',
  },
  mcpReadOnly: {
    label: 'External AI tools (read-only)',
    gates: 'Let approved external AI tools read from RadioPad for analysis. No writes.',
  },
};

function describe(flag: string): { label: string; gates: string } {
  return (
    FLAG_DESCRIPTIONS[flag] ?? {
      label: flag,
      gates: 'No description yet — ask your IT team.',
    }
  );
}

export default function FeatureFlagsPage() {
  return (
    <PermissionGate permission="billing.read" title="Feature flags">
      <FeatureFlagsPageInner />
    </PermissionGate>
  );
}

function FeatureFlagsPageInner() {
  const [data, setData] = useState<FeatureMap | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.billing.features().then(setData).catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">What&apos;s included in your plan</h1>
          <p className="rp-page-sub">
            A read-only list of features your workspace can use today, and what each one unlocks.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}
      {!data && !error && <p className="rp-page-sub">Loading…</p>}

      {data && (
        <>
          <div className="rp-panel">
            <div className="rp-panel-title">
              Your plan
              <span className="badge info" style={{ marginLeft: 8 }}>{data.plan}</span>
            </div>
            <p className="rp-page-sub">
              To change plans, go to <a href="/admin/billing">Billing &amp; plan</a>.
            </p>
          </div>

          <div className="rp-panel">
            <div className="rp-panel-title">Features</div>
            <table className="rp-table">
              <thead>
                <tr>
                  <th>Feature</th>
                  <th>Status</th>
                  <th>What it does</th>
                </tr>
              </thead>
              <tbody>
                {Object.keys(data.features).length === 0 && (
                  <tr>
                    <td colSpan={3} className="rp-page-sub">No features to show.</td>
                  </tr>
                )}
                {Object.entries(data.features).map(([flag, enabled]) => {
                  const meta = describe(flag);
                  return (
                    <tr key={flag}>
                      <td>
                        <strong>{meta.label}</strong>
                      </td>
                      <td>
                        {enabled ? (
                          <span className="badge ok">Included</span>
                        ) : (
                          <span className="badge">Not on this plan</span>
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
