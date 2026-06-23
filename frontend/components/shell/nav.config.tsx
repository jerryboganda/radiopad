/**
 * Single source of truth for primary navigation IA.
 * Update i18n keys in messages/<locale>.json for labels.
 */

import type { ComponentType, SVGProps } from 'react';
import type { PermissionKey } from '@/lib/permissions';

export type NavIcon = ComponentType<SVGProps<SVGSVGElement>>;

export interface NavItem {
  href: string;
  /** i18n key under `nav.*` */
  labelKey: string;
  icon: NavIcon;
  /** Treat any path starting with this prefix as active. Defaults to exact + `/${href}/`. */
  matchPrefix?: string;
  /**
   * Permission key required to SEE this item. Omitted → always visible to any
   * signed-in user. Gating mirrors the backend RBAC (server still enforces).
   */
  permission?: PermissionKey;
}

export interface NavGroup {
  /** i18n key under `nav.groups.*` */
  labelKey: string;
  items: NavItem[];
}

const Icon = (path: string): NavIcon => {
  const C = (props: SVGProps<SVGSVGElement>) => (
    // eslint-disable-next-line jsx-a11y/aria-props
    <svg
      width={16}
      height={16}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      {...props}
    >
      <path d={path} />
    </svg>
  );
  C.displayName = `NavIcon(${path.slice(0, 12)})`;
  return C;
};

// Minimal hand-rolled icon set (no external icon dep).
export const Icons = {
  reports: Icon('M4 4h12l4 4v12H4z M14 4v6h6'),
  validation: Icon('M5 12l4 4L19 6'),
  audit: Icon('M4 6h16 M4 12h16 M4 18h10'),
  analytics: Icon('M4 20V10 M10 20V4 M16 20v-8 M22 20H2'),
  rulebooks: Icon('M5 4h11a3 3 0 013 3v13H8a3 3 0 01-3-3z M8 4v13'),
  templates: Icon('M4 5h16v4H4z M4 13h7v6H4z M14 13h6v6h-6z'),
  prompts: Icon('M5 6h14v10H8l-3 3z'),
  copilot: Icon('M4 5h16v12H8l-4 4z M8 9h8 M8 13h5'),
  marketplace: Icon('M3 7l1-3h16l1 3 M3 7v13h18V7 M3 7h18 M9 11v4 M15 11v4'),
  terminology: Icon('M4 4h11l5 5v11H4z M14 4v6h6 M8 14h8 M8 17h6'),
  providers: Icon('M3 9l9-6 9 6v11H3z M9 20v-7h6v7'),
  ubag: Icon('M4 8h16 M8 4v8 M16 4v8 M6 16h12 M10 12v8 M14 12v8'),
  pacs: Icon('M4 6h16v6H4z M4 14h16v4H4z M8 9h.01 M8 16h.01'),
  fhir: Icon('M12 2v8m0 0l-3-3m3 3l3-3 M4 14v6h16v-6'),
  offline: Icon('M5 12a7 7 0 0114 0 M9 16a3 3 0 016 0 M3 3l18 18'),
  governance: Icon('M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z'),
  modelEval: Icon('M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h6v6h-6z'),
  security: Icon('M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z M9 12l2 2 4-4'),
  flags: Icon('M5 21V4h12l-2 4 2 4H5'),
  billing: Icon('M3 7h18v10H3z M3 11h18 M7 15h3'),
  usage: Icon('M4 20V10 M10 20V4 M16 20v-8 M22 20H2'),
  settings: Icon('M12 15a3 3 0 100-6 3 3 0 000 6z M19 12l2-2-2-2-2 2 M5 12l-2-2 2-2 2 2 M12 5l-2-2-2 2 2 2 M12 19l-2 2 2 2 2-2'),
} as const;

export const navGroups: NavGroup[] = [
  {
    labelKey: 'workspace',
    items: [
      { href: '/', labelKey: 'reports', icon: Icons.reports, permission: 'reports.read' },
      { href: '/validation', labelKey: 'validation', icon: Icons.validation, permission: 'validation_packs.read' },
      { href: '/audit', labelKey: 'audit', icon: Icons.audit, permission: 'audit.read' },
      { href: '/analytics', labelKey: 'analytics', icon: Icons.analytics, permission: 'reports.read' },
    ],
  },
  {
    labelKey: 'library',
    items: [
      { href: '/rulebooks', labelKey: 'rulebooks', icon: Icons.rulebooks, permission: 'rulebooks.read' },
      { href: '/templates', labelKey: 'templates', icon: Icons.templates, permission: 'templates.read' },
      { href: '/prompts', labelKey: 'prompts', icon: Icons.prompts, permission: 'prompt_overrides.manage' },
      { href: '/marketplace', labelKey: 'marketplace', icon: Icons.marketplace },
      { href: '/terminology', labelKey: 'terminology', icon: Icons.terminology },
    ],
  },
  {
    labelKey: 'integrations',
    items: [
      { href: '/providers', labelKey: 'providers', icon: Icons.providers, permission: 'providers.read' },
      { href: '/admin/ubag', labelKey: 'ubag', icon: Icons.ubag, permission: 'mcp_tools.invoke' },
      { href: '/copilot', labelKey: 'copilot', icon: Icons.copilot },
      { href: '/admin/pacs', labelKey: 'pacs', icon: Icons.pacs, permission: 'tenant_settings.manage' },
      { href: '/admin/fhir-import', labelKey: 'fhirImport', icon: Icons.fhir, permission: 'reports.draft' },
      { href: '/offline', labelKey: 'offline', icon: Icons.offline },
    ],
  },
  {
    labelKey: 'admin',
    items: [
      { href: '/admin/governance', labelKey: 'governance', icon: Icons.governance, permission: 'audit.verify' },
      { href: '/admin/model-eval', labelKey: 'modelEval', icon: Icons.modelEval, permission: 'audit.verify' },
      { href: '/admin/copilot', labelKey: 'copilotAdmin', icon: Icons.copilot, permission: 'prompt_overrides.manage' },
      { href: '/admin/security', labelKey: 'security', icon: Icons.security, permission: 'security.manage' },
      { href: '/admin/feature-flags', labelKey: 'featureFlags', icon: Icons.flags, permission: 'billing.read' },
      { href: '/admin/billing', labelKey: 'billing', icon: Icons.billing, permission: 'billing.read' },
      { href: '/admin/usage', labelKey: 'usage', icon: Icons.usage, permission: 'audit.read' },
      { href: '/admin/settings', labelKey: 'settings', icon: Icons.settings, permission: 'tenant_settings.manage' },
    ],
  },
];

export function isActive(pathname: string, item: NavItem): boolean {
  if (item.href === '/') return pathname === '/' || pathname.startsWith('/reports');
  return pathname === item.href || pathname.startsWith(item.href + '/');
}
