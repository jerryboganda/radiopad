'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';

/**
 * PRD §16 Marketplace catalogue. Reads approved listings from the backend
 * and lets the radiologist click through to a Stripe Checkout (paid) or
 * grant flow (free). Submission / review surfaces ship later for publisher
 * + admin roles; this page is the buyer-side view.
 *
 * Locked tokens only: `.rp-panel`, `.rp-panel-title`, `.rp-grid-3`,
 * `.rp-list`, `.badge.ok/warn/info`, `.primary`, `.subtle`.
 */
type Listing = {
  id: string;
  name: string;
  description: string;
  kind: string;
  priceCents: number;
  reviewedAt: string;
};

export default function MarketplacePage() {
  const [items, setItems] = useState<Listing[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    api.marketplace.list().then(setItems).catch((e) => setError((e as Error).message));
  }, []);

  async function buy(id: string) {
    try {
      setBusy(id);
      const r = await api.marketplace.checkout(
        id,
        typeof window === 'undefined' ? '' : window.location.origin + '/marketplace',
      );
      if (r.url) window.location.assign(r.url);
      else if (r.granted) setError('Free asset granted. Refresh to use it.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="pane">
      <div className="panel">
        <div className="panel-header">
          <div>
            <h1 className="rp-page-title">Marketplace</h1>
            <p className="rp-page-sub">
              Community-published rulebooks, templates and prompt packs (PRD §16).
            </p>
          </div>
        </div>
        {error ? <div className="banner warn">{error}</div> : null}
        <div className="rp-grid-3">
          {items.map((item) => (
            <div key={item.id} className="rp-panel">
              <div className="rp-panel-title">
                {item.name} <span className="badge ok">{item.kind}</span>
              </div>
              <p className="rp-page-sub">{item.description}</p>
              <div className="rp-row">
                <strong>
                  {item.priceCents === 0
                    ? 'Free'
                    : `$${(item.priceCents / 100).toFixed(2)}`}
                </strong>
                <button
                  type="button"
                  className="primary"
                  disabled={busy === item.id}
                  onClick={() => buy(item.id)}
                >
                  {item.priceCents === 0 ? 'Install' : 'Buy'}
                </button>
              </div>
            </div>
          ))}
          {items.length === 0 && !error ? (
            <div className="rp-page-sub">No approved listings yet.</div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
