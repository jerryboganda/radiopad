'use client';

import { useEffect, useState } from 'react';
import { api, PLAN_LABELS, publicEnv } from '@/lib/api';
import { isAuthError, useAuthSession } from '@/lib/useAuthSession';
import SignInRequired from '@/components/ui/SignInRequired';

type Settings = Awaited<ReturnType<typeof api.tenant.settings.get>>;

const SEVERITY_OPTIONS: { value: 'Info' | 'Warning' | 'Blocker'; label: string }[] = [
  { value: 'Info', label: 'Just show a note' },
  { value: 'Warning', label: 'Show a warning (recommended)' },
  { value: 'Blocker', label: 'Block signing until reviewed' },
];

const SEVERITY_BADGE: Record<string, string> = {
  Info: 'info',
  Warning: 'warn',
  Blocker: 'danger',
};

const PLAN_LABEL_FRIENDLY: Record<number, string> = {
  0: 'Free trial',
  1: 'Team',
  2: 'Enterprise',
};

export default function SettingsPage() {
  const session = useAuthSession();
  const [s, setS] = useState<Settings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [authBlocked, setAuthBlocked] = useState(false);
  const [info, setInfo] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (session.loading || session.signedOut) return;
    api.tenant.settings.get().then((next) => {
      setS(next);
      setAuthBlocked(false);
    }).catch((e: Error & { status?: number }) => {
      if (isAuthError(e)) {
        setAuthBlocked(true);
      } else {
        setError(e.message);
      }
    });
  }, [session.loading, session.signedOut]);

  const [ingestSecret, setIngestSecret] = useState('');
  const [dicomBearer, setDicomBearer] = useState('');
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
      setInfo('Your changes were saved.');
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
          ? `Encryption key is working. Last checked: ${new Date(result.lastVerifiedAt!).toLocaleString()}.`
          : 'We could not confirm the encryption key. Please ask your IT team to double-check the key reference.',
      );
    } catch (e) {
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

  if (session.signedOut) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Workspace settings</h1>
        <SignInRequired surface="Please sign in to manage your workspace settings." />
      </div>
    );
  }

  if (authBlocked) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Workspace settings</h1>
        <SignInRequired
          surface="You don't have access to workspace settings."
          detail="Ask your Medical Director, Reporting Admin, or IT Admin to update these settings, or to give you access."
        />
      </div>
    );
  }

  if (!s) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Workspace settings</h1>
        {error && <div className="banner warn">{error}</div>}
        {!error && <p className="rp-page-sub">Loading…</p>}
      </div>
    );
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Workspace settings</h1>
          <p className="rp-page-sub">
            Controls that apply to everyone in your workspace — AI safety,
            your subscription, and connections to your hospital systems.
          </p>
        </div>
      </header>

      {error && <div className="banner warn">{error}</div>}
      {info && <div className="banner ok">{info}</div>}

      <div className="rp-page-grid">
        <div className="rp-page-main">
          {/* ---------- AI safety ---------- */}
          <div className="rp-panel">
            <div className="rp-panel-title">AI safety check</div>
            <p className="rp-page-sub">
              When you ask the AI to draft an Impression, RadioPad re-reads it
              and flags any sentence that doesn&apos;t look like it&apos;s supported by
              the Findings or the study. You decide whether to keep, edit, or
              remove the flagged sentence — RadioPad never changes your report.
            </p>

            <label className="rp-field rp-row">
              <input
                type="checkbox"
                checked={s.hallucinationDetectionEnabled}
                onChange={(e) => setS({ ...s, hallucinationDetectionEnabled: e.target.checked })}
              />
              <span style={{ marginLeft: 8 }}>Turn on the AI safety check</span>
            </label>

            <label className="rp-field">
              <span>
                How strict should the safety check be?{' '}
                <span className={`badge ${SEVERITY_BADGE[s.hallucinationSeverity] ?? ''}`}>
                  {SEVERITY_OPTIONS.find((o) => o.value === s.hallucinationSeverity)?.label ?? s.hallucinationSeverity}
                </span>
              </span>
              <select
                className="rp-input"
                value={s.hallucinationSeverity}
                onChange={(e) =>
                  setS({ ...s, hallucinationSeverity: e.target.value as Settings['hallucinationSeverity'] })
                }
              >
                {SEVERITY_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="rp-field">
              <span>Phrases that should never be flagged (one per line)</span>
              <textarea
                className="rp-input"
                rows={4}
                value={s.hallucinationAllowList}
                onChange={(e) => setS({ ...s, hallucinationAllowList: e.target.value })}
                placeholder={'incidental finding\nrecommend follow-up\nclinical correlation advised'}
              />
            </label>

            <details className="rp-advanced">
              <summary>Show advanced safety options</summary>
              <label className="rp-field">
                <span>Sensitivity (0.00 – 1.00) — current: <code>{s.hallucinationMinSupport.toFixed(2)}</code></span>
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
                <span className="rp-page-sub" style={{ marginTop: 4 }}>
                  Lower numbers flag more sentences. Most workspaces leave this at 0.30.
                </span>
              </label>
            </details>
          </div>

          {/* ---------- Subscription ---------- */}
          <div className="rp-panel">
            <div className="rp-panel-title">Your subscription</div>
            <p className="rp-page-sub">
              You&apos;re currently on the{' '}
              <span className="badge info">{PLAN_LABEL_FRIENDLY[s.plan] ?? PLAN_LABELS[s.plan]}</span>{' '}
              plan
              {s.stripe.currentPeriodEnd && (
                <> · renews on <strong>{new Date(s.stripe.currentPeriodEnd).toLocaleDateString()}</strong></>
              )}
              .
            </p>

            <div className="rp-row" style={{ gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
              <button
                className="primary-ghost"
                onClick={() => checkout(publicEnv('NEXT_PUBLIC_STRIPE_PRICE_TEAM') ?? 'price_team_placeholder')}
              >
                Upgrade to Team
              </button>
              <button
                className="primary-ghost"
                onClick={() => checkout(publicEnv('NEXT_PUBLIC_STRIPE_PRICE_ENTERPRISE') ?? 'price_enterprise_placeholder')}
              >
                Upgrade to Enterprise
              </button>
              <button className="ghost" onClick={openPortal} disabled={!s.stripe.customerId}>
                Manage billing &amp; invoices
              </button>
            </div>

            <details className="rp-advanced">
              <summary>Change plan manually (admin)</summary>
              <label className="rp-field">
                <span>Set plan directly — your billing webhook will override this if there&apos;s a mismatch.</span>
                <select
                  className="rp-input"
                  value={s.plan}
                  onChange={(e) => setS({ ...s, plan: Number(e.target.value) as Settings['plan'] })}
                >
                  <option value={0}>Free trial</option>
                  <option value={1}>Team</option>
                  <option value={2}>Enterprise</option>
                </select>
              </label>
            </details>
          </div>

          {/* ---------- Hospital connections ---------- */}
          <div className="rp-panel">
            <div className="rp-panel-title">Hospital connections</div>
            <p className="rp-page-sub">
              Connect RadioPad to your hospital&apos;s ordering system and imaging
              archive so studies and orders flow in automatically. Your IT
              team usually sets these up. Passwords are stored securely and
              never shown again — leave a field blank to keep the existing
              value.
            </p>

            <label className="rp-field">
              <span>
                Connection password — orders &amp; reports{' '}
                <span className={`badge ${s.ingest.bearerConfigured ? 'ok' : 'warn'}`}>
                  {s.ingest.bearerConfigured ? 'connected' : 'not connected'}
                </span>
              </span>
              <input
                className="rp-input"
                type="password"
                value={ingestSecret}
                onChange={(e) => setIngestSecret(e.target.value)}
                placeholder={s.ingest.bearerConfigured ? '(unchanged — leave blank to keep)' : 'Paste the connection password from your IT team'}
                autoComplete="new-password"
              />
            </label>

            <details className="rp-advanced">
              <summary>Show imaging archive settings</summary>

              <label className="rp-field">
                <span>Imaging archive address</span>
                <input
                  className="rp-input"
                  value={s.dicomWeb.baseUrl}
                  onChange={(e) => setS({ ...s, dicomWeb: { ...s.dicomWeb, baseUrl: e.target.value } })}
                  placeholder="https://pacs.your-hospital.org/dicom-web"
                />
                <span className="rp-page-sub" style={{ marginTop: 4 }}>
                  Where RadioPad should look up the imaging studies attached to your reports.
                </span>
              </label>

              <label className="rp-field">
                <span>
                  Imaging archive password{' '}
                  <span className={`badge ${s.dicomWeb.bearerConfigured ? 'ok' : 'info'}`}>
                    {s.dicomWeb.bearerConfigured ? 'connected' : 'optional'}
                  </span>
                </span>
                <input
                  className="rp-input"
                  type="password"
                  value={dicomBearer}
                  onChange={(e) => setDicomBearer(e.target.value)}
                  placeholder={s.dicomWeb.bearerConfigured ? '(unchanged — leave blank to keep)' : 'Only needed if your imaging archive requires one'}
                  autoComplete="new-password"
                />
              </label>
            </details>
          </div>

          {/* ---------- Encryption key (advanced, optional) ---------- */}
          <details className="rp-panel" open={!!s.cmk?.configured}>
            <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>
              Encryption key (optional, for compliance teams)
            </summary>
            <p className="rp-page-sub" style={{ marginTop: 8 }}>
              Use your own encryption key so RadioPad can&apos;t read tenant data
              without it. Most workspaces leave this off — turn it on only if
              your compliance team asks you to. Your IT team will give you a
              key reference to paste below.
            </p>

            <label className="rp-field">
              <span>
                Encryption key reference{' '}
                {s.cmk?.configured ? (
                  <span className="badge ok">configured</span>
                ) : (
                  <span className="badge warn">not configured</span>
                )}
              </span>
              <input
                className="rp-input"
                value={cmkKeyRef}
                onChange={(e) => setCmkKeyRef(e.target.value)}
                placeholder={s.cmk?.keyRef ?? 'Paste the key reference your IT team provided'}
                autoComplete="off"
              />
            </label>

            <div className="rp-row" style={{ gap: 8, alignItems: 'center', marginTop: 8, flexWrap: 'wrap' }}>
              <button
                className="primary-ghost"
                onClick={verifyKms}
                disabled={verifying || !s.cmk?.configured}
              >
                {verifying ? 'Checking…' : 'Test the key'}
              </button>
              <span className="rp-page-sub">
                Last checked:{' '}
                {s.cmk?.lastVerifiedAt
                  ? new Date(s.cmk.lastVerifiedAt).toLocaleString()
                  : <span className="badge warn">never</span>}
              </span>
            </div>
          </details>

          {/* ---------- Developer features (very advanced) ---------- */}
          <details className="rp-panel">
            <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>
              Developer features
            </summary>
            <p className="rp-page-sub" style={{ marginTop: 8 }}>
              Edit raw feature configuration. Only change this if you&apos;ve been
              instructed to by RadioPad support.
            </p>
            <textarea
              className="rp-input"
              rows={6}
              spellCheck={false}
              value={s.featureFlagsJson}
              onChange={(e) => setS({ ...s, featureFlagsJson: e.target.value })}
              style={{ fontFamily: 'var(--mono)', fontSize: 12 }}
            />
          </details>

          <div className="rp-row" style={{ justifyContent: 'flex-end', marginTop: 16 }}>
            <button className="primary" onClick={save} disabled={saving}>
              {saving ? 'Saving…' : 'Save changes'}
            </button>
          </div>
        </div>

        {/* ---------- Help sidecar ---------- */}
        <aside className="rp-page-aside" aria-label="What these settings do">
          <div className="rp-help">
            <h3 className="rp-help-title">What you control here</h3>
            <p>
              These settings affect everyone in your workspace. Changes take
              effect the moment you save them.
            </p>
            <ul>
              <li><strong>AI safety check</strong> — extra review on AI-drafted Impressions.</li>
              <li><strong>Subscription</strong> — your plan and billing.</li>
              <li><strong>Hospital connections</strong> — links to your orders &amp; imaging systems.</li>
              <li><strong>Encryption key</strong> — bring-your-own-key (optional).</li>
            </ul>
          </div>

          <div className="rp-help">
            <h3 className="rp-help-title">Need help?</h3>
            <p>
              You don&apos;t have to figure this out alone. Ask your IT team for
              the hospital connection details, or contact RadioPad support and
              we&apos;ll walk you through it.
            </p>
          </div>

          <div className="rp-help">
            <h3 className="rp-help-title">Privacy &amp; safety</h3>
            <p>
              RadioPad never auto-signs reports — you always sign. AI text is
              clearly marked until you approve it. Patient information stays
              inside the connections you approve here.
            </p>
          </div>
        </aside>
      </div>
    </div>
  );
}
