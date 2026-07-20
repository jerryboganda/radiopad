'use client';

// PRD WL-009 — multi-tenant worklist switcher for teleradiologists.
//
// Renders nothing when the signed-in identity belongs to a single practice
// (the overwhelmingly common case), so it costs a hidden fetch and no chrome.
// Switching is a real re-authentication server-side: POST /api/tenant/switch
// mints a bearer bound to the target practice, we store it, and the app
// reloads so every cached tenant-scoped view is rebuilt from scratch rather
// than half-swapped.

import { useCallback, useEffect, useState } from 'react';
import { Building2 } from 'lucide-react';
import { api, setActiveAuthToken, type TenantMembership } from '@/lib/api';

export default function TenantSwitcher() {
  const [rows, setRows] = useState<TenantMembership[]>([]);
  const [switching, setSwitching] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.tenant
      .memberships()
      .then((list) => {
        if (!cancelled) setRows(list);
      })
      .catch(() => {
        // A workspace that predates enterprise identity has no memberships
        // endpoint data to show; the switcher simply stays hidden.
        if (!cancelled) setRows([]);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const switchTo = useCallback(async (slug: string) => {
    setSwitching(slug);
    setError(null);
    try {
      const res = await api.tenant.switch(slug);
      setActiveAuthToken(res.token);
      try {
        window.localStorage.setItem('radiopad.tenant', res.tenant);
        window.localStorage.setItem('radiopad.user', res.user);
      } catch {
        /* storage unavailable — the session cookie still carries the switch */
      }
      // Full reload: every list, cache and open panel belongs to the practice
      // we just left. Rebuilding beats trying to invalidate them one by one.
      window.location.assign('/worklist');
    } catch (e) {
      setError((e as Error).message);
      setSwitching(null);
    }
  }, []);

  if (rows.length < 2) return null;

  return (
    <div className="rp-panel" style={{ marginBottom: 12 }}>
      <div className="rp-row" style={{ gap: 8, alignItems: 'baseline' }}>
        <Building2 size={15} strokeWidth={1.8} aria-hidden />
        <div className="rp-panel-title" style={{ marginBottom: 0 }}>Reading for</div>
      </div>
      <div className="rp-row" style={{ gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
        {rows.map((m) => (
          <button
            key={m.slug}
            type="button"
            className={m.isCurrent ? 'primary-ghost' : 'ghost'}
            aria-pressed={m.isCurrent}
            disabled={m.isCurrent || switching !== null}
            onClick={() => void switchTo(m.slug)}
            title={m.isCurrent ? 'Current workspace' : `Switch to ${m.displayName} (${m.role})`}
          >
            {switching === m.slug && <span className="rp-spinner sm" aria-hidden />}
            {m.displayName}
          </button>
        ))}
      </div>
      {error && (
        <p className="text-warning" role="alert" style={{ marginTop: 8, fontSize: 13 }}>
          Couldn&apos;t switch workspace: {error}
        </p>
      )}
    </div>
  );
}
