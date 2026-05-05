'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, publicEnv, type BillingInvoice, type BillingStatus, type BillingCredits } from '@/lib/api';

type AnalyticsSummary = Awaited<ReturnType<typeof api.analytics.summary>>;
type FeatureMap = Awaited<ReturnType<typeof api.billing.features>>;

const PLAN_BADGE: Record<BillingStatus['plan'], string> = {
  Trial: 'warn',
  Team: 'info',
  Enterprise: 'ok',
};

const PLAN_LABELS: Record<BillingStatus['plan'], string> = {
  Trial: 'Trial',
  Team: 'Team',
  Enterprise: 'Enterprise',
};

const INVOICE_BADGE: Record<string, string> = {
  paid: 'ok',
  open: 'info',
  void: 'danger',
  uncollectible: 'danger',
  draft: 'info',
};

function fmtCents(amount: number, currency: string): string {
  const code = (currency || 'USD').toUpperCase();
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: code }).format(amount / 100);
  } catch {
    return `${(amount / 100).toFixed(2)} ${code}`;
  }
}

function fmtDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleDateString();
  } catch {
    return iso;
  }
}

function daysUntil(iso: string): number {
  return Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000));
}

export default function BillingDashboardPage() {
  const [status, setStatus] = useState<BillingStatus | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [statusUnavailable, setStatusUnavailable] = useState(false);
  const [features, setFeatures] = useState<FeatureMap | null>(null);
  const [usage, setUsage] = useState<AnalyticsSummary | null>(null);
  const [usageError, setUsageError] = useState<string | null>(null);
  const [invoices, setInvoices] = useState<BillingInvoice[]>([]);
  const [invoicesError, setInvoicesError] = useState<string | null>(null);
  const [credits, setCredits] = useState<BillingCredits | null>(null);
  const [creditsError, setCreditsError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Bulk-export panel state.
  const today = new Date().toISOString().slice(0, 10);
  const monthAgo = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);
  const [bulkFrom, setBulkFrom] = useState(monthAgo);
  const [bulkTo, setBulkTo] = useState(today);
  const [bulkFormat, setBulkFormat] = useState<'csv' | 'zip'>('csv');
  const [bulkBusy, setBulkBusy] = useState(false);

  useEffect(() => {
    api.billing.status()
      .then((s) => { setStatus(s); setStatusUnavailable(false); })
      .catch((e: Error & { status?: number }) => {
        if (e.status === 503) setStatusUnavailable(true);
        else setStatusError(e.message);
      });
    api.billing.features().then(setFeatures).catch((e: Error) => setStatusError(e.message));
    api.analytics.summary().then(setUsage).catch((e: Error) => setUsageError(e.message));
    api.billing.invoices().then(setInvoices).catch((e: Error & { status?: number }) => {
      if (e.status === 503) setInvoicesError('Stripe is not configured for this tenant.');
      else setInvoicesError(e.message);
    });
    api.billing.credits().then(setCredits).catch((e: Error) => setCreditsError(e.message));
  }, []);

  async function openPortal() {
    try {
      const { url } = await api.billing.portal(window.location.href);
      window.location.href = url;
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function upgradeTeam() {
    try {
      const { url } = await api.billing.checkout(
        publicEnv('NEXT_PUBLIC_STRIPE_PRICE_TEAM') ?? 'price_team_placeholder',
        window.location.origin + '/admin/billing?billing=success',
        window.location.origin + '/admin/billing?billing=cancelled',
      );
      window.location.href = url;
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function runBulkExport() {
    setError(null);
    if (!bulkFrom || !bulkTo) {
      setError('Pick both a start and end date.');
      return;
    }
    if (bulkFrom > bulkTo) {
      setError('Start date must precede end date.');
      return;
    }
    setBulkBusy(true);
    try {
      const blob = await api.billing.bulkExport({ from: bulkFrom, to: bulkTo, format: bulkFormat });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `radiopad-invoices-${bulkFrom}_${bulkTo}.${bulkFormat}`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBulkBusy(false);
    }
  }

  if (statusUnavailable) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Billing</h1>
        <div className="rp-panel">
          <div className="rp-panel-title">Billing not configured</div>
          <p className="rp-page-sub">
            Billing is not yet configured for this tenant. Contact your
            administrator to enable Stripe integration.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Billing</h1>
      <p className="rp-page-sub">
        Plan, invoices, usage, and feature entitlements for the current tenant.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {statusError && <div className="banner warn">{statusError}</div>}

      {/* 1. Plan & status ------------------------------------------ */}
      <section className="rp-panel">
        <div className="rp-panel-title">Plan &amp; status</div>
        {!status && !statusError && <p className="rp-page-sub">Loading…</p>}
        {status && (
          <>
            <div className="rp-row rp-row-wrap rp-gap-sm rp-mb-md">
              <span className={`badge ${PLAN_BADGE[status.plan] ?? 'info'}`}>
                {PLAN_LABELS[status.plan] ?? `Plan ${status.plan}`}
              </span>
              {status.subscriptionStatus && (
                <span className="badge">Stripe: <code>{status.subscriptionStatus}</code></span>
              )}
              {status.currentPeriodEnd && (
                <span className="badge info">Renews {fmtDate(status.currentPeriodEnd)}</span>
              )}
              {status.trialEndsAt && daysUntil(status.trialEndsAt) > 0 && (
                <span className="badge warn">
                  Trial ends in {daysUntil(status.trialEndsAt)} day{daysUntil(status.trialEndsAt) === 1 ? '' : 's'}
                </span>
              )}
            </div>

            {status.gracePeriodUntil && (
              <div className="banner warn">
                Payment overdue. Grace period ends{' '}
                <code>{fmtDate(status.gracePeriodUntil)}</code> ({daysUntil(status.gracePeriodUntil)} days remaining).
              </div>
            )}
            {status.suspendedAt && (
              <div className="banner danger">
                Tenant suspended on <code>{fmtDate(status.suspendedAt)}</code>.
                Resolve outstanding balance to restore access.
              </div>
            )}
            {!status.customerConfigured && (
              <p className="rp-page-sub">
                No Stripe customer is linked yet. Start a checkout below to
                provision one automatically.
              </p>
            )}

            <div className="rp-toolbar rp-mt-sm">
              <button
                className="primary"
                onClick={openPortal}
                disabled={!status.customerConfigured}
              >
                Manage in Stripe
              </button>
              <button className="primary-ghost" onClick={upgradeTeam}>
                Upgrade plan
              </button>
            </div>
          </>
        )}
      </section>

      {/* 2. AI credits this period (BILL-002) ----------------- */}
      <section className="rp-panel">
        <div className="rp-panel-title">AI credits</div>
        {creditsError && <div className="banner warn">{creditsError}</div>}
        {!credits && !creditsError && <p className="rp-page-sub">Loading…</p>}
        {credits && (
          <>
            <p className="rp-page-sub">
              Period <code>{fmtDate(credits.periodStart)}</code> – <code>{fmtDate(credits.periodEnd)}</code>{' '}
              · Plan <span className="badge info">{credits.plan}</span>
            </p>
            <div className="rp-grid-3">
              {(['calls', 'inputTokens', 'outputTokens'] as const).map((k) => {
                const labels = { calls: 'AI calls', inputTokens: 'Input tokens', outputTokens: 'Output tokens' } as const;
                const used = credits.used[k];
                const limit = credits.limits[k];
                const remaining = credits.remaining[k];
                const ratio = limit > 0 ? used / limit : 0;
                const tone = ratio >= 1 ? 'danger' : ratio >= 0.9 ? 'warn' : 'ok';
                const pct = Math.min(100, Math.round(ratio * 100));
                return (
                  <div key={k} className="rp-stat-tile">
                    <div className="rp-stat-tile-row">
                      <span className="rp-stat-label">{labels[k]}</span>
                      <span className={`badge ${tone}`}>{pct}%</span>
                    </div>
                    <div className="rp-stat-value">{used.toLocaleString()}</div>
                    <div className="rp-stat-sub">
                      of {limit.toLocaleString()} · {remaining.toLocaleString()} remaining
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
      </section>

      {/* 3. Trial countdown (BILL-007) ----------------------- */}
      {credits && credits.plan === 'Trial' && credits.trialEndsAt && (
        <section className="rp-panel">
          <div className="rp-panel-title">Trial</div>
          {(() => {
            const days = daysUntil(credits.trialEndsAt!);
            if (days <= 3) {
              return (
                <div className="rp-banner warn">
                  Trial ends in {days} day{days === 1 ? '' : 's'} (
                  <code>{fmtDate(credits.trialEndsAt)}</code>). Upgrade to keep AI features
                  enabled past the trial period.
                </div>
              );
            }
            return (
              <p className="rp-page-sub">
                Trial active — <strong>{days}</strong> days remaining (ends{' '}
                <code>{fmtDate(credits.trialEndsAt)}</code>).
              </p>
            );
          })()}
        </section>
      )}

      {/* 4. Usage this month --------------------------------------- */}
      <section className="rp-panel">
        <div className="rp-panel-title">Usage this month</div>
        {usageError && <div className="banner warn">{usageError}</div>}
        {!usage && !usageError && <p className="rp-page-sub">Loading…</p>}
        {usage && (
          <>
            <div className="rp-grid-3 rp-mb-md">
              <div>
                <div className="rp-stat-label">AI calls</div>
                <div className="rp-stat-value">{usage.ai.totalRequests.toLocaleString()}</div>
              </div>
              <div>
                <div className="rp-stat-label">Blocked / errors</div>
                <div className="rp-stat-value">
                  {usage.ai.blockedCount.toLocaleString()} / {usage.ai.errorCount.toLocaleString()}
                </div>
              </div>
              <div>
                <div className="rp-stat-label">Avg latency</div>
                <div className="rp-stat-value">
                  {Math.round(usage.ai.avgLatencyMs).toLocaleString()} ms
                </div>
              </div>
            </div>

            <ul className="rp-list">
              <li className="rp-row between rp-divider-row">
                <span className="rp-stat-label rp-cell f2">Provider</span>
                <span className="rp-stat-label rp-cell f1 r">Calls</span>
                <span className="rp-stat-label rp-cell f1 r">In tokens</span>
                <span className="rp-stat-label rp-cell f1 r">Out tokens</span>
              </li>
              {usage.ai.byProvider.length === 0 && (
                <li className="rp-page-sub rp-divider-row">No provider activity in the current window.</li>
              )}
              {usage.ai.byProvider.map((p) => (
                <li
                  key={`${p.provider}:${p.adapter}`}
                  className="rp-row between rp-divider-row"
                >
                  <span className="rp-cell f2">
                    {p.provider} <code>{p.adapter}</code>
                  </span>
                  <span className="rp-cell f1 r">{p.requests.toLocaleString()}</span>
                  <span className="rp-cell f1 r">{p.inputTokens.toLocaleString()}</span>
                  <span className="rp-cell f1 r">{p.outputTokens.toLocaleString()}</span>
                </li>
              ))}
            </ul>
          </>
        )}
      </section>

      {/* 3. Invoices ------------------------------------------------ */}
      <section className="rp-panel">
        <div className="rp-panel-title">Invoices</div>
        {invoicesError && <div className="banner warn">{invoicesError}</div>}
        {!invoicesError && invoices.length === 0 && (
          <p className="rp-page-sub">No invoices issued yet.</p>
        )}
        {invoices.length > 0 && (
          <ul className="rp-list">
            <li className="rp-row between rp-divider-row">
              <span className="rp-stat-label rp-cell f1">Period</span>
              <span className="rp-stat-label rp-cell f1">Number</span>
              <span className="rp-stat-label rp-cell f1">Status</span>
              <span className="rp-stat-label rp-cell f1 r">Paid</span>
              <span className="rp-stat-label rp-cell f1 r">Actions</span>
            </li>
            {invoices.slice(0, 20).map((inv) => (
              <li
                key={inv.id}
                className="rp-row between rp-divider-row"
              >
                <span className="rp-cell f1">{fmtDate(inv.periodStart)}</span>
                <span className="rp-cell f1">
                  <code>{inv.number ?? inv.id}</code>
                </span>
                <span className="rp-cell f1">
                  <span className={`badge ${INVOICE_BADGE[inv.status] ?? ''}`}>{inv.status}</span>
                </span>
                <span className="rp-cell f1 r">{fmtCents(inv.amountPaid, inv.currency)}</span>
                <span className="rp-cell f1 r rp-actions">
                  {inv.hostedInvoiceUrl && (
                    <a className="rp-subtle-link" href={inv.hostedInvoiceUrl} target="_blank" rel="noreferrer">View</a>
                  )}
                  {inv.invoicePdf && (
                    <a className="rp-subtle-link" href={inv.invoicePdf} target="_blank" rel="noreferrer">PDF</a>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* 4. Plan-feature flags ------------------------------------- */}
      <section className="rp-panel">
        <div className="rp-panel-title">Plan features</div>
        {!features && <p className="rp-page-sub">Loading…</p>}
        {features && (
          <>
            <p className="rp-page-sub">
              Current plan: <span className="badge info">{features.plan}</span>
            </p>
            <div className="rp-grid-3">
              {Object.entries(features.features).map(([name, enabled]) => (
                <div
                  key={name}
                  className="rp-row between rp-divider-row"
                >
                  <span><code>{name}</code></span>
                  {enabled ? (
                    <span className="badge ok">Enabled</span>
                  ) : (
                    <span className="badge">
                      Locked · <Link href="/admin/billing" onClick={(e) => { e.preventDefault(); upgradeTeam(); }}>Upgrade</Link>
                    </span>
                  )}
                </div>
              ))}
            </div>
          </>
        )}
      </section>

      {/* 5. Bulk export ------------------------------------------- */}
      <section className="rp-panel">
        <div className="rp-panel-title">Bulk export</div>
        <p className="rp-page-sub">
          Export invoices in the selected date range as a CSV summary or a ZIP
          archive of the underlying PDF documents.
        </p>
        <div className="rp-grid-3 rp-mb-md">
          <div className="section-block">
            <label htmlFor="bulk-from">From</label>
            <input
              id="bulk-from"
              className="rp-input"
              type="date"
              value={bulkFrom}
              onChange={(e) => setBulkFrom(e.target.value)}
              max={bulkTo || undefined}
            />
          </div>
          <div className="section-block">
            <label htmlFor="bulk-to">To</label>
            <input
              id="bulk-to"
              className="rp-input"
              type="date"
              value={bulkTo}
              onChange={(e) => setBulkTo(e.target.value)}
              min={bulkFrom || undefined}
            />
          </div>
          <div className="section-block">
            <label htmlFor="bulk-format">Format</label>
            <select
              id="bulk-format"
              className="rp-input"
              value={bulkFormat}
              onChange={(e) => setBulkFormat(e.target.value as 'csv' | 'zip')}
            >
              <option value="csv">CSV summary</option>
              <option value="zip">ZIP of PDFs</option>
            </select>
          </div>
        </div>
        <div className="rp-toolbar">
          <button
            className="primary-ghost"
            disabled={bulkBusy || !bulkFrom || !bulkTo}
            onClick={runBulkExport}
          >
            {bulkBusy ? 'Exporting…' : 'Export'}
          </button>
        </div>
      </section>
    </div>
  );
}