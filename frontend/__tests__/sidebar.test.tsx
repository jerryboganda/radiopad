// The sidebar nav is the locked entry point to every primary surface.
// This test asserts the locked IA — every primary route is present and
// grouped under the documented section labels (Workspace / Library /
// Integrations / Admin).
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
  { key: 'workspace', hrefs: ['/', '/validation', '/audit', '/analytics'] },
  { key: 'library', hrefs: ['/rulebooks', '/templates', '/prompts', '/marketplace', '/terminology'] },
  { key: 'integrations', hrefs: ['/providers', '/admin/pacs', '/admin/fhir-import', '/offline'] },
  { key: 'admin', hrefs: ['/admin/governance', '/admin/model-eval', '/admin/security', '/admin/feature-flags', '/admin/billing', '/admin/usage', '/admin/settings'] },
];

describe('sidebar nav config', () => {
  it('exposes the four locked groups in order', () => {
    expect(navGroups.map((g) => g.labelKey)).toEqual(LOCKED_GROUPS.map((g) => g.key));
  });

  it('places every locked route in its documented group', () => {
    for (const expected of LOCKED_GROUPS) {
      const group = navGroups.find((g) => g.labelKey === expected.key);
      expect(group, `group "${expected.key}" should exist`).toBeTruthy();
      expect(group!.items.map((i) => i.href)).toEqual(expected.hrefs);
    }
  });

  it('marks the Reports root active for / and /reports/* paths', () => {
    const reports = navGroups[0].items.find((i) => i.href === '/')!;
    expect(isActive('/', reports)).toBe(true);
    expect(isActive('/reports/abc', reports)).toBe(true);
    expect(isActive('/rulebooks', reports)).toBe(false);
  });

  it('marks a sub-section active for its own prefix', () => {
    const settings = navGroups[3].items.find((i) => i.href === '/admin/settings')!;
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
