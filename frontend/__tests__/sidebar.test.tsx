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
  { key: 'workspace', hrefs: ['/dashboard', '/worklist', '/reports/compose', '/templates', '/protocols', '/ai-assistant', '/findings-library'] },
  { key: 'insights', hrefs: ['/reports', '/analytics', '/quality', '/validation', '/audit'] },
  { key: 'library', hrefs: ['/rulebooks', '/modalities', '/body-parts', '/prompts', '/marketplace', '/terminology'] },
  { key: 'integrations', hrefs: ['/providers', '/admin/ubag', '/admin/pacs', '/admin/fhir-import', '/offline'] },
  { key: 'admin', hrefs: ['/admin/users', '/admin/governance', '/admin/model-eval', '/admin/security', '/admin/feature-flags', '/admin/billing', '/admin/usage', '/admin/settings'] },
  { key: 'account', hrefs: ['/settings', '/account/security'] },
];

describe('sidebar nav config', () => {
  it('exposes the six locked groups in order', () => {
    expect(navGroups.map((g) => g.labelKey)).toEqual(LOCKED_GROUPS.map((g) => g.key));
  });

  it('places every locked route in its documented group', () => {
    for (const expected of LOCKED_GROUPS) {
      const group = navGroups.find((g) => g.labelKey === expected.key);
      expect(group, `group "${expected.key}" should exist`).toBeTruthy();
      expect(group!.items.map((i) => i.href)).toEqual(expected.hrefs);
    }
  });

  it('marks the Report Composer item active for compose and editor routes', () => {
    const composer = navGroups[0].items.find((i) => i.href === '/reports/compose')!;
    expect(isActive('/reports/compose', composer)).toBe(true);
    expect(isActive('/reports/view', composer)).toBe(true);
    expect(isActive('/reports', composer)).toBe(false);
  });

  it('keeps Reports active for the archive but not composer routes', () => {
    const reports = navGroups[1].items.find((i) => i.href === '/reports')!;
    expect(isActive('/reports', reports)).toBe(true);
    expect(isActive('/reports/new', reports)).toBe(true);
    expect(isActive('/reports/view', reports)).toBe(false);
    expect(isActive('/reports/compose', reports)).toBe(false);
    expect(isActive('/rulebooks', reports)).toBe(false);
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
