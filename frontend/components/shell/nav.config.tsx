/**
 * Single source of truth for primary navigation IA.
 * Update i18n keys in messages/<locale>.json for labels.
 */

import type { ComponentType, SVGProps } from 'react';
import {
  FileText, ClipboardCheck, ScrollText, BarChart3, BookOpen, LayoutTemplate,
  MessageSquareText, Store, BookText, Server, Network, HardDrive,
  FileInput, WifiOff, Scale, FlaskConical, ShieldCheck, Flag, CreditCard,
  Activity, Settings2, Fingerprint, Users, ScanLine, Bone, Cpu,
  LayoutDashboard, ListTodo, PenLine, Layers, Sparkles, Library, BadgeCheck,
  GraduationCap, UsersRound, AlertTriangle, Bell,
} from 'lucide-react';
import type { PermissionKey } from '@/lib/permissions';
import { UBAG_HUB_ROLES } from '@/lib/roles';
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
   * Role ordinals (lib/roles `UserRole`) allowed to SEE this item. Used where
   * the backend gates by role rather than a permission key (e.g. UBAG Hub).
   * Omitted → no role restriction. The server still enforces.
   */
  roles?: readonly number[];
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

// RC icon set — lucide-react.
export const Icons: Record<string, NavIcon> = {
  dashboard: LayoutDashboard,
  worklist: ListTodo,
  criticalResults: AlertTriangle,
  composer: PenLine,
  protocols: Layers,
  aiAssistant: Sparkles,
  findingsLibrary: Library,
  quality: BadgeCheck,
  peerReview: UsersRound,
  notifications: Bell,
  reports: FileText,
  validation: ClipboardCheck,
  audit: ScrollText,
  analytics: BarChart3,
  rulebooks: BookOpen,
  templates: LayoutTemplate,
  prompts: MessageSquareText,
  marketplace: Store,
  terminology: BookText,
  teaching: GraduationCap,
  modalities: ScanLine,
  bodyParts: Bone,
  providers: Server,
  ubag: Network,
  pacs: HardDrive,
  onDeviceModels: Cpu,
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
    // RC primary group (mockup order): the clinical reporting flow.
    labelKey: 'workspace',
    surfaces: ['desktop'],
    items: [
      { href: '/dashboard', labelKey: 'dashboard', icon: Icons.dashboard, permission: 'reports.read' },
      { href: '/worklist', labelKey: 'worklist', icon: Icons.worklist, permission: 'reports.read' },
      // PRD §14.15 — the open critical-communication loops are an actionable queue,
      // so they sit with the reporting flow rather than the read-only insight group.
      { href: '/critical-results', labelKey: 'criticalResults', icon: Icons.criticalResults, permission: 'critical_results.read' },
      // NOTIF-001 — the personal notifications inbox. Desktop-only page (the inbox
      // route ships only in the reporting product); web admins use the topbar bell.
      { href: '/notifications', labelKey: 'notifications', icon: Icons.notifications, surfaces: ['desktop'] },
      { href: '/reports/compose', labelKey: 'composer', icon: Icons.composer, permission: 'reports.draft', matchPrefix: '/reports/view' },
      { href: '/templates', labelKey: 'templates', icon: Icons.templates, permission: 'templates.read' },
      { href: '/protocols', labelKey: 'protocols', icon: Icons.protocols },
      { href: '/ai-assistant', labelKey: 'aiAssistant', icon: Icons.aiAssistant },
      { href: '/findings-library', labelKey: 'findingsLibrary', icon: Icons.findingsLibrary, permission: 'templates.read' },
    ],
  },
  {
    // RC second group: records & insight.
    labelKey: 'insights',
    surfaces: ['desktop'],
    items: [
      { href: '/reports', labelKey: 'reports', icon: Icons.reports, permission: 'reports.read' },
      { href: '/analytics', labelKey: 'analytics', icon: Icons.analytics, permission: 'reports.read' },
      { href: '/quality', labelKey: 'quality', icon: Icons.quality, permission: 'reports.read' },
      { href: '/peer-review', labelKey: 'peerReview', icon: Icons.peerReview, permission: 'peer_review.read' },
      { href: '/validation', labelKey: 'validation', icon: Icons.validation, permission: 'validation_packs.read' },
      { href: '/audit', labelKey: 'audit', icon: Icons.audit, permission: 'audit.read' },
    ],
  },
  {
    // Clinical content authoring — part of the reporting product (desktop).
    labelKey: 'library',
    surfaces: ['desktop'],
    items: [
      { href: '/rulebooks', labelKey: 'rulebooks', icon: Icons.rulebooks, permission: 'rulebooks.read' },
      { href: '/modalities', labelKey: 'modalities', icon: Icons.modalities, permission: 'modalities.read' },
      { href: '/body-parts', labelKey: 'bodyParts', icon: Icons.bodyParts, permission: 'body_parts.read' },
      { href: '/prompts', labelKey: 'prompts', icon: Icons.prompts, permission: 'prompt_overrides.manage' },
      { href: '/marketplace', labelKey: 'marketplace', icon: Icons.marketplace },
      { href: '/terminology', labelKey: 'terminology', icon: Icons.terminology },
      // PRD §14.14 (TF-001..008) — the de-identified teaching library. Desktop
      // only: it is authored from reports, which live in the reporting product.
      {
        href: '/teaching',
        labelKey: 'teaching',
        icon: Icons.teaching,
        permission: 'teaching_cases.read',
        matchPrefix: '/teaching',
      },
    ],
  },
  {
    labelKey: 'integrations',
    items: [
      // Provider / PACS / MCP wiring is platform administration → web.
      { href: '/providers', labelKey: 'providers', icon: Icons.providers, permission: 'providers.read', surfaces: ['web'] },
      // Backend /api/ubag gates by ROLE (ItAdmin / ReportingAdmin / MedicalDirector
      // / ComplianceReviewer), not a permission key — mirror that here.
      { href: '/admin/ubag', labelKey: 'ubag', icon: Icons.ubag, roles: UBAG_HUB_ROLES, surfaces: ['web'] },
      { href: '/admin/pacs', labelKey: 'pacs', icon: Icons.pacs, permission: 'tenant_settings.manage', surfaces: ['web'] },
      // FHIR import seeds a draft report → part of the reporting flow (desktop).
      { href: '/admin/fhir-import', labelKey: 'fhirImport', icon: Icons.fhir, permission: 'reports.draft', surfaces: ['desktop'] },
      { href: '/offline', labelKey: 'offline', icon: Icons.offline, surfaces: ['desktop'] },
    ],
  },
  {
    // Master-admin / platform operations → web only ("Users & Teams" +
    // "System Settings" of the RC mockups live here on the admin surface).
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
  {
    // Personal account — desktop settings hub + shared sign-in devices.
    labelKey: 'account',
    items: [
      { href: '/settings', labelKey: 'settingsHub', icon: Icons.settings, surfaces: ['desktop'] },
      // Desktop-only: the on-device engines run HERE, so the manager must ship here. It lived
      // only under (web) and was staged out of the desktop bundle entirely.
      { href: '/settings/models', labelKey: 'onDeviceModels', icon: Icons.onDeviceModels, surfaces: ['desktop'] },
      { href: '/account/security', labelKey: 'signInDevices', icon: Icons.signInDevices },
    ],
  },
];

export function isActive(pathname: string, item: NavItem): boolean {
  if (item.matchPrefix && pathname.startsWith(item.matchPrefix)) return true;
  if (item.href === '/') return pathname === '/';
  // Keep "Reports" from claiming composer routes owned by the composer item.
  if (item.href === '/reports') {
    return pathname === '/reports' || pathname.startsWith('/reports/')
      ? !pathname.startsWith('/reports/view') && !pathname.startsWith('/reports/compose')
      : false;
  }
  return pathname === item.href || pathname.startsWith(item.href + '/');
}
