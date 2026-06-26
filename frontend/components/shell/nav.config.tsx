/**
 * Single source of truth for primary navigation IA.
 * Update i18n keys in messages/<locale>.json for labels.
 */

import type { ComponentType, SVGProps } from 'react';
import {
  FileText, ClipboardCheck, ScrollText, BarChart3, BookOpen, LayoutTemplate,
  MessageSquareText, Bot, Store, BookText, Server, Network, HardDrive,
  FileInput, WifiOff, Scale, FlaskConical, ShieldCheck, Flag, CreditCard,
  Activity, Settings2, Fingerprint, Users,
} from 'lucide-react';
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

// Hallmark icon set — lucide-react, matching UBAG's icon vocabulary.
export const Icons: Record<string, NavIcon> = {
  reports: FileText,
  validation: ClipboardCheck,
  audit: ScrollText,
  analytics: BarChart3,
  rulebooks: BookOpen,
  templates: LayoutTemplate,
  prompts: MessageSquareText,
  copilot: Bot,
  marketplace: Store,
  terminology: BookText,
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
    labelKey: 'workspace',
    items: [
      { href: '/', labelKey: 'reports', icon: Icons.reports, permission: 'reports.read' },
      { href: '/validation', labelKey: 'validation', icon: Icons.validation, permission: 'validation_packs.read' },
      { href: '/audit', labelKey: 'audit', icon: Icons.audit, permission: 'audit.read' },
      { href: '/analytics', labelKey: 'analytics', icon: Icons.analytics, permission: 'reports.read' },
      { href: '/account/security', labelKey: 'signInDevices', icon: Icons.signInDevices },
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
      { href: '/admin/users', labelKey: 'users', icon: Icons.users, permission: 'users.manage' },
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
