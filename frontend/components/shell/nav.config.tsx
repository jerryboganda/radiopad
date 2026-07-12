/**
 * Single source of truth for primary navigation IA.
 * Update i18n keys in messages/<locale>.json for labels.
 */

import type { ComponentType, SVGProps } from 'react';
import {
  FileText, ClipboardCheck, ScrollText, BarChart3, BookOpen, LayoutTemplate,
  MessageSquareText, Store, BookText, Server, Network, HardDrive,
  FileInput, WifiOff, Scale, FlaskConical, ShieldCheck, Flag, CreditCard,
  Activity, Settings2, Fingerprint, Users, ScanLine, Bone,
} from 'lucide-react';
import type { PermissionKey } from '@/lib/permissions';
import type { Surface } from '@/lib/surface';

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
  /**
   * Surfaces this item belongs on (`desktop` = reporting product, `web` =
   * master admin, `mobile` = companion). Omitted → inherits the group's
   * `surfaces`; omitted on both → visible on every surface (shared).
   */
  surfaces?: readonly Surface[];
}

export interface NavGroup {
  /** i18n key under `nav.groups.*` */
  labelKey: string;
  /** Default surfaces for items in this group; each item may override. */
  surfaces?: readonly Surface[];
  items: NavItem[];
}

// Hallmark icon set — lucide-react, matching UBAG's icon vocabulary.
export const Icons: Record<string, NavIcon> = {
  reports: FileText,
  validation: ClipboardCheck,
  audit: ScrollText,
  analytics: BarChart3,
  rulebooks: BookOpen,
  templates: LayoutTemplate,
  prompts: MessageSquareText,
  marketplace: Store,
  terminology: BookText,
  modalities: ScanLine,
  bodyParts: Bone,
  providers: Server,
  ubag: Network,
  pacs: HardDrive,
  fhir: FileInput,
  offline: WifiOff,
  governance: Scale,
  modelEval: FlaskConical,
  security: ShieldCheck,
  flags: Flag,
  billing: CreditCard,
  usage: Activity,
  settings: Settings2,
  signInDevices: Fingerprint,
  users: Users,
};

export const navGroups: NavGroup[] = [
  {
    // Reporting workspace lives on the desktop app; `account/security` is
    // shared so every surface can reach sign-in-devices / personal security.
    labelKey: 'workspace',
    items: [
      { href: '/', labelKey: 'reports', icon: Icons.reports, permission: 'reports.read', surfaces: ['desktop'] },
      { href: '/validation', labelKey: 'validation', icon: Icons.validation, permission: 'validation_packs.read', surfaces: ['desktop'] },
      // Audit trail list lives in the (desktop) group only — tag it desktop so
      // the web sidebar never links to a route staged out of the web bundle.
      // Admin audit on web is served by /admin/governance + /admin/usage.
      { href: '/audit', labelKey: 'audit', icon: Icons.audit, permission: 'audit.read', surfaces: ['desktop'] },
      { href: '/analytics', labelKey: 'analytics', icon: Icons.analytics, permission: 'reports.read', surfaces: ['desktop'] },
      { href: '/account/security', labelKey: 'signInDevices', icon: Icons.signInDevices },
    ],
  },
  {
    // Clinical content authoring — part of the reporting product (desktop).
    labelKey: 'library',
    surfaces: ['desktop'],
    items: [
      { href: '/rulebooks', labelKey: 'rulebooks', icon: Icons.rulebooks, permission: 'rulebooks.read' },
      { href: '/templates', labelKey: 'templates', icon: Icons.templates, permission: 'templates.read' },
      { href: '/modalities', labelKey: 'modalities', icon: Icons.modalities, permission: 'modalities.read' },
      { href: '/body-parts', labelKey: 'bodyParts', icon: Icons.bodyParts, permission: 'body_parts.read' },
      { href: '/prompts', labelKey: 'prompts', icon: Icons.prompts, permission: 'prompt_overrides.manage' },
      { href: '/marketplace', labelKey: 'marketplace', icon: Icons.marketplace },
      { href: '/terminology', labelKey: 'terminology', icon: Icons.terminology },
    ],
  },
  {
    labelKey: 'integrations',
    items: [
      // Provider / PACS / MCP wiring is platform administration → web.
      { href: '/providers', labelKey: 'providers', icon: Icons.providers, permission: 'providers.read', surfaces: ['web'] },
      { href: '/admin/ubag', labelKey: 'ubag', icon: Icons.ubag, permission: 'mcp_tools.invoke', surfaces: ['web'] },
      { href: '/admin/pacs', labelKey: 'pacs', icon: Icons.pacs, permission: 'tenant_settings.manage', surfaces: ['web'] },
      // FHIR import seeds a draft report → part of the reporting flow (desktop).
      { href: '/admin/fhir-import', labelKey: 'fhirImport', icon: Icons.fhir, permission: 'reports.draft', surfaces: ['desktop'] },
      { href: '/offline', labelKey: 'offline', icon: Icons.offline, surfaces: ['desktop'] },
    ],
  },
  {
    // Master-admin / platform operations → web only.
    labelKey: 'admin',
    surfaces: ['web'],
    items: [
      { href: '/admin/users', labelKey: 'users', icon: Icons.users, permission: 'users.manage' },
      { href: '/admin/governance', labelKey: 'governance', icon: Icons.governance, permission: 'audit.verify' },
      { href: '/admin/model-eval', labelKey: 'modelEval', icon: Icons.modelEval, permission: 'audit.verify' },
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
