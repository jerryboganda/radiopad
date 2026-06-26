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
    title: 'AI-assisted drafting',
    sub: 'Turn your findings into a structured impression in seconds — every AI line is marked for your review.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M15 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h6" />
        <path d="M8 8h5M8 12h6M8 16h3" />
        <path d="M18 13l.9 2.1 2.1.9-2.1.9L18 19l-.9-2.1-2.1-.9 2.1-.9z" />
      </svg>
    ),
  },
  {
    title: 'Validation rulebooks',
    sub: 'Institution rulebooks catch laterality slips, contradictions, and missing sections before you sign.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
        <path d="M9 12l2 2 4-4" />
      </svg>
    ),
  },
  {
    title: 'Hands-free dictation',
    sub: 'Dictate naturally; on-device speech-to-text keeps your audio on the machine.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <rect x="9" y="3" width="6" height="11" rx="3" />
        <path d="M5 11a7 7 0 0 0 14 0" />
        <path d="M12 18v3" />
      </svg>
    ),
  },
  {
    title: 'Structured templates',
    sub: 'Start from study-specific templates — chest CT, cardiac MRI, mammography, and more.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <rect x="4" y="4" width="16" height="16" rx="2" />
        <path d="M4 9h16" />
        <path d="M10 9v11" />
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
          <ul className="rp-auth-trust" aria-label="Compliance highlights">
            <li className="rp-auth-trust-item">Tenant-isolated</li>
            <li className="rp-auth-trust-item">Append-only audit</li>
            <li className="rp-auth-trust-item">Never auto-signs</li>
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
