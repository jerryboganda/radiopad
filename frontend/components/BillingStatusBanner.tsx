'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { api, type BillingStatus } from '@/lib/api';

const REFRESH_MS = 5 * 60 * 1000;

function daysUntil(iso: string): number {
  const ms = new Date(iso).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / 86_400_000));
}

/**
 * Sticky notice rendered above the topbar when the tenant is in a Stripe
 * grace period or has been suspended. Polls every 5 minutes; failures are
 * swallowed (the dashboard surfaces detailed errors itself).
 */
export default function BillingStatusBanner() {
  const [status, setStatus] = useState<BillingStatus | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        const next = await api.billing.status();
        if (!cancelled) setStatus(next);
      } catch {
        if (!cancelled) setStatus(null);
      }
    }
    load();
    const id = setInterval(load, REFRESH_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  if (!status) return null;
  if (status.suspendedAt) {
    return (
      <div className="banner danger" role="alert">
        Billing suspended on{' '}
        <code>{new Date(status.suspendedAt).toLocaleDateString()}</code>. Some
        features are disabled.{' '}
        <Link href="/admin/billing">Resolve in billing →</Link>
      </div>
    );
  }
  if (status.gracePeriodUntil) {
    const days = daysUntil(status.gracePeriodUntil);
    return (
      <div className="banner warn" role="status">
        Payment overdue — grace period ends in {days} day{days === 1 ? '' : 's'}.{' '}
        <Link href="/admin/billing">Update billing →</Link>
      </div>
    );
  }
  return null;
}
