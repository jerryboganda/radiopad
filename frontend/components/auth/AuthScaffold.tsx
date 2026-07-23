'use client';

import type { ReactNode } from 'react';
import CheckUpdatesButton from '@/components/shell/CheckUpdatesButton';
import ThemeToggle from '@/components/ui/ThemeToggle';

/**
 * Split-screen scaffold for the public auth entrance (sign-in / register /
 * device pairing). Left pane is a branded showcase (hidden < 880px); the right
 * pane hosts the focused auth card. Locked design tokens only — see
 * docs/02-design/design.md §"Auth entrance".
 *
 * The left pane is an INTENTIONALLY-DARK surface in both themes (same category
 * as `.op-bash`): it is a branded showcase, not app chrome, so it keeps its
 * deep-navy identity while the app is in light mode. Its palette is declared as
 * theme-neutral locals on `.rp-auth-aside` in radiopad.css.
 */

export type AuthVariant = 'signin' | 'register' | 'pair';

/** Headline is split so the trailing clause can carry the accent colour. */
const COPY: Record<AuthVariant, { headline: string; accent: string; tagline: string }> = {
  signin: {
    headline: 'Report at the speed of',
    accent: 'thought.',
    tagline: 'Intelligent tools. Clinical accuracy. Built for radiologists, by radiologists.',
  },
  register: {
    headline: 'Start reporting in',
    accent: 'minutes.',
    tagline: 'Create your organization and bring your team. There is no password to manage — ever.',
  },
  pair: {
    headline: 'Pair this device',
    accent: 'securely.',
    tagline: 'Bind this desktop to your tenant with a one-time code. The device never sees a password.',
  },
};

const FEATURES = [
  {
    title: 'AI-assisted drafting',
    sub: 'Smarter suggestions, faster reports.',
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
    sub: 'Built-in clinical validation and checks.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
        <path d="M9 12l2 2 4-4" />
      </svg>
    ),
  },
  {
    title: 'Hands-free dictation',
    sub: 'Speak naturally. We handle the rest.',
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
    sub: 'Consistent, compliant, and customizable.',
    icon: (
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <rect x="4" y="4" width="16" height="16" rx="2" />
        <path d="M4 9h16" />
        <path d="M10 9v11" />
      </svg>
    ),
  },
];

const TRUST = [
  {
    label: 'Tenant-isolated',
    icon: (
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <rect x="5" y="11" width="14" height="9" rx="2" />
        <path d="M8 11V8a4 4 0 0 1 8 0v3" />
      </svg>
    ),
  },
  {
    label: 'Append-only audit',
    icon: (
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
        <path d="M9 12l2 2 4-4" />
      </svg>
    ),
  },
  {
    label: 'Never auto-signs',
    icon: (
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <rect x="4" y="10" width="16" height="10" rx="2" />
        <path d="M9 10V7a3 3 0 0 1 6 0v3" />
        <path d="M12 14v2" />
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

/**
 * Isometric "R" showcase mark — stacked glowing plates with an extruded
 * letterform. Hand-built SVG approximation of the brand render; purely
 * decorative, so it is aria-hidden.
 */
function ShowcaseMark() {
  return (
    <div className="rp-auth-illus" aria-hidden>
      <svg viewBox="0 0 560 560" className="rp-auth-illus-svg">
        <defs>
          <linearGradient id="rp-illus-plate" x1="0" y1="0" x2="0.9" y2="1">
            <stop offset="0" stopColor="#5cb0ff" />
            <stop offset="1" stopColor="#1552a8" />
          </linearGradient>
          <linearGradient id="rp-illus-edge" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#8fd0ff" />
            <stop offset="1" stopColor="#2f88d8" />
          </linearGradient>
          <filter id="rp-illus-glow" x="-60%" y="-60%" width="220%" height="220%">
            <feGaussianBlur stdDeviation="14" result="blur" />
            <feMerge>
              <feMergeNode in="blur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
          <filter id="rp-illus-soft" x="-60%" y="-60%" width="220%" height="220%">
            <feGaussianBlur stdDeviation="26" />
          </filter>
        </defs>

        {/* ambient bloom behind the whole mark */}
        <ellipse cx="280" cy="330" rx="190" ry="150" fill="#5cb0ff" opacity="0.2" filter="url(#rp-illus-soft)" />

        {/* stacked isometric plates, back to front */}
        <g transform="translate(280 392)">
          <g transform="matrix(0.94 0.42 -0.94 0.42 0 0)">
            <rect x="-168" y="-168" width="336" height="336" rx="30" fill="url(#rp-illus-plate)" opacity="0.16" />
            <rect x="-168" y="-168" width="336" height="336" rx="30" fill="none" stroke="url(#rp-illus-edge)" strokeWidth="2.5" opacity="0.45" />
          </g>
        </g>
        <g transform="translate(280 336)">
          <g transform="matrix(0.94 0.42 -0.94 0.42 0 0)">
            <rect x="-134" y="-134" width="268" height="268" rx="26" fill="url(#rp-illus-plate)" opacity="0.3" />
            <rect x="-134" y="-134" width="268" height="268" rx="26" fill="none" stroke="url(#rp-illus-edge)" strokeWidth="2.5" opacity="0.7" />
          </g>
        </g>
        <g transform="translate(280 288)">
          <g transform="matrix(0.94 0.42 -0.94 0.42 0 0)">
            <rect x="-104" y="-104" width="208" height="208" rx="22" fill="url(#rp-illus-plate)" opacity="0.5" />
            <rect x="-104" y="-104" width="208" height="208" rx="22" fill="none" stroke="url(#rp-illus-edge)" strokeWidth="3" opacity="0.95" />
          </g>
        </g>

        {/* extrusion trail, then the lit face */}
        <g className="rp-auth-illus-letter" filter="url(#rp-illus-glow)">
          <text x="266" y="272" opacity="0.16">R</text>
          <text x="271" y="267" opacity="0.26">R</text>
          <text x="276" y="262" opacity="0.36">R</text>
          <text x="281" y="257" className="rp-auth-illus-face">R</text>
        </g>
      </svg>
    </div>
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
        <ShowcaseMark />

        <div className="rp-auth-brand">
          <BrandMark />
          <span className="rp-auth-brand-text">
            <span className="rp-auth-brand-name">RadioPad</span>
            <span className="rp-auth-brand-kicker">AI-assisted radiology reporting</span>
          </span>
        </div>

        <div className="rp-auth-aside-body">
          <h2 className="rp-auth-headline">
            {copy.headline} <span className="rp-auth-headline-accent">{copy.accent}</span>
          </h2>
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

        <ul className="rp-auth-trust" aria-label="Compliance highlights">
          {TRUST.map((t) => (
            <li className="rp-auth-trust-item" key={t.label}>
              <span className="rp-auth-trust-icon">{t.icon}</span>
              {t.label}
            </li>
          ))}
        </ul>
      </aside>

      <main className="rp-auth-main">
        {/* Pre-sign-in chrome: theme toggle (THEME-002 — visible on the sign-in
            screen) + desktop self-update (CheckUpdatesButton self-hides on
            web/mobile). */}
        <div className="rp-auth-update">
          <ThemeToggle />
          <CheckUpdatesButton />
        </div>
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
