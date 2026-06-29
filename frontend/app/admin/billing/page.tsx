'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, publicEnv, type BillingInvoice, type BillingStatus, type BillingCredits } from '@/lib/api';
import { isAuthError, useAuthSession } from '@/lib/useAuthSession';
import SignInRequired from '@/components/ui/SignInRequired';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';
import EmptyState from '@/components/ui/EmptyState';
import AnimatedNumber from '@/components/ui/AnimatedNumber';
import Skeleton from '@/components/ui/Skeleton';

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
  const session = useAuthSession();
  const [status, setStatus] = useState<BillingStatus | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [statusUnavailable, setStatusUnavailable] = useState(false);
  const [authBlocked, setAuthBlocked] = useState(false);
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
    if (session.loading || session.signedOut) return;
    api.billing.status()
      .then((s) => { setStatus(s); setStatusUnavailable(false); })
      .catch((e: Error & { status?: number }) => {
        if (isAuthError(e)) { setAuthBlocked(true); return; }
        if (e.status === 503) setStatusUnavailable(true);
        else setStatusError(e.message);
      });
    api.billing.features().then(setFeatures).catch((e: Error & { status?: number }) => {
      if (isAuthError(e)) setAuthBlocked(true);
      else setStatusError(e.message);
    });
    api.analytics.summary().then(setUsage).catch((e: Error & { status?: number }) => {
      if (isAuthError(e)) setUsageError('Insufficient permissions for usage analytics.');
      else setUsageError(e.message);
    });
    api.billing.invoices().then(setInvoices).catch((e: Error & { status?: number }) => {
      if (e.status === 503) setInvoicesError('Stripe is not configured for this tenant.');
      else if (isAuthError(e)) setInvoicesError('Invoices require the Billing Admin, IT Admin, or Medical Director role.');
      else setInvoicesError(e.message);
    });
    api.billing.credits().then(setCredits).catch((e: Error & { status?: number }) => {
      if (isAuthError(e)) setCreditsError('Insufficient permissions for AI credit usage.');
      else setCreditsError(e.message);
    });
  }, [session.loading, session.signedOut]);

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

  if (session.signedOut) {
    return (
      <Container>
        <h1 className="rp-page-title">Billing &amp; plan</h1>
        <SignInRequired surface="Please sign in to view your workspace billing." />
      </Container>
    );
  }

  if (authBlocked) {
    return (
      <Container>
        <h1 className="rp-page-title">Billing &amp; plan</h1>
        <SignInRequired
          surface="You don't have access to billing for this workspace."
          detail="Ask your Billing Admin, IT Admin, or Medical Director to give you access — or to handle this for you."
        />
      </Container>
    );
  }

  if (statusUnavailable) {
    return (
      <Container>
        <h1 className="rp-page-title">Billing &amp; plan</h1>
        <div className="rp-panel rp-anim-fade-in-up">
          <EmptyState
            title="Billing isn't set up yet"
            description="Billing hasn't been turned on for this workspace yet. Ask your workspace admin to finish setup."
          />
        </div>
      </Container>
    );
  }

  return (
    <Container>
      <PageHeader
        title="Billing & plan"
        description="Your current plan, this month's AI usage, invoices, and what each plan unlocks."
      />

      <div aria-live="polite">
        {error && (
          <Banner tone="warn" title="Something went wrong" onDismiss={() => setError(null)}>
            {error}
          </Banner>
        )}
        {statusError && (
          <Banner tone="warn" title="Couldn't load your plan">{statusError}</Banner>
        )}
      </div>

      <div className="rp-page-grid">
        <div className="rp-page-main rp-stagger">

      {/* 1. Plan & status ------------------------------------------ */}
      <section className="rp-panel rp-anim-fade-in-up" aria-busy={!status && !statusError}>
        <div className="rp-panel-title">Your plan</div>
        {!status && !statusError && <Skeleton variant="block" height={72} />}
        {status && (
          <>
            <div className="rp-row rp-row-wrap rp-gap-sm rp-mb-md">
              <span className={`badge ${PLAN_BADGE[status.plan] ?? 'info'}`}>
                {PLAN_LABELS[status.plan] ?? `Plan ${status.plan}`}
              </span>
              {status.subscriptionStatus && (
                <span className="badge">Status: {status.subscriptionStatus}</span>
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
              <Banner tone="warn" title="Payment overdue">
                You have until {fmtDate(status.gracePeriodUntil)} ({daysUntil(status.gracePeriodUntil)} days) before AI features pause.
              </Banner>
            )}
            {status.suspendedAt && (
              <Banner tone="danger" title="Workspace suspended">
                This workspace was suspended on {fmtDate(status.suspendedAt)}. Pay the outstanding invoice to restore access.
              </Banner>
            )}
            {!status.customerConfigured && (
              <p className="rp-page-sub">
                You haven&apos;t added a payment method yet. Click &ldquo;Upgrade&rdquo; below — we&apos;ll set everything up automatically.
              </p>
            )}

            <div className="rp-toolbar rp-mt-sm">
              <button
                className="primary"
                onClick={openPortal}
                disabled={!status.customerConfigured}
              >
                Manage billing &amp; invoices
              </button>
              <button className="primary-ghost" onClick={upgradeTeam}>
                Upgrade plan
              </button>
            </div>
          </>
        )}
      </section>

      {/* 2. AI credits this period (BILL-002) ----------------- */}
      <section className="rp-panel rp-anim-fade-in-up" aria-busy={!credits && !creditsError}>
        <div className="rp-panel-title">AI credit usage this period</div>
        {creditsError && <Banner tone="warn">{creditsError}</Banner>}
        {!credits && !creditsError && <Skeleton variant="block" height={120} />}
        {credits && (
          <>
            <p className="rp-page-sub">
              {fmtDate(credits.periodStart)} – {fmtDate(credits.periodEnd)}{' '}
              · Plan <span className="badge info">{credits.plan}</span>
            </p>
            <div className="metric-grid rp-stagger">
              {(['calls', 'inputTokens', 'outputTokens'] as const).map((k) => {
                const labels = { calls: 'AI requests', inputTokens: 'Words sent to AI', outputTokens: 'Words received from AI' } as const;
                const used = credits.used[k];
                const limit = credits.limits[k];
                const remaining = credits.remaining[k];
                const ratio = limit > 0 ? used / limit : 0;
                const tone = ratio >= 1 ? 'danger' : ratio >= 0.9 ? 'warn' : 'ok';
                const cardTone = ratio >= 1 ? 'blocked' : ratio >= 0.9 ? 'review' : 'ready';
                const pct = Math.min(100, Math.round(ratio * 100));
                return (
                  <div key={k} className="metric-card" data-tone={cardTone}>
                    <div className="rp-stat-tile-row">
                      <span className="metric-card-label">{labels[k]}</span>
                      <span className={`badge ${tone}`}>{pct}%</span>
                    </div>
                    <div className="metric-card-value">
                      <AnimatedNumber value={used} />
                    </div>
                    <div
                      style={{ height: 6, borderRadius: 3, overflow: 'hidden', background: 'var(--color-rule)' }}
                      role="progressbar"
                      aria-valuenow={pct}
                      aria-valuemin={0}
                      aria-valuemax={100}
                      aria-label={`${labels[k]} used`}
                    >
                      <div
                        className={`rp-bar-fill badge ${tone}`}
                        style={{ width: `${pct}%`, height: '100%', borderRadius: 0 }}
                      />
                    </div>
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
        <section className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Free trial</div>
          {(() => {
            const days = daysUntil(credits.trialEndsAt!);
            if (days <= 3) {
              return (
                <Banner tone="warn" title="Free trial ending soon">
                  Your free trial ends in {days} day{days === 1 ? '' : 's'} (on {fmtDate(credits.trialEndsAt)}). Upgrade now to keep using AI drafting after that.
                </Banner>
              );
            }
            return (
              <p className="rp-page-sub">
                Free trial active — <strong>{days}</strong> days remaining (ends {fmtDate(credits.trialEndsAt)}).
              </p>
            );
          })()}
        </section>
      )}

      {/* 4. Usage this month --------------------------------------- */}
      <section className="rp-panel rp-anim-fade-in-up" aria-busy={!usage && !usageError}>
        <div className="rp-panel-title">AI activity this month</div>
        {usageError && <Banner tone="warn">{usageError}</Banner>}
        {!usage && !usageError && <Skeleton variant="block" height={96} />}
        {usage && (
          <>
            <div className="metric-grid rp-mb-md rp-stagger">
              <div className="metric-card" data-tone="info">
                <div className="metric-card-label">AI requests</div>
                <div className="metric-card-value">
                  <AnimatedNumber value={usage.ai.totalRequests} />
                </div>
              </div>
              <div className="metric-card">
                <div className="metric-card-label">Blocked for safety / errors</div>
                <div className="metric-card-value">
                  {usage.ai.blockedCount.toLocaleString()} / {usage.ai.errorCount.toLocaleString()}
                </div>
              </div>
              <div className="metric-card">
                <div className="metric-card-label">Average speed</div>
                <div className="metric-card-value">
                  {Math.round(usage.ai.avgLatencyMs).toLocaleString()} ms
                </div>
              </div>
            </div>

            <details className="rp-advanced">
              <summary>Show usage by AI model</summary>
              <ul className="rp-list">
                <li className="rp-row between rp-divider-row">
                  <span className="rp-stat-label rp-cell f2">Model</span>
                  <span className="rp-stat-label rp-cell f1 r">Requests</span>
                  <span className="rp-stat-label rp-cell f1 r">Words in</span>
                  <span className="rp-stat-label rp-cell f1 r">Words out</span>
                </li>
                {usage.ai.byProvider.length === 0 && (
                  <li className="rp-page-sub rp-divider-row">No AI activity yet this period.</li>
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
            </details>
          </>
        )}
      </section>

      {/* 3. Invoices ------------------------------------------------ */}
      <section className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Invoices</div>
        {invoicesError && <Banner tone="warn">{invoicesError}</Banner>}
        {!invoicesError && invoices.length === 0 && (
          <EmptyState
            title="No invoices issued yet"
            description="Invoices will appear here once your first billing period closes."
          />
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
      <section className="rp-panel rp-anim-fade-in-up" aria-busy={!features}>
        <div className="rp-panel-title">What&apos;s included in your plan</div>
        {!features && <Skeleton variant="block" height={96} />}
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
                  <span>{friendlyFeatureName(name)}</span>
                  {enabled ? (
                    <span className="badge ok">Included</span>
                  ) : (
                    <span className="badge">
                      Not on this plan · <Link href="/admin/billing" onClick={(e) => { e.preventDefault(); upgradeTeam(); }}>Upgrade</Link>
                    </span>
                  )}
                </div>
              ))}
            </div>
          </>
        )}
      </section>

      {/* 5. Bulk export ------------------------------------------- */}
      <section className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Download invoices in bulk</div>
        <p className="rp-page-sub">
          Pick a date range to download all invoices from that period — as one
          spreadsheet or as a ZIP of PDFs.
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
              <option value="csv">Spreadsheet summary (CSV)</option>
              <option value="zip">All PDFs in a ZIP</option>
            </select>
          </div>
        </div>
        <div className="rp-toolbar">
          <button
            className="primary-ghost"
            disabled={bulkBusy || !bulkFrom || !bulkTo}
            onClick={runBulkExport}
            aria-busy={bulkBusy}
          >
            {bulkBusy && <span className="rp-spinner sm" aria-hidden />}
            {bulkBusy ? 'Preparing…' : 'Download'}
          </button>
        </div>
      </section>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What you control here</div>
            <p>Your plan, payment method, AI credit usage, and invoices.</p>
            <p>To change your plan, click <strong>Upgrade plan</strong> or <strong>Manage billing &amp; invoices</strong>. We&apos;ll take you to a secure checkout — no card details are stored in RadioPad.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Need help?</div>
            <p>Ask your Billing Admin or IT Admin to handle plan changes, or email <a href="mailto:support@radiopad.com">support@radiopad.com</a>.</p>
          </div>
        </aside>
      </div>
    </Container>
  );
}

const FEATURE_LABELS: Record<string, string> = {
  scim: 'Auto-add users from your identity provider',
  siemExport: 'Audit log export for security teams',
  marketplacePublish: 'Publish rulebooks to the marketplace',
  advancedAnalytics: 'Advanced analytics',
  stripeConnect: 'Marketplace payouts',
  customKms: 'Bring-your-own encryption key',
  ipAllowlist: 'IP address allowlist',
  priorCompare: 'Compare with prior report',
  voiceDictation: 'Voice dictation',
  mcpReadOnly: 'External AI tools (read-only)',
};

function friendlyFeatureName(flag: string): string {
  return FEATURE_LABELS[flag] ?? flag;
}