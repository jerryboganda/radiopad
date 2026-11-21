'use client';

// First-run checklist: an ordered list of links that walks a new user through
// setup, dismissed per device. Links only — no PHI, nothing fetched.
// ponytail: steps are not auto-ticked; add completion checks if users ask for them.
import { useEffect, useState } from 'react';
import Link from 'next/link';

export interface GettingStartedStep {
  href: string;
  title: string;
  detail: string;
}

export default function GettingStarted({
  id,
  title,
  steps,
}: {
  /** localStorage suffix, so desktop and web admin dismiss independently. */
  id: string;
  title: string;
  steps: GettingStartedStep[];
}) {
  const key = `rp-getting-started-${id}`;
  // Hidden until the stored choice is read, so a dismissed panel never flashes.
  const [show, setShow] = useState(false);
  useEffect(() => {
    try {
      setShow(localStorage.getItem(key) !== 'done');
    } catch {
      setShow(true);
    }
  }, [key]);
  if (!show) return null;

  return (
    <section className="rp-panel" aria-label={title} style={{ marginBottom: 16 }}>
      <div className="rp-row between">
        <div className="rp-panel-title">{title}</div>
        <button
          type="button"
          className="ghost"
          onClick={() => {
            try {
              localStorage.setItem(key, 'done');
            } catch {
              /* private mode: just hide for this visit */
            }
            setShow(false);
          }}
        >
          Dismiss
        </button>
      </div>
      <ol style={{ margin: '8px 0 0', paddingLeft: 20, display: 'grid', gap: 6, listStyle: 'decimal' }}>
        {steps.map((s) => (
          <li key={s.href}>
            <Link href={s.href} style={{ fontWeight: 600 }}>{s.title}</Link>
            <span className="rp-page-sub"> — {s.detail}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}
