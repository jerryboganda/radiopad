'use client';

import type { ReactNode } from 'react';

/**
 * Split-screen scaffold for the public auth entrance (sign-in / register /
 * device pairing). Left pane is a branded showcase (hidden < 880px); the right
 * pane hosts the focused auth card. Locked design tokens only — see
 * docs/02-design/design.md §"Auth entrance".
 */

export type AuthVariant = 'signin' | 'register' | 'pair';

const COPY: Record<AuthVariant, { headline: string; tagline: string }> = {
  signin: {
    headline: 'Sign in to your reporting workspace.',
    tagline: 'AI-assisted radiology reporting — draft, validate, and sign with confidence.',
  },
  register: {
    headline: 'Start reporting in minutes.',
    tagline: 'Create your organization and bring your team. There is no password to manage — ever.',
  },
  pair: {
    headline: 'Pair this device securely.',
    tagline: 'Bind this desktop to your tenant with a one-time code. The device never sees a password.',
  },
};

const FEATURES = [
  {
    title: 'Passwordless by design',
    sub: 'Magic links, SSO, and device pairing — no passwords to leak.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M7 11V8a5 5 0 0 1 10 0v3" />
        <rect x="5" y="11" width="14" height="9" rx="2" />
      </svg>
    ),
  },
  {
    title: 'Tenant-isolated',
    sub: "Every organization's data stays strictly separated.",
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
      </svg>
    ),
  },
  {
    title: 'Audit-logged, never auto-signed',
    sub: 'AI drafts and marks its text; a radiologist always signs.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M9 12l2 2 4-4" />
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
      </svg>
    ),
  },
];

function BrandMark() {
  return (
    <span className="brand-mark" aria-hidden>
      <span className="brand-mark-letter">R</span>
    </span>
  );
}

export default function AuthScaffold({
  variant,
  children,
}: {
  variant: AuthVariant;
  children: ReactNode;
}) {
  const copy = COPY[variant];
  return (
    <div className="rp-auth-split">
      <aside className="rp-auth-aside">
        <div className="rp-auth-aside-motif" aria-hidden />
        <div className="rp-auth-brand">
          <BrandMark />
          <span className="rp-auth-brand-name">RadioPad</span>
        </div>
        <div className="rp-auth-aside-body">
          <h2 className="rp-auth-headline">{copy.headline}</h2>
          <p className="rp-auth-tagline">{copy.tagline}</p>
          <ul className="rp-auth-features">
            {FEATURES.map((f) => (
              <li className="rp-auth-feature" key={f.title}>
                <span className="rp-auth-feature-icon">{f.icon}</span>
                <span className="rp-auth-feature-text">
                  <span className="rp-auth-feature-title">{f.title}</span>
                  <span className="rp-auth-feature-sub">{f.sub}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
        <p className="rp-auth-aside-foot">
          RadioPad never auto-signs reports. AI-generated text is always marked for review.
        </p>
      </aside>

      <main className="rp-auth-main">
        <div className="rp-auth-card">
          <div className="rp-auth-mobile-brand">
            <BrandMark />
            <span className="rp-auth-mobile-brand-name">RadioPad</span>
          </div>
          {children}
        </div>
      </main>
    </div>
  );
}
