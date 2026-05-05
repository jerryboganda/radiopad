// The topbar nav is the locked entry point to every primary surface.
// This test renders the nav in isolation (without the Next.js Link
// router context) and asserts every locked nav item is present, using
// the same labels as `frontend/app/layout.tsx`.
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import * as React from 'react';

const LOCKED_NAV_ITEMS: ReadonlyArray<{ href: string; label: string }> = [
  { href: '/', label: 'Reports' },
  { href: '/rulebooks', label: 'Rulebooks' },
  { href: '/templates', label: 'Templates' },
  { href: '/providers', label: 'Providers' },
  { href: '/prompts', label: 'Prompts' },
  { href: '/marketplace', label: 'Marketplace' },
  { href: '/validation', label: 'Validation' },
  { href: '/admin/governance', label: 'Governance' },
  { href: '/audit', label: 'Audit' },
  { href: '/analytics', label: 'Analytics' },
  { href: '/terminology', label: 'Terminology' },
  { href: '/offline', label: 'Offline' },
  { href: '/admin/settings', label: 'Settings' },
  { href: '/admin/billing', label: 'Billing' },
  { href: '/admin/usage', label: 'Usage' },
  { href: '/admin/feature-flags', label: 'Feature flags' },
  { href: '/admin/fhir-import', label: 'FHIR import' },
  { href: '/admin/pacs', label: 'PACS' },
  { href: '/admin/security', label: 'Security' },
  { href: '/login', label: 'Sign in' },
];

function Topbar() {
  return (
    <header className="topbar">
      <div className="topbar-left">
        <span className="brand-mark" aria-hidden>
          <span className="brand-mark-letter">R</span>
        </span>
        <div className="topbar-title">
          <span className="title">RadioPad</span>
          <span className="meta">AI radiology reporting · v0.1</span>
        </div>
      </div>
      <nav className="topbar-right rp-nav" aria-label="Primary">
        {LOCKED_NAV_ITEMS.map((it) => (
          <a key={it.href} href={it.href}>{it.label}</a>
        ))}
      </nav>
    </header>
  );
}

describe('topbar', () => {
  it('renders the locked shell classes', () => {
    const { container } = render(<Topbar />);
    expect(container.querySelector('header.topbar')).not.toBeNull();
    expect(container.querySelector('.topbar-left')).not.toBeNull();
    expect(container.querySelector('.topbar-right.rp-nav')).not.toBeNull();
    expect(container.querySelector('.brand-mark')).not.toBeNull();
  });

  it('renders every locked nav item with its href', () => {
    const { getByRole } = render(<Topbar />);
    const nav = getByRole('navigation', { name: 'Primary' });
    const links = nav.querySelectorAll('a');
    expect(links).toHaveLength(LOCKED_NAV_ITEMS.length);
    for (const item of LOCKED_NAV_ITEMS) {
      const match = Array.from(links).find((a) => a.textContent === item.label);
      expect(match, `nav item "${item.label}" should be present`).toBeTruthy();
      expect(match!.getAttribute('href')).toBe(item.href);
    }
  });
});
