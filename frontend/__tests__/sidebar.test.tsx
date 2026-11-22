// The sidebar nav is the locked entry point to every primary surface.
// This test asserts the locked RC IA (PRD v3.0 §20.8, RC-01 mockups) —
// every primary route is present and grouped under the documented section
// labels (Workspace / Insights / Library / Integrations / Admin / Account).
import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import * as React from 'react';
import { navGroups, isActive } from '@/components/shell/nav.config';

vi.mock('next/link', () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

const LOCKED_GROUPS: ReadonlyArray<{ key: string; hrefs: string[] }> = [
  // Owner decision 2026-11-21: a short always-visible reading flow, with every other
  // desktop page folded into the collapsible Library / Quality / Settings groups. Each
  // route stays pinned here — a primary surface that falls out of the nav is unreachable.
  { key: 'workspace', hrefs: ['/dashboard', '/worklist', '/critical-results', '/reports', '/ai-assistant'] },
  { key: 'library', hrefs: ['/templates', '/report-templates', '/findings-library', '/protocols', '/rulebooks', '/prompts', '/modalities', '/body-parts', '/terminology', '/teaching', '/marketplace'] },
  { key: 'quality', hrefs: ['/peer-review', '/quality', '/analytics', '/validation', '/audit'] },
  { key: 'integrations', hrefs: ['/providers', '/admin/ubag', '/admin/pacs'] },
  { key: 'admin', hrefs: ['/admin/users', '/admin/governance', '/admin/model-eval', '/admin/validation-packs', '/admin/security', '/admin/sso', '/admin/feature-flags', '/admin/billing', '/admin/usage', '/admin/settings'] },
  { key: 'settings', hrefs: ['/settings', '/settings/models', '/device-pairing', '/notifications', '/account/security', '/admin/fhir-import', '/offline'] },
  { key: 'account', hrefs: ['/account/security', '/companion', '/reporting'] },
];

describe('sidebar nav config', () => {
  it('exposes the locked groups in order', () => {
    expect(navGroups.map((g) => g.labelKey)).toEqual(LOCKED_GROUPS.map((g) => g.key));
  });

  it('places every locked route in its documented group', () => {
    for (const expected of LOCKED_GROUPS) {
      const group = navGroups.find((g) => g.labelKey === expected.key);
      expect(group, `group "${expected.key}" should exist`).toBeTruthy();
      expect(group!.items.map((i) => i.href)).toEqual(expected.hrefs);
    }
  });

  it('keeps Reports active for the archive, intake and editor routes', () => {
    const reports = navGroups[0].items.find((i) => i.href === '/reports')!;
    expect(isActive('/reports', reports)).toBe(true);
    expect(isActive('/reports/new', reports)).toBe(true);
    expect(isActive('/reports/view', reports)).toBe(true);
    expect(isActive('/rulebooks', reports)).toBe(false);
  });

  it('folds only the desktop Library, Quality and Settings groups', () => {
    expect(navGroups.filter((g) => g.collapsible).map((g) => g.labelKey)).toEqual(['library', 'quality', 'settings']);
    for (const g of navGroups.filter((x) => x.collapsible)) expect(g.icon).toBeTruthy();
  });

  it('marks a sub-section active for its own prefix', () => {
    const admin = navGroups.find((g) => g.labelKey === 'admin')!;
    const settings = admin.items.find((i) => i.href === '/admin/settings')!;
    expect(isActive('/admin/settings', settings)).toBe(true);
    expect(isActive('/admin/settings/general', settings)).toBe(true);
    expect(isActive('/admin/billing', settings)).toBe(false);
  });
});

describe('sidebar nav config — render smoke', () => {
  it('every group icon component renders without crashing', () => {
    for (const group of navGroups) {
      for (const item of group.items) {
        const Icon = item.icon;
        const { container } = render(<Icon />);
        expect(container.querySelector('svg')).not.toBeNull();
      }
    }
  });
});
